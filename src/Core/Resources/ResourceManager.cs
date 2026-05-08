using Core.Common;
using Core.Config;
using Core.Logging;
using System.Text.Json;

namespace Core.Resources;

/// <summary>
/// Gerenciador de recursos configuráveis.
/// Carrega definições de recursos de arquivos JSON.
/// </summary>
public class ResourceManager : IResourceManager
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ILogger _logger;
    private readonly Dictionary<string, ResourceDefinition> _definitions = new();
    
    public ResourceManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ILogger logger)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public void LoadResourceDefinitions(string configName)
    {
        _definitions.Clear();
        
        try
        {
            // Obter cadeia de herança do config
            var configChain = _configManager.ResolveInheritanceChain(configName);
            
            // Tentar carregar recursos conhecidos
            var resourceNames = new[] { "health", "energy", "mana", "shield", "stamina", "rage" };
            
            foreach (var resourceName in resourceNames)
            {
                try
                {
                    var relativePath = $"resources/{resourceName}.json";
                    var resourceData = _resourceLoader.LoadResource(relativePath, configChain, strictMode: false);
                    
                    if (resourceData.Count == 0)
                        continue;
                    
                    // Pegar o primeiro elemento (assumindo que é a definição completa)
                    var firstElement = resourceData.Values.FirstOrDefault();
                    if (firstElement.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                        continue;
                    
                    var definition = JsonSerializer.Deserialize<ResourceDefinition>(
                        firstElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (definition == null)
                    {
                        _logger.LogWarning($"Failed to deserialize resource definition: {resourceName}");
                        continue;
                    }
                    
                    var validation = ValidateResourceDefinition(definition);
                    if (validation.IsFailure)
                    {
                        _logger.LogError($"Invalid resource definition '{resourceName}': {validation.Error}");
                        continue;
                    }
                    
                    _definitions[definition.ResourceId] = definition;
                    _logger.LogDebug($"Loaded resource: {definition.ResourceId}");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Could not load resource '{resourceName}': {ex.Message}");
                }
            }
            
            _logger.LogInformation($"Loaded {_definitions.Count} resource definitions from config '{configName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading resource definitions: {ex.Message}");
        }
    }
    
    public Result<ResourceDefinition> GetDefinition(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return Result<ResourceDefinition>.Failure("Resource ID cannot be empty");
        
        if (_definitions.TryGetValue(resourceId, out var definition))
            return Result<ResourceDefinition>.Success(definition);
        
        return Result<ResourceDefinition>.Failure($"Resource not found: {resourceId}");
    }
    
    public IReadOnlyList<ResourceDefinition> GetAllDefinitions()
    {
        return _definitions.Values.ToList();
    }
    
    public IReadOnlyList<ResourceDefinition> GetDefinitionsByCategory(ResourceCategory category)
    {
        return _definitions.Values
            .Where(d => d.Category == category)
            .ToList();
    }
    
    public IReadOnlyList<ResourceDefinition> GetDefinitionsByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<ResourceDefinition>();
        
        return _definitions.Values
            .Where(d => d.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
    
    public ResourcePool CreatePool(string resourceId, float? initialCurrent = null)
    {
        var defResult = GetDefinition(resourceId);
        if (defResult.IsFailure)
            throw new InvalidOperationException($"Resource not found: {resourceId}");
        
        return CreatePoolFromDefinition(defResult.Value, initialCurrent);
    }
    
    public ResourcePool CreatePoolFromDefinition(
        ResourceDefinition definition, 
        float? initialCurrent = null)
    {
        return new ResourcePool
        {
            ResourceId = definition.ResourceId,
            Current = initialCurrent ?? definition.DefaultCurrent,
            Maximum = definition.DefaultMax,
            Minimum = definition.DefaultMin,
            Definition = definition
        };
    }
    
    public Dictionary<string, ResourcePool> CreateDefaultPools()
    {
        return _definitions.Values
            .ToDictionary(
                def => def.ResourceId,
                def => CreatePoolFromDefinition(def));
    }
    
    public bool ValidateResourceExists(string resourceId)
    {
        return _definitions.ContainsKey(resourceId);
    }
    
    public Result ValidateCost(ResourcePool pool, float cost)
    {
        if (cost < 0)
            return Result.Failure("Cost cannot be negative");
        
        if (!pool.CanAfford(cost))
            return Result.Failure(
                $"Insufficient {pool.Definition.DisplayName}: has {pool.Current}, needs {cost}");
        
        return Result.Success();
    }
    
    public Result ValidateResourceDefinition(ResourceDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ResourceId))
            return Result.Failure("Resource ID cannot be empty");
        
        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            return Result.Failure("Display name cannot be empty");
        
        if (definition.DefaultMax < definition.DefaultMin)
            return Result.Failure("Default max cannot be less than default min");
        
        if (definition.DefaultCurrent < definition.DefaultMin && !definition.CanBeNegative)
            return Result.Failure("Default current cannot be less than default min");
        
        if (definition.DefaultCurrent > definition.DefaultMax && !definition.CanExceedMax)
            return Result.Failure("Default current cannot exceed default max");
        
        return Result.Success();
    }
}
