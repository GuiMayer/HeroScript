using System.Text.Json;
using Core.Caching;
using Core.Common;
using Core.Config;
using Core.Entity.Components;
using Core.Entity.Controllers;
using Core.Logging;
using Core.Resources;

namespace Core.Entity.Definitions;

/// <summary>
/// Carrega e gerencia definições de entidades de arquivos JSON.
/// Suporta herança delta (baseDefinitionId).
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
    
    public string CacheName => _cacheName;

    public EntityDefinitionLoader(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ILogger? logger = null,
        string configName = "default",
        int cacheCapacity = 256,
        IDefinitionPersister? persister = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _logger = logger ?? NullLogger.Instance;
        _configName = string.IsNullOrWhiteSpace(configName) ? "default" : configName;
        _cacheName = $"EntityDefinitions_{configName}";
        _persister = persister; // Optional for backward compatibility
        
        _cache = new LruCache<string, EntityDefinition>(cacheCapacity);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
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
                return Result<EntityDefinition>.Success(cached);
            }
            
            var chain = _configManager.ResolveInheritanceChain(_configName);
            var data = _resourceLoader.LoadResource($"Entities/{definitionId}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<EntityDefinition>.Failure($"Definition not found: {definitionId}");

            var root = data.TryGetValue(definitionId, out var exact) ? exact : data.Values.First();
            
            var definition = JsonSerializer.Deserialize<EntityDefinition>(root.GetRawText(), _jsonOptions);
            
            if (definition == null)
            {
                return Result<EntityDefinition>.Failure($"Failed to deserialize definition: {definitionId}");
            }
            
            // Aplicar herança delta se necessário
            if (!string.IsNullOrEmpty(definition.BaseDefinitionId))
            {
                var baseResult = LoadDefinition(definition.BaseDefinitionId);
                if (!baseResult.IsSuccess)
                {
                    return Result<EntityDefinition>.Failure(
                        $"Failed to load base definition '{definition.BaseDefinitionId}': {baseResult.Error}");
                }
                
                definition = MergeDefinitions(baseResult.Value!, definition, root);
            }
            
            // Validar definição
            var validationResult = ValidateDefinition(definition);
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
    
    /// <summary>
    /// Carrega todas as definições de um diretório
    /// </summary>
    public Result<Dictionary<string, EntityDefinition>> LoadAllDefinitions()
    {
        try
        {
            var definitions = new Dictionary<string, EntityDefinition>();
            
            var chain = _configManager.ResolveInheritanceChain(_configName);
            var definitionIds = _resourceLoader.DiscoverResources("Entities", chain, "*.json");

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
    /// Mescla definição base com definição derivada (herança delta)
    /// </summary>
    private EntityDefinition MergeDefinitions(
        EntityDefinition baseDefinition, 
        EntityDefinition derived,
        JsonElement derivedJson)
    {
        // Merge Stats com base nos campos presentes no JSON
        StatsDefinition? mergedStats = null;
        if (baseDefinition.Stats != null || derived.Stats != null)
        {
            if (derivedJson.TryGetProperty("stats", out var statsJson))
            {
                var baseS = baseDefinition.Stats ?? new StatsDefinition();
                var derivedS = derived.Stats ?? new StatsDefinition();
                
                mergedStats = new StatsDefinition
                {
                    Strength = statsJson.TryGetProperty("strength", out _) 
                        ? derivedS.Strength : baseS.Strength,
                    Dexterity = statsJson.TryGetProperty("dexterity", out _) 
                        ? derivedS.Dexterity : baseS.Dexterity,
                    Intelligence = statsJson.TryGetProperty("intelligence", out _) 
                        ? derivedS.Intelligence : baseS.Intelligence,
                    Constitution = statsJson.TryGetProperty("constitution", out _) 
                        ? derivedS.Constitution : baseS.Constitution,
                    Wisdom = statsJson.TryGetProperty("wisdom", out _) 
                        ? derivedS.Wisdom : baseS.Wisdom,
                    Charisma = statsJson.TryGetProperty("charisma", out _) 
                        ? derivedS.Charisma : baseS.Charisma,
                    CustomStats = MergeDictionaries(
                        baseS.CustomStats.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                        derivedS.CustomStats.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value))
                        .ToDictionary(kvp => kvp.Key, kvp => (float)kvp.Value)
                };
            }
            else
            {
                mergedStats = baseDefinition.Stats;
            }
        }
        
        return new EntityDefinition
        {
            DefinitionId = derived.DefinitionId,
            Type = derived.Type != default ? derived.Type : baseDefinition.Type,
            DisplayName = !string.IsNullOrEmpty(derived.DisplayName) 
                ? derived.DisplayName 
                : baseDefinition.DisplayName,
            Description = !string.IsNullOrEmpty(derived.Description)
                ? derived.Description
                : baseDefinition.Description,
            Resources = derived.Resources ?? baseDefinition.Resources,
            Stats = mergedStats,
            Inventory = derived.Inventory ?? baseDefinition.Inventory,
            AI = derived.AI ?? baseDefinition.AI,
            Gambits = derived.Gambits ?? baseDefinition.Gambits,
            IconPath = !string.IsNullOrEmpty(derived.IconPath)
                ? derived.IconPath
                : baseDefinition.IconPath,
            SpritePath = !string.IsNullOrEmpty(derived.SpritePath)
                ? derived.SpritePath
                : baseDefinition.SpritePath,
            CustomData = MergeDictionaries(baseDefinition.CustomData, derived.CustomData),
            BaseDefinitionId = derived.BaseDefinitionId
        };
    }
    
    /// <summary>
    /// Mescla dois dicionários (derived sobrescreve base)
    /// </summary>
    private Dictionary<string, object> MergeDictionaries(
        Dictionary<string, object> baseDict,
        Dictionary<string, object> derived)
    {
        var result = new Dictionary<string, object>(baseDict);
        
        foreach (var kvp in derived)
        {
            result[kvp.Key] = kvp.Value;
        }
        
        return result;
    }
    
    /// <summary>
    /// Valida uma definição de entidade
    /// </summary>
    private Result ValidateDefinition(EntityDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.DefinitionId))
        {
            return Result.Failure("DefinitionId is required");
        }
        
        if (string.IsNullOrEmpty(definition.DisplayName))
        {
            return Result.Failure("DisplayName is required");
        }
        
        // Validar que inimigos têm AI
        if (definition.Type == EntityType.ENEMY && definition.AI == null)
        {
            _logger.LogWarning($"Enemy {definition.DefinitionId} has no AI definition");
        }
        
        // Validar que companions têm Gambits
        if (definition.Type == EntityType.COMPANION && definition.Gambits == null)
        {
            _logger.LogWarning($"Companion {definition.DefinitionId} has no Gambit definition");
        }
        
        return Result.Success();
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
        var validation = ValidateDefinition(definition);
        if (validation.IsFailure)
            return validation;

        try
        {
            // Serialize to JSON
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(definition, _jsonOptions));
            
            // Save via persister
            var result = _persister.SaveDefinition("Entities", definition.DefinitionId, jsonDoc, configName);
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
        var validation = ValidateDefinition(updatedDefinition);
        if (validation.IsFailure)
            return validation;

        try
        {
            // Serialize to JSON
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(updatedDefinition, _jsonOptions));
            
            // Update via persister
            var result = _persister.UpdateDefinition("Entities", definitionId, jsonDoc, configName);
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
            var result = _persister.DeleteDefinition("Entities", definitionId, configName);
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
