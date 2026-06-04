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
    private readonly IResourceLoader _resourceLoader;
    private readonly Dictionary<string, GambitDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private string? _loadedConfigName;

    public GambitEngine(IConfigManager configManager, IResourceLoader resourceLoader)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
    }

    public Result LoadDefinitions(string configName)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            var definitions = LoadDefinitionJson(configName, options);
            if (definitions == null)
                return Result.Failure("Failed to deserialize gambits");

            _definitions.Clear();
            _loadedConfigName = configName;
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
        if (_definitions.TryGetValue(gambitId, out var definition))
            return Result<GambitDefinition>.Success(definition);

        if (!string.IsNullOrWhiteSpace(_loadedConfigName))
        {
            var reload = LoadDefinitions(_loadedConfigName);
            if (reload.IsSuccess && _definitions.TryGetValue(gambitId, out definition))
                return Result<GambitDefinition>.Success(definition);
        }

        return Result<GambitDefinition>.Failure($"Gambit definition not found: {gambitId}");
    }

    public IReadOnlyList<GambitDefinition> GetAllDefinitions()
    {
        return _definitions.Values.OrderByDescending(g => g.Priority).ToList();
    }

    public Result<EntityAction> DecideAction(Entity.Entity controlledEntity, Models.CombatState combatState, IEnumerable<string>? gambitIds = null)
    {
        var decision = DecideActionWithMetadata(controlledEntity, combatState, gambitIds);
        return decision.IsSuccess
            ? Result<EntityAction>.Success(decision.Value.Action)
            : Result<EntityAction>.Failure(decision.Error);
    }

    public Result<GambitDecision> DecideActionWithMetadata(Entity.Entity controlledEntity, Models.CombatState combatState, IEnumerable<string>? gambitIds = null)
    {
        var candidates = ResolveCandidates(gambitIds);
        foreach (var gambit in candidates.OrderByDescending(g => g.Priority))
        {
            if (gambit.Conditions.All(condition => Matches(condition, controlledEntity, combatState, gambit.Action)))
            {
                return Result<GambitDecision>.Success(new GambitDecision
                {
                    Action = MapAction(gambit.Action, controlledEntity, combatState),
                    GambitId = gambit.GambitId,
                    Priority = gambit.Priority,
                    Intent = gambit.Intent
                });
            }
        }

        return Result<GambitDecision>.Success(new GambitDecision
        {
            Action = new EntityAction { ActionType = Models.ActionType.PASS }
        });
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
            GambitConditionType.ACTOR_RESOURCE_PERCENT => Compare(ResourcePercent(state.GetEntity(controlledEntity.EntityId), condition.ResourceId), condition),
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
            "ACTOR" => state.GetEntity(controlledEntity.EntityId),
            "HERO" => state.Hero,
            "FIRST_ALIVE_ENEMY" or "TARGET" => state.Enemies.FirstOrDefault(e => e.IsAlive),
            "FIRST_ALIVE_OPPONENT" => FirstAliveOpponent(controlledEntity, state),
            _ when !string.IsNullOrWhiteSpace(target) => state.GetEntity(target),
            _ => null
        };
    }

    private static Models.CombatEntity? FirstAliveOpponent(Entity.Entity controlledEntity, Models.CombatState state)
    {
        var actor = state.GetEntity(controlledEntity.EntityId);
        if (actor == null)
            return null;

        return actor.IsHero
            ? state.Enemies.FirstOrDefault(e => e.IsAlive)
            : state.Hero.IsAlive ? state.Hero : null;
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

    private Dictionary<string, GambitDefinition>? LoadDefinitionJson(string configName, JsonSerializerOptions options)
    {
        var chain = _configManager.ResolveInheritanceChain(configName);
        var data = _resourceLoader.LoadResource("Gambits/gambits.json", chain, strictMode: false);
        if (data.Count == 0)
            return null;

        return data.ToDictionary(
            kvp => kvp.Key,
            kvp => JsonSerializer.Deserialize<GambitDefinition>(kvp.Value.GetRawText(), options)!,
            StringComparer.OrdinalIgnoreCase);
    }
}
