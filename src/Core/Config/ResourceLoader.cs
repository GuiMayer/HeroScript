using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Logging;

namespace Core.Config
{
    /// <summary>
    /// Loader universal de recursos JSON com suporte a herança delta.
    /// Funciona como um mod loader genérico para qualquer tipo de recurso do jogo.
    /// </summary>
    public class ResourceLoader
    {
        private static ResourceLoader? _instance;
        private static readonly object _instanceLock = new();

        // Cache: relativePath -> (resourceId -> JsonElement)
        private readonly Dictionary<string, Dictionary<string, JsonElement>> _cache = new();
        
        // Cache de origens: relativePath -> (resourceId -> configName)
        private readonly Dictionary<string, Dictionary<string, string>> _originCache = new();
        
        private readonly object _cacheLock = new();
        private readonly ILogger _logger;

        // NOVO: Resolver de caminhos
        private ResourcePathResolver? _pathResolver;

        /// <summary>
        /// Singleton instance (for backward compatibility)
        /// </summary>
        public static ResourceLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new ResourceLoader();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Constructor for dependency injection
        /// </summary>
        public ResourceLoader(ILogger? logger = null)
        {
            _logger = logger ?? CoreLogger.Current;
            // Inicializar com configuração padrão
            InitializePathResolver(new ResourceConfiguration());
        }

        /// <summary>
        /// Permite reconfigurar o resolver (útil para testes)
        /// </summary>
        public void InitializePathResolver(ResourceConfiguration config)
        {
            lock (_cacheLock)
            {
                _pathResolver = ResourceProviderFactory.CreateResolver(config);
                InvalidateCache(); // Limpar cache ao reconfigurar
            }
        }

        /// <summary>
        /// Carrega um recurso JSON com herança delta.
        /// </summary>
        /// <param name="relativePath">Caminho relativo do recurso (ex: "Pipelines/MathFormulas.json")</param>
        /// <param name="configChain">Cadeia de herança (base → mod)</param>
        /// <param name="strictMode">Se true, erros de delta causam exceções</param>
        /// <returns>Dicionário de recursos merged</returns>
        public Dictionary<string, JsonElement> LoadResource(
            string relativePath,
            IEnumerable<string> configChain,
            bool strictMode = false)
        {
            lock (_cacheLock)
            {
                // Verificar cache
                if (_cache.TryGetValue(relativePath, out var cached))
                    return cached;

                _logger.LogDebug($"Loading resource: {relativePath}");
                _logger.LogDebug($"Config chain: {string.Join(" -> ", configChain)}");

                var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Carregar e fazer merge de cada config na cadeia
                foreach (var configName in configChain)
                {
                    // NOVO: Construir caminho relativo incluindo config
                    var configRelativePath = Path.Combine(configName, "Resources", relativePath);

                    // NOVO: Usar resolver para encontrar arquivo
                    var stream = _pathResolver!.OpenResource(configRelativePath, out var result);

                    if (stream == null)
                    {
                        _logger.LogDebug($"Resource not found for '{configName}': {relativePath}");
                        if (result.SearchedLocations.Count > 0)
                        {
                            _logger.LogDebug($"Searched locations:");
                            foreach (var location in result.SearchedLocations)
                            {
                                _logger.LogDebug($"  - {location}");
                            }
                        }
                        continue;
                    }

                    // Carregar e fazer merge
                    try
                    {
                        using (stream)
                        {
                            var resources = LoadFromStream(stream, configName, strictMode);
                            MergeResources(merged, origins, resources, configName, strictMode);
                            _logger.LogDebug($"Loaded {resources.Count} resources from {relativePath} ({configName})");
                            _logger.LogDebug($"Source: {result.Provider!.Name} - {result.PhysicalPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error loading {configRelativePath}: {ex.Message}", ex);
                        if (strictMode)
                            throw;
                    }
                }

                // Cachear resultado
                _cache[relativePath] = merged;
                _originCache[relativePath] = origins;

                _logger.LogInformation($"Loaded {merged.Count} resources total for {relativePath}");
                return merged;
            }
        }

        /// <summary>
        /// Carrega recursos de um stream JSON.
        /// </summary>
        private Dictionary<string, JsonElement> LoadFromStream(
            Stream stream,
            string configName,
            bool strictMode)
        {
            using var reader = new StreamReader(stream);
            var jsonContent = reader.ReadToEnd();
            _logger.LogDebug($"File content length: {jsonContent.Length} bytes");

            var rawDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);

            if (rawDict == null)
                throw new InvalidOperationException($"Failed to deserialize resource from {configName}");

            _logger.LogDebug($"Deserialized {rawDict.Count} resources from file");
            foreach (var key in rawDict.Keys)
            {
                _logger.LogDebug($"  - {key}");
            }

            return rawDict;
        }

