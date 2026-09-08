using Core.Common;
using Core.Config;
using Core.Content;
using Core.Logging;
using System.Collections.Immutable;
using System.Text.Json;

namespace Core.Resources;

/// <summary>
/// Gerenciador de recursos configuráveis.
/// Carrega definições de recursos de arquivos JSON.
/// </summary>
public class ResourceManager : IResourceManager, IRevisionedResourceManager
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ILogger _logger;
    private ImmutableDictionary<string, ResourceDefinition> _definitions =
        ImmutableDictionary<string, ResourceDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    private readonly IContentRuntimeResolver? _contentRuntimes;
    
    public ResourceManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ILogger logger,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contentRuntimes = contentRuntimes;
    }
    
    public void LoadResourceDefinitions(string configName)
    {
        try
        {
            var loaded = ImmutableDictionary.CreateBuilder<string, ResourceDefinition>(StringComparer.Ordinal);
            // Obter cadeia de herança do config
            var configChain = _configManager.ResolveInheritanceChain(configName);
            
            // Descobrir automaticamente todos os recursos disponíveis
            var discoveredResources = _resourceLoader.DiscoverResources("resources", configChain, "*.json");
            
            _logger.LogDebug($"Discovered {discoveredResources.Count()} resource files");
            
            foreach (var resourceName in discoveredResources)
            {
                try
                {
                    var relativePath = $"resources/{resourceName}.json";
                    var resourceData = _resourceLoader.LoadResource(relativePath, configChain, strictMode: true);
                    
                    if (resourceData.Count == 0)
                        continue;
                    
                    if (!resourceData.TryGetValue(resourceName, out var definitionElement))
                    {
                        _logger.LogError(
                            $"Resource file '{relativePath}' must declare definition id '{resourceName}'");
                        continue;
                    }
                    
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                    };
                    
                    var definition = JsonSerializer.Deserialize<ResourceDefinition>(
                        definitionElement.GetRawText(),
                        options);
                    
                    if (definition == null)
                    {
                        _logger.LogWarning($"Failed to deserialize resource definition: {resourceName}");
                        continue;
                    }
                    if (!string.Equals(definition.ResourceId, resourceName, StringComparison.Ordinal))
                    {
                        _logger.LogError(
                            $"Resource definition identity mismatch in '{relativePath}': {definition.ResourceId}");
                        continue;
                    }
                    
                    var validation = ValidateResourceDefinition(definition);
                    if (validation.IsFailure)
                    {
                        _logger.LogError($"Invalid resource definition '{resourceName}': {validation.Error}");
                        continue;
                    }
                    
                    loaded[definition.ResourceId] = definition;
                    _logger.LogDebug($"Loaded resource: {definition.ResourceId}");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Could not load resource '{resourceName}': {ex.Message}");
                }
            }
            
            Interlocked.Exchange(ref _definitions, loaded.ToImmutable());
            _logger.LogInformation($"Loaded {loaded.Count} resource definitions from config '{configName}'");
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

    public Result<ResourceDefinition> GetDefinition(
        string resourceId,
        string contentRevision,
        string? configName = null)
    {
        if (_contentRuntimes == null)
            return Result<ResourceDefinition>.Failure("Revisioned resource runtime is not configured");

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<ResourceDefinition>.Failure(runtime.Error);
        return runtime.Value.GetDefinition<ResourceDefinition>("resources", resourceId);
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

    public Result<ResourcePool> CreatePool(
        string resourceId,
        float? initialCurrent,
        string contentRevision,
        string? configName = null)
    {
        var definition = GetDefinition(resourceId, contentRevision, configName);
        return definition.IsFailure
            ? Result<ResourcePool>.Failure(definition.Error)
            : Result<ResourcePool>.Success(CreatePoolFromDefinition(definition.Value, initialCurrent));
    }
    
    public ResourcePool CreatePoolFromDefinition(
        ResourceDefinition definition, 
        float? initialCurrent = null) =>
        ResourcePool.Materialize(definition, initialCurrent);
    
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
        => ResourceDefinitionValidator.Validate(definition);
    
}
