using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Logging;

namespace Core.Config
{
    /// <summary>
    /// Loader universal de recursos JSON com suporte a herança delta.
    /// Funciona como um mod loader genérico para qualquer tipo de recurso do jogo.
    /// </summary>
    public class ResourceLoader : IResourceLoader
    {
        // Cache: resource path + resolved config chain -> (resourceId -> JsonElement)
        private readonly Dictionary<ResourceCacheKey, Dictionary<string, JsonElement>> _cache = new();
        
        // Cache de origens: resource path + resolved config chain -> (resourceId -> configName)
        private readonly Dictionary<ResourceCacheKey, Dictionary<string, string>> _originCache = new();
        
        private readonly object _cacheLock = new();
        private readonly ILogger _logger;

        // NOVO: Resolver de caminhos
        private ResourcePathResolver? _pathResolver;
        private readonly ResourceProviderFactory _providerFactory;

        /// <summary>
        /// Constructor for dependency injection
        /// </summary>
        public ResourceLoader(ILogger logger, ResourceProviderFactory providerFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
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
                _pathResolver = _providerFactory.CreateResolver(config);
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
                var chain = configChain.ToArray();
                var cacheKey = ResourceCacheKey.From(relativePath, chain);

                // Verificar cache
                if (_cache.TryGetValue(cacheKey, out var cached))
                    return cached;

                _logger.LogDebug($"Loading resource: {relativePath}");
                _logger.LogDebug($"Config chain: {string.Join(" -> ", chain)}");

                var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Core resources live directly below the provider root, while
                // game configs live under <config>/Resources. Load the core
                // baseline first so the config chain can deterministically
                // override it with deltas.
                var coreStream = _pathResolver!.OpenResource(relativePath, out var coreResult);
                if (coreStream != null)
                {
                    try
                    {
                        using (coreStream)
                        {
                            var resources = LoadFromStream(coreStream, "core", strictMode);
                            MergeResources(merged, origins, resources, "core", strictMode);
                            _logger.LogDebug($"Loaded {resources.Count} core resources from {relativePath}");
                            _logger.LogDebug($"Source: {coreResult.Provider!.Name} - {coreResult.PhysicalPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error loading core resource {relativePath}: {ex.Message}", ex);
                        if (strictMode)
                            throw;
                    }
                }

                // Carregar e fazer merge de cada config na cadeia
                foreach (var configName in chain)
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
                _cache[cacheKey] = merged;
                _originCache[cacheKey] = origins;

                _logger.LogInformation($"Loaded {merged.Count} resources total for {relativePath}");
                return merged;
            }
        }

        /// <summary>
        /// Asynchronously loads a JSON resource with delta inheritance.
        /// </summary>
        /// <param name="relativePath">Relative path to the resource (e.g., "Pipelines/MathFormulas.json")</param>
        /// <param name="configChain">Inheritance chain (base → mod)</param>
        /// <param name="strictMode">If true, delta errors cause exceptions</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary of merged resources</returns>
        public async Task<Dictionary<string, JsonElement>> LoadResourceAsync(
            string relativePath,
            IEnumerable<string> configChain,
            bool strictMode = false,
            CancellationToken cancellationToken = default)
        {
            var chain = configChain.ToArray();
            var cacheKey = ResourceCacheKey.From(relativePath, chain);

            // Check cache first (synchronous)
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(cacheKey, out var cached))
                    return cached;
            }

            _logger.LogDebug($"Loading resource async: {relativePath}");
            _logger.LogDebug($"Config chain: {string.Join(" -> ", chain)}");

