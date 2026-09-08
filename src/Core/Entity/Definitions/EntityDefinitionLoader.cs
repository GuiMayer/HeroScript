using System.Text.Json;
using Core.Caching;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Logging;
using Core.Resources;

namespace Core.Entity.Definitions;

/// <summary>
/// Carrega e gerencia definições de entidades de arquivos JSON.
/// Thread-safe com cache LRU.
/// </summary>
public class EntityDefinitionLoader : ICacheService
{
    private readonly LruCache<string, EntityDefinition> _cache;
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly IDefinitionPersister? _persister;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _configName;
    private readonly string _cacheName;
    private readonly IContentRuntimeResolver? _contentRuntimes;
    
    public string CacheName => _cacheName;

    public EntityDefinitionLoader(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ILogger? logger = null,
        string configName = "default",
        int cacheCapacity = 256,
        IDefinitionPersister? persister = null,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _logger = logger ?? NullLogger.Instance;
        _configName = string.IsNullOrWhiteSpace(configName) ? "default" : configName;
        _cacheName = $"EntityDefinitions_{configName}";
        _persister = persister; // Authoring-only dependency; gameplay uses published runtimes.
        _contentRuntimes = contentRuntimes;
        
        _cache = new LruCache<string, EntityDefinition>(cacheCapacity);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }
    
    /// <summary>
    /// Carrega uma definição de entidade de um arquivo JSON.
    /// Thread-safe com cache LRU.
    /// </summary>
    public Result<EntityDefinition> LoadDefinition(string definitionId)
    {
        try
        {
            // Verificar cache (thread-safe)
            if (_cache.TryGetValue(definitionId, out var cached))
            {
                return Result<EntityDefinition>.Success(cached!);
            }
            
            var chain = _configManager.ResolveInheritanceChain(_configName);
            var data = _resourceLoader.LoadResource($"entities/{definitionId}.json", chain, strictMode: true);
            if (data.Count == 0)
                return Result<EntityDefinition>.Failure($"Definition not found: {definitionId}");

            if (!data.TryGetValue(definitionId, out var root))
                return Result<EntityDefinition>.Failure(
                    $"Entity file must declare definition id: {definitionId}");
            
            var definition = JsonSerializer.Deserialize<EntityDefinition>(root.GetRawText(), _jsonOptions);
            
            if (definition == null)
            {
                return Result<EntityDefinition>.Failure($"Failed to deserialize definition: {definitionId}");
            }
            if (!string.Equals(definition.DefinitionId, definitionId, StringComparison.Ordinal))
            {
                return Result<EntityDefinition>.Failure(
                    $"Entity definition identity mismatch: expected {definitionId}, got {definition.DefinitionId}");
            }
            
            // Validar definição
            var validationResult = EntityDefinitionValidator.Validate(definition);
            if (!validationResult.IsSuccess)
            {
                return Result<EntityDefinition>.Failure(validationResult.Error);
            }
            
            // Cachear (thread-safe)
            _cache.Set(definitionId, definition);
            
            _logger.LogInformation($"Loaded entity definition: {definitionId}");
            return Result<EntityDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading definition {definitionId}: {ex.Message}", ex);
            return Result<EntityDefinition>.Failure($"Error loading definition: {ex.Message}");
        }
    }

    public Result<EntityDefinition> LoadDefinition(
        string definitionId,
        string contentRevision,
        string? configName = null)
    {
        if (_contentRuntimes == null)
            return Result<EntityDefinition>.Failure("Revisioned entity runtime is not configured");

        var runtime = _contentRuntimes.Resolve(contentRevision, configName ?? _configName);
        return runtime.IsFailure
            ? Result<EntityDefinition>.Failure(runtime.Error)
            : LoadRevisionDefinition(definitionId, runtime.Value);
    }

    private Result<EntityDefinition> LoadRevisionDefinition(
        string definitionId,
        ContentRuntime runtime)
    {
        try
        {
            var definitions = runtime.GetDefinitions("entities");
            if (!definitions.TryGetValue(definitionId, out var root))
                return Result<EntityDefinition>.Failure(
                    $"Entity definition '{definitionId}' was not found in revision {runtime.Manifest.Revision}");

            var definition = JsonSerializer.Deserialize<EntityDefinition>(root.GetRawText(), _jsonOptions);
            if (definition == null)
                return Result<EntityDefinition>.Failure($"Failed to deserialize definition: {definitionId}");
            if (!string.Equals(definition.DefinitionId, definitionId, StringComparison.Ordinal))
            {
                return Result<EntityDefinition>.Failure(
                    $"Entity definition identity mismatch: expected {definitionId}, got {definition.DefinitionId}");
            }

            var validation = EntityDefinitionValidator.Validate(definition);
            return validation.IsFailure
                ? Result<EntityDefinition>.Failure(validation.Error)
                : Result<EntityDefinition>.Success(definition);
        }
        catch (Exception exception)
        {
            return Result<EntityDefinition>.Failure(
                $"Error loading revisioned entity definition '{definitionId}': {exception.Message}",
                exception);
        }
    }
    
