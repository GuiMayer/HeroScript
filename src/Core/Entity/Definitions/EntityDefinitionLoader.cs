using System.Text.Json;
using Core.Common;
using Core.Entity.Components;
using Core.Entity.Controllers;
using Core.Logging;
using Core.Resources;

namespace Core.Entity.Definitions;

/// <summary>
/// Carrega e gerencia definições de entidades de arquivos JSON.
/// Suporta herança delta (baseDefinitionId).
/// </summary>
public class EntityDefinitionLoader
{
    private readonly Dictionary<string, EntityDefinition> _definitions = new();
    private readonly string _basePath;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public EntityDefinitionLoader(string basePath, ILogger? logger = null)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _logger = logger ?? new ConsoleLogger("EntityDefinitionLoader");
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }
    
    /// <summary>
    /// Carrega uma definição de entidade de um arquivo JSON
    /// </summary>
    public Result<EntityDefinition> LoadDefinition(string definitionId)
    {
        try
        {
            // Verificar cache
            if (_definitions.TryGetValue(definitionId, out var cached))
            {
                return Result<EntityDefinition>.Success(cached);
            }
            
            // Construir caminho do arquivo
            var filePath = Path.Combine(_basePath, $"{definitionId}.json");
            
            if (!File.Exists(filePath))
            {
                return Result<EntityDefinition>.Failure($"Definition file not found: {filePath}");
            }
            
            // Carregar JSON
            var json = File.ReadAllText(filePath);
            
            // Parse como JsonDocument para detectar campos presentes
            using var jsonDoc = JsonDocument.Parse(json);
            var root = jsonDoc.RootElement;
            
            var definition = JsonSerializer.Deserialize<EntityDefinition>(json, _jsonOptions);
            
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
            
            // Cachear
            _definitions[definitionId] = definition;
            
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
            
            if (!Directory.Exists(_basePath))
            {
                _logger.LogWarning($"Entity definitions directory not found: {_basePath}");
                return Result<Dictionary<string, EntityDefinition>>.Success(definitions);
            }
            
            var files = Directory.GetFiles(_basePath, "*.json", SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                var definitionId = Path.GetFileNameWithoutExtension(file);
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
    /// Obtém uma definição do cache
    /// </summary>
    public EntityDefinition? GetDefinition(string definitionId)
    {
        return _definitions.TryGetValue(definitionId, out var def) ? def : null;
    }
    
    /// <summary>
    /// Limpa o cache de definições
    /// </summary>
    public void ClearCache()
    {
        _definitions.Clear();
        _logger.LogInformation("Entity definition cache cleared");
    }
    
    /// <summary>
    /// Recarrega uma definição específica
    /// </summary>
    public Result<EntityDefinition> ReloadDefinition(string definitionId)
    {
        _definitions.Remove(definitionId);
        return LoadDefinition(definitionId);
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
}