        /// <summary>
        /// Faz merge de recursos usando operações delta.
        /// </summary>
        private void MergeResources(
            Dictionary<string, JsonElement> merged,
            Dictionary<string, string> origins,
            Dictionary<string, JsonElement> newResources,
            string configName,
            bool strictMode)
        {
            foreach (var kvp in newResources)
            {
                var resourceId = kvp.Key;
                var resourceElement = kvp.Value;

                // Tentar deserializar como DeltaDefinition
                Delta.DeltaDefinition? delta = null;
                try
                {
                    delta = JsonSerializer.Deserialize<Delta.DeltaDefinition>(resourceElement.GetRawText());
                }
                catch
                {
                    // Se falhar, trata como REPLACE legado
                }

                // Se não é delta estruturado, trata como REPLACE legado
                if (delta == null || !delta.IsStructuredDelta())
                {
                    // Formato legado: substitui recurso inteiro
                    if (merged.ContainsKey(resourceId))
                    {
                        _logger.LogDebug($"Override: {resourceId} (from {configName}) [legacy format]");
                    }

                    merged[resourceId] = resourceElement.Clone();
                    origins[resourceId] = configName;
                    continue;
                }

                // Delta estruturado: validar e aplicar
                var validation = Delta.DeltaValidator.Validate(resourceId, delta, strictMode);

                // Logar warnings
                foreach (var warning in validation.Warnings)
                {
                    _logger.LogWarning(warning);
                }

                // Logar erros
                if (!validation.IsValid)
                {
                    foreach (var error in validation.Errors)
                    {
                        _logger.LogError(error);
                    }

                    if (strictMode)
                        throw new InvalidOperationException($"Delta validation failed for '{resourceId}'");

                    _logger.LogWarning($"Skipping invalid delta for '{resourceId}'");
                    continue;
                }

                // Aplicar delta
                var baseValue = merged.ContainsKey(resourceId) ? merged[resourceId] : (JsonElement?)null;
                var operation = delta.GetOperationOrDefault();

                try
                {
                    var result = Delta.DeltaMerger.ApplyDelta(baseValue, delta, resourceId, strictMode);

                    if (result.HasValue)
                    {
                        // Recurso modificado ou adicionado
                        if (merged.ContainsKey(resourceId))
                        {
                            _logger.LogDebug($"Delta {operation}: {resourceId} (from {configName})");
                        }
                        else
                        {
                            _logger.LogDebug($"New resource: {resourceId} (from {configName})");
                        }

                        merged[resourceId] = result.Value;
                        origins[resourceId] = configName;
                    }
                    else
                    {
                        // DELETE: remover recurso
                        if (merged.Remove(resourceId))
                        {
                            _logger.LogDebug($"Deleted: {resourceId} (from {configName})");
                            origins.Remove(resourceId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error applying delta for '{resourceId}': {ex.Message}", ex);
                    if (strictMode)
                        throw;
                }
            }
        }

        /// <summary>
        /// Invalida cache de um recurso específico ou todos.
        /// </summary>
        public void InvalidateCache(string? relativePath = null)
        {
            lock (_cacheLock)
            {
                if (relativePath != null)
                {
                    _cache.Remove(relativePath);
                    _originCache.Remove(relativePath);
                    _logger.LogInformation($"Cache invalidated for: {relativePath}");
                }
                else
                {
                    _cache.Clear();
                    _originCache.Clear();
                    _logger.LogInformation("All cache invalidated");
                }
            }
        }

        /// <summary>
        /// Retorna de qual config cada recurso veio (para introspecção).
        /// </summary>
        public Dictionary<string, string> GetResourceOrigins(string relativePath)
        {
            lock (_cacheLock)
            {
                return _originCache.TryGetValue(relativePath, out var origins)
                    ? new Dictionary<string, string>(origins)
                    : new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Verifica se um recurso está em cache.
        /// </summary>
        public bool IsCached(string relativePath)
        {
            lock (_cacheLock)
            {
                return _cache.ContainsKey(relativePath);
            }
        }
    }
}