            var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var coreStream = _pathResolver!.OpenResource(relativePath, out var coreResult);
            if (coreStream != null)
            {
                try
                {
                    using (coreStream)
                    {
                        var resources = await LoadFromStreamAsync(
                            coreStream,
                            "core",
                            strictMode,
                            cancellationToken);
                        MergeResources(merged, origins, resources, "core", strictMode);
                        _logger.LogDebug($"Loaded {resources.Count} core resources from {relativePath}");
                        _logger.LogDebug($"Source: {coreResult.Provider!.Name} - {coreResult.PhysicalPath}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error loading core resource {relativePath}: {ex.Message}", ex);
                    if (strictMode)
                        throw;
                }
            }

            // Load and merge each config in the chain
            foreach (var configName in chain)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var configRelativePath = Path.Combine(configName, "Resources", relativePath);
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

                // Load and merge asynchronously
                try
                {
                    using (stream)
                    {
                        var resources = await LoadFromStreamAsync(stream, configName, strictMode, cancellationToken);
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

            // Cache result
            lock (_cacheLock)
            {
                _cache[cacheKey] = merged;
                _originCache[cacheKey] = origins;
            }

            _logger.LogInformation($"Loaded {merged.Count} resources total for {relativePath}");
            return merged;
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
        /// Asynchronously loads resources from a JSON stream.
        /// </summary>
        private async Task<Dictionary<string, JsonElement>> LoadFromStreamAsync(
            Stream stream,
            string configName,
            bool strictMode,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream);
            var jsonContent = await reader.ReadToEndAsync(cancellationToken);
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
                    foreach (var key in _cache.Keys.Where(k => k.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase)).ToList())
                    {
                        _cache.Remove(key);
                        _originCache.Remove(key);
                    }
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
        /// Implementação da interface IResourceLoader - invalida todo o cache
        /// </summary>
        void IResourceLoader.InvalidateCache()
        {
            InvalidateCache(null);
        }

        /// <summary>
        /// Gets cache statistics for monitoring and diagnostics.
        /// </summary>
        public Dictionary<string, object> GetCacheStats()
        {
            lock (_cacheLock)
            {
                int totalResources = 0;
                int totalResourceIds = 0;

                foreach (var resourceDict in _cache.Values)
                {
                    totalResources++;
                    totalResourceIds += resourceDict.Count;
                }

                return new Dictionary<string, object>
                {
                    ["CachedResources"] = totalResources,
                    ["TotalResourceIds"] = totalResourceIds,
                    ["CachedPaths"] = _cache.Keys.Select(k => k.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    ["CachedProfiles"] = _cache.Keys.Select(k => k.ConfigChainKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };
            }
        }

        /// <summary>
        /// Retorna de qual config cada recurso veio (para introspecção).
        /// </summary>
        public Dictionary<string, string> GetResourceOrigins(string relativePath)
        {
            lock (_cacheLock)
            {
                var key = _originCache.Keys.LastOrDefault(k => k.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
                return key != null && _originCache.TryGetValue(key, out var origins)
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
                return _cache.Keys.Any(k => k.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Discovers all available resource files in a directory across the configuration chain.
        /// </summary>
        public IEnumerable<string> DiscoverResources(
            string relativeDirectory,
            IEnumerable<string> configChain,
            string filePattern = "*.json")
        {
            if (_pathResolver == null)
                throw new InvalidOperationException("PathResolver not initialized");

            var discoveredFiles = new HashSet<string>();

            // Traverse the config chain from base to most specific
            foreach (var configName in configChain.Reverse())
            {
                try
                {
                    var configDirectory = Path.Combine(configName, "Resources", relativeDirectory);
                    foreach (var directoryPath in _pathResolver.GetExistingPhysicalDirectories(configDirectory))
                    {
                        var files = Directory.GetFiles(directoryPath, filePattern, SearchOption.TopDirectoryOnly);

                        foreach (var file in files)
                        {
                            var fileName = Path.GetFileNameWithoutExtension(file);
                            discoveredFiles.Add(fileName);
                            _logger.LogDebug($"Discovered resource: {fileName} in config '{configName}'");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error discovering resources in config '{configName}': {ex.Message}");
                }
            }

            return discoveredFiles.OrderBy(f => f).ToList();
        }
    }

    internal sealed record ResourceCacheKey(string RelativePath, string ConfigChainKey)
    {
        public static ResourceCacheKey From(string relativePath, IReadOnlyList<string> configChain)
        {
            var chainKey = string.Join("|", configChain.Select(c => c.Trim().ToLowerInvariant()));
            return new ResourceCacheKey(relativePath.Replace('\\', '/').ToLowerInvariant(), chainKey);
        }
    }
}
