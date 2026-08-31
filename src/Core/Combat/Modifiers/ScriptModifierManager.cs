using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Events;
using Core.Events.Domain;
using Core.Math;

namespace Core.Combat.Modifiers;

public sealed class ScriptModifierManager : IScriptModifierManager, IRevisionedScriptModifierManager
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly IRuntimeFormulaEvaluator _formulaEvaluator;
    private readonly IEventBus? _eventBus;
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private readonly ConcurrentDictionary<string, ScriptModifierDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ScriptModifierInstance>> _activeModifiers = new(StringComparer.OrdinalIgnoreCase);
    private string? _loadedConfigName;

    public ScriptModifierManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        IRuntimeFormulaEvaluator formulaEvaluator,
        IEventBus? eventBus = null,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _formulaEvaluator = formulaEvaluator ?? throw new ArgumentNullException(nameof(formulaEvaluator));
        _eventBus = eventBus;
        _contentRuntimes = contentRuntimes;
    }

    public Result LoadDefinitions(string configName)
    {
        try
        {
            var definitionsResult = LoadDefinitionJson(configName);
            if (definitionsResult.IsFailure)
                return Result.Failure(definitionsResult.Error);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());

            _definitions.Clear();
            _loadedConfigName = configName;
            foreach (var (key, element) in definitionsResult.Value)
            {
                var definition = JsonSerializer.Deserialize<ScriptModifierDefinition>(element.GetRawText(), options);
                if (definition == null)
                    return Result.Failure($"Failed to deserialize script modifier: {key}");

                var modifierId = string.IsNullOrWhiteSpace(definition.ModifierId) ? key : definition.ModifierId;
                _definitions[modifierId] = definition with { ModifierId = modifierId };
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to load script modifiers: {ex.Message}");
        }
    }

    public Result<ScriptModifierDefinition> GetDefinition(string modifierId)
    {
        if (string.IsNullOrWhiteSpace(modifierId))
            return Result<ScriptModifierDefinition>.Failure("ModifierId cannot be empty");

        if (_definitions.TryGetValue(modifierId, out var definition))
            return Result<ScriptModifierDefinition>.Success(definition);

        if (!string.IsNullOrWhiteSpace(_loadedConfigName))
        {
            var lazyResult = LoadSingleDefinition(modifierId, _loadedConfigName);
            if (lazyResult.IsSuccess)
                return lazyResult;
        }

        return Result<ScriptModifierDefinition>.Failure($"Script modifier definition not found: {modifierId}");
    }

    public Result<ScriptModifierDefinition> GetDefinition(
        string modifierId,
        string contentRevision,
        string? configName = null)
    {
        if (_contentRuntimes == null)
            return GetDefinition(modifierId);

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<ScriptModifierDefinition>.Failure(runtime.Error);
        var definition = runtime.Value.GetDefinition<ScriptModifierDefinition>("modifiers", modifierId);
        return definition.IsFailure
            ? definition
            : Result<ScriptModifierDefinition>.Success(definition.Value with
            {
                ModifierId = string.IsNullOrWhiteSpace(definition.Value.ModifierId)
                    ? modifierId
                    : definition.Value.ModifierId
            });
    }

    public IReadOnlyList<ScriptModifierDefinition> GetAllDefinitions()
    {
        return _definitions.Values.ToList();
    }

    public Result<ScriptModifierInstance> ApplyModifier(string ownerId, string modifierId, int stacks = 1, int? duration = null, string? sourceId = null)
    {
        return ApplyModifier(Guid.NewGuid(), ownerId, modifierId, stacks, duration, sourceId); // nondeterministic-boundary: compatibility overload outside replayable runs
    }

    public Result<ScriptModifierInstance> ApplyModifier(Guid instanceId, string ownerId, string modifierId, int stacks = 1, int? duration = null, string? sourceId = null)
        => ApplyModifierWithDefinition(
            instanceId,
            ownerId,
            modifierId,
            stacks,
            duration,
            sourceId,
            GetDefinition(modifierId));

    public Result<ScriptModifierInstance> ApplyModifier(
        Guid instanceId,
        string ownerId,
        string modifierId,
        string contentRevision,
        int stacks = 1,
        int? duration = null,
        string? sourceId = null,
        string? configName = null)
        => ApplyModifierWithDefinition(
            instanceId,
            ownerId,
            modifierId,
            stacks,
            duration,
            sourceId,
            GetDefinition(modifierId, contentRevision, configName));

    private Result<ScriptModifierInstance> ApplyModifierWithDefinition(
        Guid instanceId,
        string ownerId,
        string modifierId,
        int stacks,
        int? duration,
        string? sourceId,
        Result<ScriptModifierDefinition> definitionResult)
    {
        if (instanceId == Guid.Empty)
            return Result<ScriptModifierInstance>.Failure("InstanceId cannot be empty");

        if (string.IsNullOrWhiteSpace(ownerId))
            return Result<ScriptModifierInstance>.Failure("OwnerId cannot be empty");

        if (stacks <= 0)
            return Result<ScriptModifierInstance>.Failure("Stacks must be greater than 0");

        if (!definitionResult.IsSuccess)
        {
            _eventBus?.Publish(new ModifierRejectedEvent(ownerId, modifierId, definitionResult.Error));
            return Result<ScriptModifierInstance>.Failure(definitionResult.Error);
        }

        var definition = definitionResult.Value;
        var list = GetOwnerList(ownerId);
        lock (list)
        {
            var existing = list.FirstOrDefault(m => m.ModifierId.Equals(definition.ModifierId, StringComparison.OrdinalIgnoreCase) && m.IsActive);
            if (existing != null)
            {
                var updated = existing with
                {
                    Stacks = System.Math.Min(existing.Stacks + stacks, definition.MaxStacks),
                    Duration = duration ?? existing.Duration
                };
                ReplaceInstance(list, updated);
                _eventBus?.Publish(new ModifierAppliedEvent(ownerId, modifierId, updated.InstanceId, updated.Stacks, updated.Duration, sourceId));
                return Result<ScriptModifierInstance>.Success(updated);
            }

            var instance = new ScriptModifierInstance
            {
                InstanceId = instanceId,
                ModifierId = definition.ModifierId,
                Definition = definition,
                OwnerId = ownerId,
                SourceId = sourceId,
                Stacks = System.Math.Min(stacks, definition.MaxStacks),
                Duration = duration ?? definition.DefaultDuration
            };
            list.Add(instance);
            _eventBus?.Publish(new ModifierAppliedEvent(ownerId, modifierId, instance.InstanceId, instance.Stacks, instance.Duration, sourceId));
            return Result<ScriptModifierInstance>.Success(instance);
        }
    }

    public Result RemoveModifier(string ownerId, Guid instanceId)
    {
        var list = GetOwnerList(ownerId);
        string? modifierId = null;
        lock (list)
        {
            var item = list.FirstOrDefault(m => m.InstanceId == instanceId);
            modifierId = item?.ModifierId;
            var removed = list.RemoveAll(m => m.InstanceId == instanceId);
            if (removed == 0)
                return Result.Failure($"Modifier instance {instanceId} not found");
        }
        _eventBus?.Publish(new ModifierRemovedEvent(ownerId, modifierId ?? string.Empty, instanceId));
        return Result.Success();
    }

    public IReadOnlyList<ScriptModifierInstance> GetActiveModifiers(string ownerId)
    {
        var list = GetOwnerList(ownerId);
        lock (list)
        {
            return list.Where(m => m.IsActive).ToList();
        }
    }

    public Dictionary<string, float> GetPipelineModifiers(string ownerId, IEnumerable<string>? effectTags = null)
    {
        var tags = new HashSet<string>(effectTags ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var modifiers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        foreach (var modifier in GetActiveModifiers(ownerId))
        {
            if (string.IsNullOrWhiteSpace(modifier.Definition.ModifierKey) || !TagsMatch(modifier.Definition, tags))
                continue;

            var value = CalculateValue(modifier);
            modifiers[modifier.Definition.ModifierKey] = modifiers.TryGetValue(modifier.Definition.ModifierKey, out var current)
                ? current + value
                : value;
        }

        return modifiers;
    }

    public Result TickDurations(string ownerId)
    {
        var list = GetOwnerList(ownerId);
        var expired = new List<ScriptModifierInstance>();
        var ticked = new List<(ScriptModifierInstance modifier, int newDuration)>();
        lock (list)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var modifier = list[i];
                if (modifier.Duration == -1)
                    continue;

                var duration = modifier.Duration - 1;
                if (duration <= 0)
                {
                    expired.Add(modifier);
                    list.RemoveAt(i);
                }
                else
                {
                    list[i] = modifier with { Duration = duration };
                    ticked.Add((modifier, duration));
                }
            }
        }

        foreach (var (modifier, newDuration) in ticked)
            _eventBus?.Publish(new ModifierTickedEvent(ownerId, modifier.ModifierId, modifier.InstanceId, newDuration));
        foreach (var modifier in expired)
            _eventBus?.Publish(new ModifierExpiredEvent(ownerId, modifier.ModifierId, modifier.InstanceId));

        return Result.Success();
    }

    private List<ScriptModifierInstance> GetOwnerList(string ownerId)
    {
        return _activeModifiers.GetOrAdd(ownerId, _ => new List<ScriptModifierInstance>());
    }

    private Result<Dictionary<string, JsonElement>> LoadDefinitionJson(string configName)
    {
        var chain = _configManager.ResolveInheritanceChain(configName);
        var data = _resourceLoader.LoadResource("Modifiers/script_modifiers.json", chain, strictMode: false);
        return data.Count == 0
            ? Result<Dictionary<string, JsonElement>>.Failure("Script modifiers not found: Modifiers/script_modifiers.json")
            : Result<Dictionary<string, JsonElement>>.Success(data);
    }

    private Result<ScriptModifierDefinition> LoadSingleDefinition(string modifierId, string configName)
    {
        var definitionsResult = LoadDefinitionJson(configName);
        if (definitionsResult.IsFailure)
            return Result<ScriptModifierDefinition>.Failure(definitionsResult.Error);

        var element = definitionsResult.Value.TryGetValue(modifierId, out var exact)
            ? exact
            : default;
        if (element.ValueKind == JsonValueKind.Undefined)
            return Result<ScriptModifierDefinition>.Failure($"Script modifier definition not found: {modifierId}");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var definition = JsonSerializer.Deserialize<ScriptModifierDefinition>(element.GetRawText(), options);
        if (definition == null)
            return Result<ScriptModifierDefinition>.Failure($"Failed to deserialize script modifier: {modifierId}");

        var resolvedId = string.IsNullOrWhiteSpace(definition.ModifierId) ? modifierId : definition.ModifierId;
        var resolvedDefinition = definition with { ModifierId = resolvedId };
        _definitions[resolvedId] = resolvedDefinition;
        return Result<ScriptModifierDefinition>.Success(resolvedDefinition);
    }

    private static void ReplaceInstance(List<ScriptModifierInstance> list, ScriptModifierInstance updated)
    {
        var index = list.FindIndex(m => m.InstanceId == updated.InstanceId);
        if (index >= 0)
            list[index] = updated;
    }

    private static bool TagsMatch(ScriptModifierDefinition definition, HashSet<string> tags)
    {
        if (definition.RequiredTags.Count > 0 && !definition.RequiredTags.All(tags.Contains))
            return false;

        return definition.ExcludedTags.Count == 0 || !definition.ExcludedTags.Any(tags.Contains);
    }

    private float CalculateValue(ScriptModifierInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.Definition.FormulaValue))
        {
            var variables = new Dictionary<string, float>
            {
                ["stacks"] = instance.Stacks,
                ["duration"] = instance.Duration
            };

            var value = _formulaEvaluator.Evaluate(instance.Definition.FormulaValue, variables);
            if (value.IsSuccess)
                return value.Value;
        }

        return instance.Definition.BaseValue * instance.Stacks;
    }
}
