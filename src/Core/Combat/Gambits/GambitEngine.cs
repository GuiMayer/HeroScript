using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Entity;
using Core.Entity.Controllers;
using Core.Events;
using Core.Events.Domain;
using Core.Resources;

namespace Core.Combat.Gambits;

public sealed class GambitEngine : IGambitEngine
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly IDefinitionPersister? _persister;
    private readonly IEventBus? _eventBus;
    private readonly Dictionary<string, GambitDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private string? _loadedConfigName;
    private readonly IContentRuntimeResolver? _contentRuntimes;

    public GambitEngine(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        IEventBus? eventBus = null,
        IDefinitionPersister? persister = null,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _eventBus = eventBus;
        _persister = persister; // Optional for backward compatibility
        _contentRuntimes = contentRuntimes;
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
        var combatId = combatState.CombatId;
        var actorId = controlledEntity.EntityId;
        var candidates = ResolveCandidates(gambitIds, combatState.Determinism.ContentRevision);
        if (candidates.IsFailure)
            return Result<GambitDecision>.Failure(candidates.Error);
        foreach (var gambit in candidates.Value.OrderByDescending(g => g.Priority))
        {
            if (gambit.Conditions.All(condition => Matches(condition, controlledEntity, combatState, gambit.Action)))
            {
                _eventBus?.Publish(new GambitRuleMatchedEvent(combatId, actorId, gambit.GambitId, gambit.GambitId, gambit.Priority));
                var action = MapAction(gambit.Action, controlledEntity, combatState);
                _eventBus?.Publish(new GambitActionSelectedEvent(combatId, actorId, gambit.GambitId, action.PowerId ?? action.ActionType.ToString(), action.TargetId));
                return Result<GambitDecision>.Success(new GambitDecision
                {
                    Action = action,
                    GambitId = gambit.GambitId,
                    Priority = gambit.Priority,
                    Intent = gambit.Intent
                });
            }
        }

        // No rule matched — fall through to PASS
        _eventBus?.Publish(new GambitDecisionFailedEvent(combatId, actorId, gambitIds?.FirstOrDefault() ?? "default", "No gambit rule matched; defaulting to PASS"));
        return Result<GambitDecision>.Success(new GambitDecision
        {
            Action = new EntityAction { ActionType = Models.ActionType.PASS }
        });
    }

    private Result<IReadOnlyList<GambitDefinition>> ResolveCandidates(
        IEnumerable<string>? gambitIds,
        string contentRevision)
    {
        if (_contentRuntimes != null)
        {
            var runtime = _contentRuntimes.Resolve(contentRevision);
            if (runtime.IsFailure)
                return Result<IReadOnlyList<GambitDefinition>>.Failure(runtime.Error);

            var ids = gambitIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray()
                ?? runtime.Value.GetDefinitions("gambits").Keys.ToArray();
            var definitions = new List<GambitDefinition>();
            foreach (var id in ids)
            {
                var definition = runtime.Value.GetDefinition<GambitDefinition>("gambits", id);
                if (definition.IsFailure)
                    return Result<IReadOnlyList<GambitDefinition>>.Failure(definition.Error);
                definitions.Add(definition.Value with
                {
                    GambitId = string.IsNullOrWhiteSpace(definition.Value.GambitId)
                        ? id
                        : definition.Value.GambitId
                });
            }
            return Result<IReadOnlyList<GambitDefinition>>.Success(definitions);
        }

        if (gambitIds == null)
            return Result<IReadOnlyList<GambitDefinition>>.Success(GetAllDefinitions());

        return Result<IReadOnlyList<GambitDefinition>>.Success(gambitIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => _definitions.TryGetValue(id, out var definition) ? definition : null)
            .Where(definition => definition != null)
            .Cast<GambitDefinition>()
            .ToList());
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
    
    // ===== PERSISTÊNCIA =====
    
    public Result SaveDefinition(GambitDefinition definition, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (definition == null)
            return Result.Failure("Definition cannot be null");

        // Basic validation
        if (string.IsNullOrWhiteSpace(definition.GambitId))
            return Result.Failure("GambitId is required");

        if (definition.Conditions == null || definition.Conditions.Count == 0)
            return Result.Failure("At least one condition is required");

        if (definition.Action == null)
            return Result.Failure("Action is required");

        try
        {
            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(definition, options));
            
            // Save via persister
            var result = _persister.SaveDefinition("Gambits", definition.GambitId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Add to cache
            _definitions[definition.GambitId] = definition;

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to save gambit definition: {ex.Message}");
        }
    }

    public Result UpdateDefinition(string gambitId, GambitDefinition updatedDefinition, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (string.IsNullOrWhiteSpace(gambitId))
            return Result.Failure("GambitId cannot be empty");

        if (updatedDefinition == null)
            return Result.Failure("Updated definition cannot be null");

        // Ensure IDs match
        if (updatedDefinition.GambitId != gambitId)
            return Result.Failure($"GambitId mismatch: URL has '{gambitId}' but definition has '{updatedDefinition.GambitId}'");

        // Basic validation
        if (updatedDefinition.Conditions == null || updatedDefinition.Conditions.Count == 0)
            return Result.Failure("At least one condition is required");

        if (updatedDefinition.Action == null)
            return Result.Failure("Action is required");

        try
        {
            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            
            var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(updatedDefinition, options));
            
            // Update via persister
            var result = _persister.UpdateDefinition("Gambits", gambitId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Update cache
            _definitions[gambitId] = updatedDefinition;

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to update gambit definition: {ex.Message}");
        }
    }

    public Result DeleteDefinition(string gambitId, string configName = "default")
    {
        if (_persister == null)
            return Result.Failure("DefinitionPersister not available");

        if (string.IsNullOrWhiteSpace(gambitId))
            return Result.Failure("GambitId cannot be empty");

        try
        {
            // Delete via persister
            var result = _persister.DeleteDefinition("Gambits", gambitId, configName);
            if (result.IsFailure)
                return result;

            // Remove from cache
            _definitions.Remove(gambitId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete gambit definition: {ex.Message}");
        }
    }
}
