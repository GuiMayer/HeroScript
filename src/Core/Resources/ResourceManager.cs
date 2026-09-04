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
    private readonly IResourceRegenerationProcessor _regenerationProcessor;
    private ImmutableDictionary<string, ResourceDefinition> _definitions =
        ImmutableDictionary<string, ResourceDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    private readonly IContentRuntimeResolver? _contentRuntimes;
    
    public ResourceManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ILogger logger,
        IResourceRegenerationProcessor regenerationProcessor,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _regenerationProcessor = regenerationProcessor ?? throw new ArgumentNullException(nameof(regenerationProcessor));
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
                    var resourceData = _resourceLoader.LoadResource(relativePath, configChain, strictMode: false);
                    
                    if (resourceData.Count == 0)
                        continue;
                    
                    // Pegar o primeiro elemento (assumindo que é a definição completa)
                    var firstElement = resourceData.Values.FirstOrDefault();
                    if (firstElement.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                        continue;
                    
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                    };
                    
                    var definition = JsonSerializer.Deserialize<ResourceDefinition>(
                        firstElement.GetRawText(),
                        options);
                    
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
            return GetDefinition(resourceId);

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<ResourceDefinition>.Failure(runtime.Error);
        var definition = runtime.Value.GetDefinition<ResourceDefinition>("resources", resourceId);
        if (definition.IsFailure)
        {
            foreach (var key in runtime.Value.GetDefinitions("resources").Keys)
            {
                var candidate = runtime.Value.GetDefinition<ResourceDefinition>("resources", key);
                if (candidate.IsSuccess &&
                    string.Equals(candidate.Value.ResourceId, resourceId, StringComparison.Ordinal))
                {
                    definition = candidate;
                    break;
                }
            }
        }
        return definition.IsFailure
            ? Result<ResourceDefinition>.Failure(definition.Error)
            : Result<ResourceDefinition>.Success(definition.Value with
            {
                ResourceId = string.IsNullOrWhiteSpace(definition.Value.ResourceId)
                    ? resourceId
                    : definition.Value.ResourceId
            });
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
        float? initialCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var validation = ValidateResourceDefinition(definition);
        if (validation.IsFailure)
            throw new InvalidOperationException(validation.Error);

        var pool = new ResourcePool
        {
            ResourceId = definition.ResourceId,
            Current = definition.DefaultCurrent,
            Maximum = definition.DefaultMax,
            Minimum = definition.DefaultMin,
            Definition = definition
        };
        return initialCurrent.HasValue ? pool.Set(initialCurrent.Value) : pool;
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
        => ResourceDefinitionValidator.Validate(definition);
    
    public Result<ResourceRegenerationResult> ProcessRegeneration(
        ResourceSet resourceState,
        RegenerationTiming timing,
        ResourceRegenerationContext? context = null)
    {
        return _regenerationProcessor.ProcessRegeneration(resourceState, timing, context);
    }
    
}
