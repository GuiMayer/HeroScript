using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Combat.Models;
using Core.Config;
using Core.Content;
using Core.Resources;
using System.Collections.Immutable;

namespace Core.Combat.Gambits;

public sealed class GambitEngine : IGambitEngine
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly IDefinitionPersister? _persister;
    private ImmutableDictionary<string, GambitDefinition> _definitions =
        ImmutableDictionary<string, GambitDefinition>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private volatile string? _loadedConfigName;
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private readonly Core.Math.IRuntimeFormulaEvaluator? _formulas;

    public GambitEngine(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        IDefinitionPersister? persister = null,
        IContentRuntimeResolver? contentRuntimes = null,
        Core.Math.IRuntimeFormulaEvaluator? formulas = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _persister = persister;
        _contentRuntimes = contentRuntimes;
        _formulas = formulas;
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

            var loaded = ImmutableDictionary.CreateBuilder<string, GambitDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, definition) in definitions)
            {
                if (!string.Equals(definition.GambitId, key, StringComparison.Ordinal))
                {
                    return Result.Failure(
                        $"Gambit definition identity mismatch: expected {key}, got {definition.GambitId}");
                }
                loaded[key] = definition;
            }

            Interlocked.Exchange(ref _definitions, loaded.ToImmutable());
            _loadedConfigName = configName;

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

    public Result<EntityAction> DecideAction(CombatActorState controlledActor, Models.CombatState combatState, IEnumerable<string>? gambitIds = null)
    {
        var decision = DecideActionWithMetadata(controlledActor, combatState, gambitIds);
        return decision.IsSuccess
            ? Result<EntityAction>.Success(decision.Value.Action)
            : Result<EntityAction>.Failure(decision.Error);
    }

    public Result<GambitDecision> DecideActionWithMetadata(CombatActorState controlledActor, Models.CombatState combatState, IEnumerable<string>? gambitIds = null)
    {
        var actorId = controlledActor.InstanceId;
        var candidates = ResolveCandidates(gambitIds, combatState.Determinism.ContentRevision);
        if (candidates.IsFailure)
            return Result<GambitDecision>.Failure(candidates.Error);
        foreach (var gambit in candidates.Value.OrderByDescending(g => g.Priority))
        {
            if (gambit.Conditions.All(condition => Matches(condition, controlledActor, combatState, gambit.Action)))
            {
                var action = MapAction(gambit.Action, controlledActor, combatState);
                if (action.ActionType is not (Models.ActionType.PASS or Models.ActionType.END_TURN))
                {
                    var actionTags = new HashSet<string>(StringComparer.Ordinal) { "action", "ability" };
                    if (_contentRuntimes != null)
                    {
                        var runtime = _contentRuntimes.Resolve(combatState.Determinism.ContentRevision);
                        if (runtime.IsFailure) return Result<GambitDecision>.Failure(runtime.Error);
                        var definition = runtime.Value.GetDefinition<ActionDefinition>("actions", action.PowerId ?? "basic_attack");
                        if (definition.IsFailure) return Result<GambitDecision>.Failure(definition.Error);
                        actionTags.UnionWith(definition.Value.Tags);
                    }
                    var constraint = Core.StatusEffects.StatusActionConstraints.Evaluate(combatState,
                        combatState.GetActor(actorId)!, actionTags, _formulas, combatState.Determinism.ContentRevision);
                    if (constraint.IsFailure) return Result<GambitDecision>.Failure(constraint.Error);
                    if (!constraint.Value.IsEmpty) continue;
                }
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
                definitions.Add(definition.Value);
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

    private static bool Matches(GambitCondition condition, CombatActorState controlledActor, Models.CombatState state, GambitActionDefinition action)
    {
        return condition.Type switch
        {
            GambitConditionType.ALWAYS => true,
            GambitConditionType.ANY_OPPONENT_ALIVE => FirstAliveOpponent(controlledActor, state) != null,
            GambitConditionType.TURN_GREATER_THAN_OR_EQUAL => Compare(state.CurrentTurn, condition),
            GambitConditionType.SELF_RESOURCE_PERCENT => Compare(ResourcePercent(state.GetActor(controlledActor.InstanceId), condition.ResourceId), condition),
            GambitConditionType.ACTOR_RESOURCE_PERCENT => Compare(ResourcePercent(state.GetActor(controlledActor.InstanceId), condition.ResourceId), condition),
            GambitConditionType.TARGET_RESOURCE_PERCENT => Compare(ResourcePercent(ResolveTarget(action.Target, controlledActor, state), condition.ResourceId), condition),
            _ => false
        };
    }

    private static EntityAction MapAction(GambitActionDefinition action, CombatActorState controlledActor, Models.CombatState state)
    {
        return new EntityAction
        {
            ActionType = action.ActionType,
            PowerId = action.PowerId,
            TargetId = ResolveTarget(action.Target, controlledActor, state)?.InstanceId,
            CostOptionId = action.CostOptionId
        };
    }

    private static CombatActorState? ResolveTarget(string? target, CombatActorState controlledActor, Models.CombatState state)
    {
        return target?.ToUpperInvariant() switch
        {
            "SELF" => state.GetActor(controlledActor.InstanceId),
            "ACTOR" => state.GetActor(controlledActor.InstanceId),
            "FIRST_ALIVE_OPPONENT" or "TARGET" => FirstAliveOpponent(controlledActor, state),
            _ when !string.IsNullOrWhiteSpace(target) => state.GetActor(target),
            _ => null
        };
    }

    private static CombatActorState? FirstAliveOpponent(CombatActorState controlledActor, Models.CombatState state)
    {
        var actor = state.GetActor(controlledActor.InstanceId);
        if (actor == null)
            return null;

        return state.GetAllActors()
            .Where(candidate => candidate.IsAlive &&
                                !string.Equals(candidate.InstanceId, actor.InstanceId, StringComparison.Ordinal) &&
                                state.Relationship(actor, candidate) == SideRelationship.Enemy)
            .OrderBy(candidate => candidate.InstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static float ResourcePercent(CombatActorState? entity, string? resourceId)
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
        var data = _resourceLoader.LoadResource("gambits/gambits.json", chain, strictMode: true);
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
            var result = _persister.SaveDefinition("gambits", definition.GambitId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Add to cache
            ImmutableInterlocked.AddOrUpdate(
                ref _definitions,
                definition.GambitId,
                definition,
                (_, _) => definition);

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
            var result = _persister.UpdateDefinition("gambits", gambitId, jsonDoc, configName);
            if (result.IsFailure)
                return result;

            // Update cache
            ImmutableInterlocked.AddOrUpdate(
                ref _definitions,
                gambitId,
                updatedDefinition,
                (_, _) => updatedDefinition);

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
            var result = _persister.DeleteDefinition("gambits", gambitId, configName);
            if (result.IsFailure)
                return result;

            // Remove from cache
            ImmutableInterlocked.TryRemove(ref _definitions, gambitId, out _);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete gambit definition: {ex.Message}");
        }
    }
}
