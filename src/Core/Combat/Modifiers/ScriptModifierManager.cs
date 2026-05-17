using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Config;

namespace Core.Combat.Modifiers;

public sealed class ScriptModifierManager : IScriptModifierManager
{
    private readonly IConfigManager _configManager;
    private readonly ConcurrentDictionary<string, ScriptModifierDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ScriptModifierInstance>> _activeModifiers = new(StringComparer.OrdinalIgnoreCase);

    public ScriptModifierManager(IConfigManager configManager)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
    }

    public Result LoadDefinitions(string configName)
    {
        try
        {
            var configPath = _configManager.GetConfigPath(configName);
            var modifierPath = Path.Combine(configPath, "Modifiers", "script_modifiers.json");
            if (!File.Exists(modifierPath))
                return Result.Failure($"Script modifiers file not found: {modifierPath}");

            var json = File.ReadAllText(modifierPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            var definitions = JsonSerializer.Deserialize<Dictionary<string, ScriptModifierDefinition>>(json, options);
            if (definitions == null)
                return Result.Failure("Failed to deserialize script modifiers");

            _definitions.Clear();
            foreach (var (key, definition) in definitions)
            {
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

        return _definitions.TryGetValue(modifierId, out var definition)
            ? Result<ScriptModifierDefinition>.Success(definition)
            : Result<ScriptModifierDefinition>.Failure($"Script modifier definition not found: {modifierId}");
    }

    public IReadOnlyList<ScriptModifierDefinition> GetAllDefinitions()
    {
        return _definitions.Values.ToList();
    }

    public Result<ScriptModifierInstance> ApplyModifier(string ownerId, string modifierId, int stacks = 1, int? duration = null, string? sourceId = null)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return Result<ScriptModifierInstance>.Failure("OwnerId cannot be empty");

        if (stacks <= 0)
            return Result<ScriptModifierInstance>.Failure("Stacks must be greater than 0");

        var definitionResult = GetDefinition(modifierId);
        if (!definitionResult.IsSuccess)
            return Result<ScriptModifierInstance>.Failure(definitionResult.Error);

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
                return Result<ScriptModifierInstance>.Success(updated);
            }

            var instance = new ScriptModifierInstance
            {
                ModifierId = definition.ModifierId,
                Definition = definition,
                OwnerId = ownerId,
                SourceId = sourceId,
                Stacks = System.Math.Min(stacks, definition.MaxStacks),
                Duration = duration ?? definition.DefaultDuration
            };
            list.Add(instance);
            return Result<ScriptModifierInstance>.Success(instance);
        }
    }

    public Result RemoveModifier(string ownerId, Guid instanceId)
    {
        var list = GetOwnerList(ownerId);
        lock (list)
        {
            var removed = list.RemoveAll(m => m.InstanceId == instanceId);
            return removed > 0 ? Result.Success() : Result.Failure($"Modifier instance {instanceId} not found");
        }
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
        lock (list)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var modifier = list[i];
                if (modifier.Duration == -1)
                    continue;

                var duration = modifier.Duration - 1;
                if (duration <= 0)
                    list.RemoveAt(i);
                else
                    list[i] = modifier with { Duration = duration };
            }
        }

        return Result.Success();
    }

    private List<ScriptModifierInstance> GetOwnerList(string ownerId)
    {
        return _activeModifiers.GetOrAdd(ownerId, _ => new List<ScriptModifierInstance>());
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

    private static float CalculateValue(ScriptModifierInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.Definition.FormulaValue))
        {
            var variables = new Dictionary<string, float>
            {
                ["stacks"] = instance.Stacks,
                ["duration"] = instance.Duration
            };

            if (TryEvaluateFormula(instance.Definition.FormulaValue, variables, out var value))
                return value;
        }

        return instance.Definition.BaseValue * instance.Stacks;
    }

    private static bool TryEvaluateFormula(string formula, Dictionary<string, float> variables, out float result)
    {
        result = 0f;
        var tokens = formula.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length % 2 == 0 || !TryReadValue(tokens[0], variables, out result))
            return false;

        for (var i = 1; i < tokens.Length; i += 2)
        {
            if (!TryReadValue(tokens[i + 1], variables, out var right))
                return false;

            result = tokens[i] switch
            {
                "+" => result + right,
                "-" => result - right,
                "*" => result * right,
                "/" when right != 0f => result / right,
                _ => result
            };
        }

        return true;
    }

    private static bool TryReadValue(string token, Dictionary<string, float> variables, out float value)
    {
        return variables.TryGetValue(token, out value) ||
            float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
