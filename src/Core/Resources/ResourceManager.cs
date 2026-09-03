using Core.Combat.Models;
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
public class ResourceManager : IResourceManager, IRevisionedResourceManager, IDisposable
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ILogger _logger;
    private readonly IResourceRegenerationProcessor _regenerationProcessor;
    private ImmutableDictionary<string, ResourceDefinition> _definitions =
        ImmutableDictionary<string, ResourceDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly IContentRuntimeResolver? _contentRuntimes;
    
    // Hot-reload support
    private FileSystemWatcher? _fileWatcher;
    private volatile string? _currentConfigName;
    private bool _hotReloadEnabled;
    
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
            lock (_lock)
            {
                _currentConfigName = configName;
            }
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
    {
        if (definition == null)
            return Result.Failure("Resource definition cannot be null");
        if (string.IsNullOrWhiteSpace(definition.ResourceId))
            return Result.Failure("Resource ID cannot be empty");
        
        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            return Result.Failure("Display name cannot be empty");
        
        if (!IsFinite(definition.DefaultMin) ||
            !IsFinite(definition.DefaultMax) ||
            !IsFinite(definition.DefaultCurrent))
            return Result.Failure("Resource defaults must be finite");

        if (!IsFinite(definition.CostMultiplier) || definition.CostMultiplier < 0)
            return Result.Failure("Resource cost multiplier must be finite and non-negative");

        if (definition.DefaultMax < definition.DefaultMin)
            return Result.Failure("Default max cannot be less than default min");
        
        if (definition.DefaultCurrent < definition.DefaultMin && !definition.CanBeNegative)
            return Result.Failure("Default current cannot be less than default min");
        
        if (definition.DefaultCurrent > definition.DefaultMax && !definition.CanExceedMax)
            return Result.Failure("Default current cannot exceed default max");

        var duplicatePolicyIds = definition.ThresholdPolicies
            .Where(policy => !string.IsNullOrWhiteSpace(policy.PolicyId))
            .GroupBy(policy => policy.PolicyId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePolicyIds != null)
            return Result.Failure($"Duplicate resource threshold policy id: {duplicatePolicyIds.Key}");
        foreach (var policy in definition.ThresholdPolicies)
        {
            if (string.IsNullOrWhiteSpace(policy.PolicyId))
                return Result.Failure("Resource threshold policy id cannot be empty");
            if (policy.Boundary == ResourceThresholdBoundary.Unspecified)
                return Result.Failure($"Resource threshold boundary is required: {policy.PolicyId}");
            if (policy.Consequence == ResourceThresholdConsequence.Unspecified)
                return Result.Failure($"Resource threshold consequence is required: {policy.PolicyId}");
        }

        if (definition.Regeneration is { } regeneration)
        {
            if (!IsFinite(regeneration.AmountPerTurn))
                return Result.Failure("Resource regeneration amount must be finite");
            if (regeneration.Enabled &&
                !Enum.IsDefined(regeneration.Timing))
                return Result.Failure("Resource regeneration timing is invalid");
        }
        
        return Result.Success();
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    
    public Result<EntityResourceState> ProcessRegeneration(
        EntityResourceState entityResourceState,
        RegenerationTiming timing,
        Dictionary<string, float>? context = null)
    {
        return _regenerationProcessor.ProcessRegeneration(entityResourceState, timing, context);
    }
    
    public void EnableHotReload(string configName)
    {
        lock (_lock)
        {
            if (_hotReloadEnabled)
            {
                _logger.LogWarning("Hot-reload is already enabled");
                return;
            }
            
            _currentConfigName = configName;
            
            // Get the config path to monitor
            var configPath = _configManager.GetConfigPath(configName);
            var resourcesPath = Path.Combine(configPath, "Resources", "resources");
            
            if (!Directory.Exists(resourcesPath))
            {
                _logger.LogWarning($"Resources directory not found: {resourcesPath}");
                return;
            }
            
            _fileWatcher = new FileSystemWatcher(resourcesPath)
            {
                Filter = "*.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            
            _fileWatcher.Changed += OnResourceFileChanged;
            _fileWatcher.Created += OnResourceFileChanged;
            _fileWatcher.Deleted += OnResourceFileDeleted;
            _fileWatcher.Renamed += OnResourceFileRenamed;
            
            _hotReloadEnabled = true;
            _logger.LogInformation($"Hot-reload enabled for config '{configName}' at {resourcesPath}");
        }
    }
    
    public void DisableHotReload()
    {
        lock (_lock)
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Changed -= OnResourceFileChanged;
                _fileWatcher.Created -= OnResourceFileChanged;
                _fileWatcher.Deleted -= OnResourceFileDeleted;
                _fileWatcher.Renamed -= OnResourceFileRenamed;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
            
            _hotReloadEnabled = false;
            _currentConfigName = null;
            _logger.LogInformation("Hot-reload disabled");
        }
    }
    
    public Result ReloadResource(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(_currentConfigName))
            return Result.Failure("No config loaded for hot-reload");
        
        try
        {
            var configChain = _configManager.ResolveInheritanceChain(_currentConfigName);
            var relativePath = $"resources/{resourceId}.json";
            
            // Invalidate cache for this resource
            _resourceLoader.InvalidateCache(relativePath);
            
            // Reload the resource
            var resourceData = _resourceLoader.LoadResource(relativePath, configChain, strictMode: false);
            
            if (resourceData.Count == 0)
            {
                ImmutableInterlocked.TryRemove(ref _definitions, resourceId, out _);
                _logger.LogInformation($"Resource '{resourceId}' removed (file not found or empty)");
                return Result.Success();
            }
            
            var firstElement = resourceData.Values.FirstOrDefault();
            if (firstElement.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            {
                return Result.Failure($"Invalid resource data for '{resourceId}'");
            }
            
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
                return Result.Failure($"Failed to deserialize resource '{resourceId}'");
            }
            
            var validation = ValidateResourceDefinition(definition);
            if (validation.IsFailure)
            {
                return Result.Failure($"Invalid resource definition '{resourceId}': {validation.Error}");
            }
            
            ImmutableInterlocked.AddOrUpdate(
                ref _definitions,
                definition.ResourceId,
                definition,
                (_, _) => definition);
            
            _logger.LogInformation($"Resource '{resourceId}' reloaded successfully");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error reloading resource '{resourceId}': {ex.Message}");
            return Result.Failure($"Error reloading resource: {ex.Message}");
        }
    }
    
    private void OnResourceFileChanged(object sender, FileSystemEventArgs e)
    {
        var resourceId = Path.GetFileNameWithoutExtension(e.Name);
        if (string.IsNullOrWhiteSpace(resourceId))
            return;
        _logger.LogDebug($"Resource file changed: {e.Name}");
        
        // Debounce: wait a bit for file to be fully written
        Task.Delay(100).ContinueWith(_ =>
        {
            var result = ReloadResource(resourceId);
            if (result.IsFailure)
            {
                _logger.LogWarning($"Failed to reload resource '{resourceId}': {result.Error}");
            }
        });
    }
    
    private void OnResourceFileDeleted(object sender, FileSystemEventArgs e)
    {
        var resourceId = Path.GetFileNameWithoutExtension(e.Name);
        if (string.IsNullOrWhiteSpace(resourceId))
            return;
        _logger.LogDebug($"Resource file deleted: {e.Name}");
        
        ImmutableInterlocked.TryRemove(ref _definitions, resourceId, out _);
        
        _logger.LogInformation($"Resource '{resourceId}' removed from definitions");
    }
    
    private void OnResourceFileRenamed(object sender, RenamedEventArgs e)
    {
        var oldResourceId = Path.GetFileNameWithoutExtension(e.OldName);
        var newResourceId = Path.GetFileNameWithoutExtension(e.Name);
        if (string.IsNullOrWhiteSpace(oldResourceId) || string.IsNullOrWhiteSpace(newResourceId))
            return;
        
        _logger.LogDebug($"Resource file renamed: {e.OldName} -> {e.Name}");
        
        ImmutableInterlocked.TryRemove(ref _definitions, oldResourceId, out _);
        
        var result = ReloadResource(newResourceId);
        if (result.IsFailure)
        {
            _logger.LogWarning($"Failed to reload renamed resource '{newResourceId}': {result.Error}");
        }
    }
    
    public void Dispose()
    {
        DisableHotReload();
    }
}