    /// <summary>
    /// Carrega todas as definições de um diretório
    /// </summary>
    public Result<Dictionary<string, EntityDefinition>> LoadAllDefinitions()
    {
        try
        {
            var definitions = new Dictionary<string, EntityDefinition>();
            
            var chain = _configManager.ResolveInheritanceChain(_configName);
            var definitionIds = _resourceLoader.DiscoverResources("entities", chain, "*.json");

            foreach (var definitionId in definitionIds)
            {
                var result = LoadDefinition(definitionId);
                
                if (result.IsSuccess)
                {
                    definitions[definitionId] = result.Value!;
                }
                else
                {
                    _logger.LogWarning($"Failed to load {definitionId}: {result.Error}");
                }
            }
            
            _logger.LogInformation($"Loaded {definitions.Count} entity definitions");
            return Result<Dictionary<string, EntityDefinition>>.Success(definitions);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading all definitions: {ex.Message}", ex);
            return Result<Dictionary<string, EntityDefinition>>.Failure(
                $"Error loading definitions: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Obtém uma definição do cache.
    /// Thread-safe.
    /// </summary>
    public EntityDefinition? GetDefinition(string definitionId)
    {
        return _cache.TryGetValue(definitionId, out var def) ? def : null;
    }
    
    /// <summary>
    /// Limpa o cache de definições.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _logger.LogInformation("Entity definition cache cleared");
    }
    
    /// <summary>
    /// Recarrega uma definição específica.
    /// </summary>
    public Result<EntityDefinition> ReloadDefinition(string definitionId)
    {
        _cache.Remove(definitionId);
        return LoadDefinition(definitionId);
    }

    /// <summary>
    /// Implementa ICacheService.Invalidate().
    /// </summary>
    public void Invalidate(string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            ClearCache();
        }
        else
        {
            ReloadDefinition(key);
        }
    }

    /// <summary>
    /// Implementa ICacheService.GetStats().
    /// </summary>
    public CacheServiceStats GetStats()
    {
        var stats = _cache.GetStats();
        return new CacheServiceStats
        {
            CacheName = _cacheName,
            Capacity = stats.Capacity,
            Count = stats.Count,
            Hits = stats.Hits,
            Misses = stats.Misses,
            Evictions = stats.Evictions,
            HitRate = stats.HitRate,
            LastInvalidation = stats.LastInvalidation
        };
    }
    
    /// <summary>
    /// Salva uma nova definição de entidade.
    /// </summary>
    public Result SaveDefinition(EntityDefinition definition, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (definition == null)
            return Result.Failure("Definition cannot be null");

        // Validate definition first
        var validation = EntityDefinitionValidator.Validate(definition);
        if (validation.IsFailure)
            return validation;

        try
        {
            // Serialize to JSON
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(definition, _jsonOptions));
            
            // Save via persister
            var result = _persister.SaveDefinition("entities", definition.DefinitionId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Add to cache
            _cache.Set(definition.DefinitionId, definition);
            _logger.LogInformation($"Saved entity definition: {definition.DefinitionId}");

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error saving entity definition '{definition.DefinitionId}': {ex.Message}");
            return Result.Failure($"Failed to save entity definition: {ex.Message}");
        }
    }

    /// <summary>
    /// Atualiza uma definição de entidade existente.
    /// </summary>
    public Result UpdateDefinition(string definitionId, EntityDefinition updatedDefinition, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (string.IsNullOrWhiteSpace(definitionId))
            return Result.Failure("DefinitionId cannot be empty");

        if (updatedDefinition == null)
            return Result.Failure("Updated definition cannot be null");

        // Ensure IDs match
        if (updatedDefinition.DefinitionId != definitionId)
            return Result.Failure($"DefinitionId mismatch: URL has '{definitionId}' but definition has '{updatedDefinition.DefinitionId}'");

        // Validate updated definition
        var validation = EntityDefinitionValidator.Validate(updatedDefinition);
        if (validation.IsFailure)
            return validation;

        try
        {
            // Serialize to JSON
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(updatedDefinition, _jsonOptions));
            
            // Update via persister
            var result = _persister.UpdateDefinition("entities", definitionId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Update cache
            _cache.Set(definitionId, updatedDefinition);
            _logger.LogInformation($"Updated entity definition: {definitionId}");

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating entity definition '{definitionId}': {ex.Message}");
            return Result.Failure($"Failed to update entity definition: {ex.Message}");
        }
    }

    /// <summary>
    /// Deleta uma definição de entidade.
    /// </summary>
    public Result DeleteDefinition(string definitionId, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (string.IsNullOrWhiteSpace(definitionId))
            return Result.Failure("DefinitionId cannot be empty");

        try
        {
            // Delete via persister
            var result = _persister.DeleteDefinition("entities", definitionId, configName);
            if (result.IsFailure)
                return result;

            // Remove from cache
            _cache.Remove(definitionId);
            _logger.LogInformation($"Deleted entity definition: {definitionId}");

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting entity definition '{definitionId}': {ex.Message}");
            return Result.Failure($"Failed to delete entity definition: {ex.Message}");
        }
    }
}
