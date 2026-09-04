using Core.Combat.Models;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Effects;
using Core.Logging;
using Core.Resources;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Combat;

/// <summary>
/// Gerenciador de ações configuráveis.
/// Carrega definições de ações de arquivos JSON.
/// </summary>
public class ActionManager : IActionManager, IRevisionedActionCatalog
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ILogger _logger;
    private readonly IDefinitionPersister? _persister;
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private ImmutableDictionary<string, ActionDefinition> _definitions =
        ImmutableDictionary<string, ActionDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    private volatile string? _loadedConfigName;
    
    public ActionManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ILogger logger,
        IDefinitionPersister? persister = null,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _persister = persister; // Optional for backward compatibility
        _contentRuntimes = contentRuntimes;
    }
    
    /// <summary>
    /// Carrega definições de ações de um config.
    /// </summary>
    public void LoadActionDefinitions(string configName)
    {
        try
        {
            var loaded = ImmutableDictionary.CreateBuilder<string, ActionDefinition>(StringComparer.Ordinal);
            // Obter cadeia de herança do config
            var configChain = _configManager.ResolveInheritanceChain(configName);
            
            var actionNames = _resourceLoader.DiscoverResources("actions", configChain);
            
            foreach (var actionName in actionNames)
            {
                try
                {
                    var relativePath = $"actions/{actionName}.json";
                    var actionData = _resourceLoader.LoadResource(relativePath, configChain, strictMode: false);
                    
                    if (actionData.Count == 0)
                        continue;
                    
                    var firstElement = actionData.Values.FirstOrDefault();
                    if (firstElement.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                        continue;
                    
                    var definition = JsonSerializer.Deserialize<ActionDefinition>(
                        firstElement.GetRawText(),
                        CreateJsonOptions());
                    
                    if (definition == null)
                    {
                        _logger.LogWarning($"Failed to deserialize action definition: {actionName}");
                        continue;
                    }

                    definition = NormalizeEffectIds(definition);
                    
                    var validation = ValidateActionDefinition(definition);
                    if (validation.IsFailure)
                    {
                        _logger.LogError($"Invalid action definition '{actionName}': {validation.Error}");
                        continue;
                    }
                    
                    loaded[definition.ActionId] = definition;
                    _logger.LogDebug($"Loaded action: {definition.ActionId}");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Could not load action '{actionName}': {ex.Message}");
                }
            }
            
            Interlocked.Exchange(ref _definitions, loaded.ToImmutable());
            _loadedConfigName = configName;
            _logger.LogInformation($"Loaded {loaded.Count} action definitions from config '{configName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading action definitions: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Obtém definição de uma ação.
    /// </summary>
    public Result<ActionDefinition> GetDefinition(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
            return Result<ActionDefinition>.Failure("Action ID cannot be empty");
        
        if (_definitions.TryGetValue(actionId, out var definition))
            return Result<ActionDefinition>.Success(definition);

        if (!string.IsNullOrWhiteSpace(_loadedConfigName))
        {
            var lazyResult = LoadSingleDefinition(actionId, _loadedConfigName);
            if (lazyResult.IsSuccess)
                return lazyResult;
        }
        
        return Result<ActionDefinition>.Failure($"Action not found: {actionId}");
    }

    public Result<ActionDefinition> GetDefinition(
        string actionId,
        string contentRevision,
        string? configName = null)
    {
        if (string.IsNullOrWhiteSpace(actionId))
            return Result<ActionDefinition>.Failure("Action ID cannot be empty");
        if (string.IsNullOrWhiteSpace(contentRevision))
            return Result<ActionDefinition>.Failure("Content revision cannot be empty");
        if (_contentRuntimes == null)
            return GetDefinition(actionId);

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<ActionDefinition>.Failure(runtime.Error);

        var definition = runtime.Value.GetDefinition<ActionDefinition>("actions", actionId);
        if (definition.IsFailure)
            return definition;

        var normalized = NormalizeEffectIds(definition.Value);
        var validation = ValidateActionDefinition(normalized);
        return validation.IsFailure
            ? Result<ActionDefinition>.Failure(validation.Error)
            : Result<ActionDefinition>.Success(normalized);
    }
    
    /// <summary>
    /// Obtém todas as definições de ações.
    /// </summary>
    public IReadOnlyList<ActionDefinition> GetAllDefinitions()
    {
        return _definitions.Values.ToList();
    }
    
    /// <summary>
    /// Obtém definições por tipo.
    /// </summary>
    public IReadOnlyList<ActionDefinition> GetDefinitionsByType(ActionType actionType)
    {
        return _definitions.Values
            .Where(d => d.ActionType == actionType)
            .ToList();
    }
    
    /// <summary>
    /// Obtém definições por tag.
    /// </summary>
    public IReadOnlyList<ActionDefinition> GetDefinitionsByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<ActionDefinition>();
        
        return _definitions.Values
            .Where(d => d.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
    
    /// <summary>
    /// Valida definição de ação.
    /// </summary>
    public Result ValidateActionDefinition(ActionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ActionId))
            return Result.Failure("Action ID cannot be empty");
        
        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            return Result.Failure("Display name cannot be empty");
        
        if (definition.Cooldown < 0)
            return Result.Failure("Cooldown cannot be negative");
        
        // Validar custos
        foreach (var cost in definition.Costs.Costs)
        {
            if (string.IsNullOrWhiteSpace(cost.ResourceId))
                return Result.Failure("Resource ID in cost cannot be empty");
            
            if (cost.Amount < 0)
                return Result.Failure($"Cost amount for {cost.ResourceId} cannot be negative");
        }

        foreach (var effect in EnumerateEffects(definition.Effects))
        {
            if (effect.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE &&
                string.IsNullOrWhiteSpace(effect.TargetResource))
            {
                return Result.Failure(
                    $"Effect {effect.EffectId} ({effect.Type}) requires targetResource");
            }
            if (effect.Target is EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY &&
                string.IsNullOrWhiteSpace(effect.SelectionResourceId))
            {
                return Result.Failure(
                    $"Effect {effect.EffectId} ({effect.Target}) requires selectionResourceId");
            }
        }
        
        return Result.Success();
    }

    private static IEnumerable<EffectDefinition> EnumerateEffects(
        IEnumerable<EffectDefinition> effects)
    {
        foreach (var effect in effects)
        {
            yield return effect;
            foreach (var nested in EnumerateEffects(effect.ChainedEffects ?? []))
                yield return nested;
            foreach (var nested in EnumerateEffects(effect.ConditionalEffects ?? []))
                yield return nested;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private Result<ActionDefinition> LoadSingleDefinition(string actionId, string configName)
    {
        try
        {
            var configChain = _configManager.ResolveInheritanceChain(configName);
            var actionData = _resourceLoader.LoadResource($"actions/{actionId}.json", configChain, strictMode: false);
            if (actionData.Count == 0)
                return Result<ActionDefinition>.Failure($"Action not found: {actionId}");

            var element = actionData.TryGetValue(actionId, out var exact)
                ? exact
                : actionData.Values.First();

            var definition = JsonSerializer.Deserialize<ActionDefinition>(element.GetRawText(), CreateJsonOptions());
            if (definition == null)
                return Result<ActionDefinition>.Failure($"Failed to deserialize action definition: {actionId}");

            definition = NormalizeEffectIds(definition);

            var validation = ValidateActionDefinition(definition);
            if (validation.IsFailure)
                return Result<ActionDefinition>.Failure(validation.Error);

            ImmutableInterlocked.AddOrUpdate(
                ref _definitions,
                definition.ActionId,
                definition,
                (_, _) => definition);
            _logger.LogDebug($"Lazy loaded action: {definition.ActionId}");
            return Result<ActionDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<ActionDefinition>.Failure($"Could not load action '{actionId}': {ex.Message}", ex);
        }
    }

    public Result SaveDefinition(ActionDefinition definition, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (definition == null)
            return Result.Failure("Definition cannot be null");

        definition = NormalizeEffectIds(definition);

        // Validate definition first
        var validation = ValidateActionDefinition(definition);
        if (validation.IsFailure)
            return validation;

        try
        {
            // Serialize to JSON
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(definition, CreateJsonOptions()));
            
            // Save via persister
            var result = _persister.SaveDefinition("actions", definition.ActionId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Add to cache
            ImmutableInterlocked.AddOrUpdate(
                ref _definitions,
                definition.ActionId,
                definition,
                (_, _) => definition);
            _logger.LogInformation($"Saved action definition: {definition.ActionId}");

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error saving action definition '{definition.ActionId}': {ex.Message}");
            return Result.Failure($"Failed to save action definition: {ex.Message}");
        }
    }

    public Result UpdateDefinition(string actionId, ActionDefinition updatedDefinition, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (string.IsNullOrWhiteSpace(actionId))
            return Result.Failure("ActionId cannot be empty");

        if (updatedDefinition == null)
            return Result.Failure("Updated definition cannot be null");

        updatedDefinition = NormalizeEffectIds(updatedDefinition);

        // Ensure IDs match
        if (updatedDefinition.ActionId != actionId)
            return Result.Failure($"ActionId mismatch: URL has '{actionId}' but definition has '{updatedDefinition.ActionId}'");

        // Validate updated definition
        var validation = ValidateActionDefinition(updatedDefinition);
        if (validation.IsFailure)
            return validation;

        try
        {
            // Serialize to JSON
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(updatedDefinition, CreateJsonOptions()));
            
            // Update via persister
            var result = _persister.UpdateDefinition("actions", actionId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Update cache
            ImmutableInterlocked.AddOrUpdate(
                ref _definitions,
                actionId,
                updatedDefinition,
                (_, _) => updatedDefinition);
            _logger.LogInformation($"Updated action definition: {actionId}");

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating action definition '{actionId}': {ex.Message}");
            return Result.Failure($"Failed to update action definition: {ex.Message}");
        }
    }

    public Result DeleteDefinition(string actionId, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (string.IsNullOrWhiteSpace(actionId))
            return Result.Failure("ActionId cannot be empty");

        try
        {
            // Delete via persister
            var result = _persister.DeleteDefinition("actions", actionId, configName);
            if (result.IsFailure)
                return result;

            // Remove from cache
            ImmutableInterlocked.TryRemove(ref _definitions, actionId, out _);
            _logger.LogInformation($"Deleted action definition: {actionId}");

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting action definition '{actionId}': {ex.Message}");
            return Result.Failure($"Failed to delete action definition: {ex.Message}");
        }
    }

    private static ActionDefinition NormalizeEffectIds(ActionDefinition definition)
    {
        var effects = definition.Effects
            .Select((effect, index) => NormalizeEffectId(
                effect,
                $"{definition.ActionId}.effect.{index}"))
            .ToList();
        return definition with { Effects = effects };
    }

    private static Effects.EffectDefinition NormalizeEffectId(
        Effects.EffectDefinition effect,
        string path)
    {
        var chained = effect.ChainedEffects?
            .Select((child, index) => NormalizeEffectId(child, $"{path}.chained.{index}"))
            .ToList();
        var conditional = effect.ConditionalEffects?
            .Select((child, index) => NormalizeEffectId(child, $"{path}.conditional.{index}"))
            .ToList();
        return effect with
        {
            EffectId = string.IsNullOrWhiteSpace(effect.EffectId) ? path : effect.EffectId,
            ChainedEffects = chained,
            ConditionalEffects = conditional
        };
    }
}
