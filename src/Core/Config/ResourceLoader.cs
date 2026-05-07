using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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

        /// <summary>
        /// Singleton instance
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

        private ResourceLoader() { }

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

                Console.WriteLine($"[ResourceLoader] Loading resource: {relativePath}");
                Console.WriteLine($"[ResourceLoader] Config chain: {string.Join(" -> ", configChain)}");

                var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Carregar e fazer merge de cada config na cadeia
                foreach (var configName in configChain)
                {
                    var configPath = ConfigManager.GetConfigPath(configName);
                    var fullPath = Path.Combine(configPath, "Resources", relativePath);

                    // Fallback para dev mode
                    if (!File.Exists(fullPath))
                    {
                        var devPath = Path.Combine(AppContext.BaseDirectory, "Resources", relativePath);
                        if (File.Exists(devPath))
                        {
                            fullPath = devPath;
                            Console.WriteLine($"[ResourceLoader] Using dev fallback for '{configName}': {devPath}");
                        }
                        else
                        {
                            Console.WriteLine($"[ResourceLoader] Resource not found for '{configName}': {relativePath}");
                            continue;
                        }
                    }

                    // Carregar e fazer merge
                    try
                    {
                        var resources = LoadFromFile(fullPath, configName, strictMode);
                        MergeResources(merged, origins, resources, configName, strictMode);
                        Console.WriteLine($"[ResourceLoader] Loaded {resources.Count} resources from {Path.GetFileName(fullPath)} ({configName})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ResourceLoader] Error loading {fullPath}: {ex.Message}");
                        if (strictMode)
                            throw;
                    }
                }

                // Cachear resultado
                _cache[relativePath] = merged;
                _originCache[relativePath] = origins;

                Console.WriteLine($"[ResourceLoader] Loaded {merged.Count} resources total for {relativePath}");
                return merged;
            }
        }

        /// <summary>
        /// Carrega recursos de um arquivo JSON.
        /// </summary>
        private Dictionary<string, JsonElement> LoadFromFile(
            string filePath,
            string configName,
            bool strictMode)
        {
            var jsonContent = File.ReadAllText(filePath);
            var rawDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);

            if (rawDict == null)
                throw new InvalidOperationException($"Failed to deserialize {filePath}");

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
                        Console.WriteLine($"[ResourceLoader] Override: {resourceId} (from {configName}) [legacy format]");
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
                    Console.WriteLine($"[ResourceLoader] Warning: {warning}");
                }

                // Logar erros
                if (!validation.IsValid)
                {
                    foreach (var error in validation.Errors)
                    {
                        Console.WriteLine($"[ResourceLoader] Error: {error}");
                    }

                    if (strictMode)
                        throw new InvalidOperationException($"Delta validation failed for '{resourceId}'");

                    Console.WriteLine($"[ResourceLoader] Skipping invalid delta for '{resourceId}'");
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
                            Console.WriteLine($"[ResourceLoader] Delta {operation}: {resourceId} (from {configName})");
                        }
                        else
                        {
                            Console.WriteLine($"[ResourceLoader] New resource: {resourceId} (from {configName})");
                        }

                        merged[resourceId] = result.Value;
                        origins[resourceId] = configName;
                    }
                    else
                    {
                        // DELETE: remover recurso
                        if (merged.Remove(resourceId))
                        {
                            Console.WriteLine($"[ResourceLoader] Deleted: {resourceId} (from {configName})");
                            origins.Remove(resourceId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ResourceLoader] Error applying delta for '{resourceId}': {ex.Message}");
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
                    Console.WriteLine($"[ResourceLoader] Cache invalidated for: {relativePath}");
                }
                else
                {
                    _cache.Clear();
                    _originCache.Clear();
                    Console.WriteLine("[ResourceLoader] All cache invalidated");
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
