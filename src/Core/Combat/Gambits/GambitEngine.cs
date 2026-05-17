using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Config;
using Core.Entity;
using Core.Entity.Controllers;

namespace Core.Combat.Gambits;

public sealed class GambitEngine : IGambitEngine
{
    private readonly IConfigManager _configManager;
    private readonly Dictionary<string, GambitDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    public GambitEngine(IConfigManager configManager)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
    }

    public Result LoadDefinitions(string configName)
    {
        try
        {
            var configPath = _configManager.GetConfigPath(configName);
            var gambitPath = Path.Combine(configPath, "Gambits", "gambits.json");
            if (!File.Exists(gambitPath))
                return Result.Failure($"Gambits file not found: {gambitPath}");

            var json = File.ReadAllText(gambitPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            var definitions = JsonSerializer.Deserialize<Dictionary<string, GambitDefinition>>(json, options);
            if (definitions == null)
                return Result.Failure("Failed to deserialize gambits");

            _definitions.Clear();
            foreach (var (key, definition) in definitions)
            {
                var gambitId = string.IsNullOrWhiteSpace(definition.GambitId) ? key : definition.GambitId;
                _definitions[gambitId] = definition with { GambitId = gambitId };
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to load gambits: {ex.Message}");
        }
    }

    public Result<GambitDefinition> GetDefinition(string gambitId)
    {
        return _definitions.TryGetValue(gambitId, out var definition)
            ? Result<GambitDefinition>.Success(definition)
            : Result<GambitDefinition>.Failure($"Gambit definition not found: {gambitId}");
    }

    public IReadOnlyList<GambitDefinition> GetAllDefinitions()
    {
        return _definitions.Values.OrderByDescending(g => g.Priority).ToList();
    }

    public Result<EntityAction> DecideAction(Entity.Entity controlledEntity, Models.CombatState combatState, IEnumerable<string>? gambitIds = null)
    {
        var candidates = ResolveCandidates(gambitIds);
        foreach (var gambit in candidates.OrderByDescending(g => g.Priority))
        {
            if (gambit.Conditions.All(condition => Matches(condition, controlledEntity, combatState, gambit.Action)))
                return Result<EntityAction>.Success(MapAction(gambit.Action, controlledEntity, combatState));
        }

        return Result<EntityAction>.Success(new EntityAction { ActionType = Models.ActionType.PASS });
    }

    private IReadOnlyList<GambitDefinition> ResolveCandidates(IEnumerable<string>? gambitIds)
    {
        if (gambitIds == null)
            return GetAllDefinitions();

        return gambitIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => _definitions.TryGetValue(id, out var definition) ? definition : null)
            .Where(definition => definition != null)
            .Cast<GambitDefinition>()
            .ToList();
    }

    private static bool Matches(GambitCondition condition, Entity.Entity controlledEntity, Models.CombatState state, GambitActionDefinition action)
    {
        return condition.Type switch
        {
            GambitConditionType.ALWAYS => true,
            GambitConditionType.ANY_ENEMY_ALIVE => state.Enemies.Any(e => e.IsAlive),
            GambitConditionType.TURN_GREATER_THAN_OR_EQUAL => Compare(state.CurrentTurn, condition),
            GambitConditionType.SELF_RESOURCE_PERCENT => Compare(ResourcePercent(state.GetEntity(controlledEntity.EntityId), condition.ResourceId), condition),
            GambitConditionType.HERO_RESOURCE_PERCENT => Compare(ResourcePercent(state.Hero, condition.ResourceId), condition),
            GambitConditionType.TARGET_RESOURCE_PERCENT => Compare(ResourcePercent(ResolveTarget(action.Target, controlledEntity, state), condition.ResourceId), condition),
            _ => false
        };
    }

    private static EntityAction MapAction(GambitActionDefinition action, Entity.Entity controlledEntity, Models.CombatState state)
    {
        return new EntityAction
        {
            ActionType = action.ActionType,
            PowerId = action.PowerId,
            TargetId = ResolveTarget(action.Target, controlledEntity, state)?.EntityId,
            CostOptionId = action.CostOptionId
        };
    }

    private static Models.CombatEntity? ResolveTarget(string? target, Entity.Entity controlledEntity, Models.CombatState state)
    {
        return target?.ToUpperInvariant() switch
        {
            "SELF" => state.GetEntity(controlledEntity.EntityId),
            "HERO" => state.Hero,
            "FIRST_ALIVE_ENEMY" or "TARGET" => state.Enemies.FirstOrDefault(e => e.IsAlive),
            _ when !string.IsNullOrWhiteSpace(target) => state.GetEntity(target),
            _ => null
        };
    }

    private static float ResourcePercent(Models.CombatEntity? entity, string? resourceId)
    {
        if (entity == null || string.IsNullOrWhiteSpace(resourceId))
            return 0f;

        var resource = entity.GetResource(resourceId);
        if (resource == null || resource.Maximum <= 0)
            return 0f;

        return resource.Current / resource.Maximum;
    }

    private static bool Compare(float value, GambitCondition condition)
    {
        if (condition.LessThanOrEqual.HasValue && value > condition.LessThanOrEqual.Value)
            return false;

        return !condition.GreaterThanOrEqual.HasValue || value >= condition.GreaterThanOrEqual.Value;
    }
}
