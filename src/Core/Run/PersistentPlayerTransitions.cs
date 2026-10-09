using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Entity;
using Core.Entity.Definitions;
using Core.Resources;

namespace Core.Run;

/// <summary>Persistent attributes and actor resources. Combat resources are transported only by mode policy.</summary>
public static class PersistentPlayerTransitions
{
    public static Result<RunState> ResolveEncounter(RunState run, CombatState combat, bool retry)
    {
        var policy = run.ResolvedMode?.ActorResourceLifecyclePolicy;
        if (policy == null) return Result<RunState>.Success(run);
        var player = run.PlayerEntity;
        var actor = combat.GetActor(run.PlayerEntityId);
        if (player == null || actor == null || player.InstanceId != run.PlayerEntityId ||
            player.ContentRevision != run.Determinism.ContentRevision)
            return Result<RunState>.Failure("Persistent player is missing or incompatible with resolved encounter");
        var promoted = ActorResourceLifecycleTransitions.Exit(player, actor, policy, combat.Status, retry);
        return promoted.IsFailure ? Result<RunState>.Failure(promoted.Error)
            : Result<RunState>.Success(run with { PlayerEntity = promoted.Value });
    }

    public static RunState ApplyResourceConsequences(RunState run) => run.ActiveEncounterId == null &&
        run.Lifecycle != RunLifecycleState.Abandoned && run.PlayerEntity?.Component<ResourceEntityComponentState>() is { } resources &&
        ResourceThresholdEvaluator.IsOwnerDefeated(resources.State.Resources.Values)
        ? run with { Lifecycle = RunLifecycleState.Failed } : run;

    public static Result<EntityState> Create(string instanceId, string definitionId, ContentRuntime runtime)
        => Create(instanceId, definitionId, runtime, true);

    private static Result<EntityState> Create(string instanceId, string definitionId, ContentRuntime runtime, bool resources)
    {
        var definition = runtime.GetDefinition<EntityDefinition>("entities", definitionId);
        if (definition.IsFailure) return Result<EntityState>.Failure(definition.Error);
        var valid = EntityDefinitionValidator.Validate(definition.Value);
        if (valid.IsFailure) return Result<EntityState>.Failure(valid.Error);
        var components = ImmutableDictionary.CreateBuilder<string, EntityComponentState>(StringComparer.Ordinal);
        foreach (var stats in definition.Value.Components.OfType<StatEntityComponentDefinition>())
            components.Add(stats.ComponentId, new StatEntityComponentState { ComponentId = stats.ComponentId,
                Values = stats.Values, ValueRules = stats.ValueRules });
        if (resources)
        foreach (var component in definition.Value.Components.OfType<ResourceEntityComponentDefinition>())
        {
            var pools = new Dictionary<string, ResourcePool>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, configured) in component.Pools.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var resource = runtime.GetDefinition<ResourceDefinition>("resources", id);
                if (resource.IsFailure) return Result<EntityState>.Failure(resource.Error);
                try { pools.Add(id, ResourcePool.Materialize(resource.Value, configured.Current, configured.Max)); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException)
                { return Result<EntityState>.Failure($"Persistent resource '{id}': {error.Message}"); }
            }
            components.Add(component.ComponentId, new ResourceEntityComponentState { ComponentId = component.ComponentId,
                State = new() { OwnerId = instanceId, Resources = pools } });
        }
        return Result<EntityState>.Success(new() { InstanceId = instanceId, DefinitionId = definitionId,
            ContentRevision = runtime.Manifest.Revision, Name = definition.Value.DisplayName,
            Components = components.ToImmutable() });
    }

    public static Result<CombatState> Materialize(RunState run, CombatState combat)
    {
        if (run.PlayerEntity == null) return Result<CombatState>.Success(combat);
        var player = run.PlayerEntity;
        var actor = combat.GetActor(run.PlayerEntityId);
        if (player.InstanceId != run.PlayerEntityId || actor == null || actor.DefinitionId != player.DefinitionId ||
            actor.ContentRevision != player.ContentRevision || player.ContentRevision != run.Determinism.ContentRevision)
            return Result<CombatState>.Failure("Persistent player identity/revision does not match encounter");
        var components = actor.Components.Where(pair => pair.Value is not StatEntityComponentState).ToImmutableDictionary(StringComparer.Ordinal);
        var materialized = actor with { Components = components.SetItems(player.Components.Where(pair => pair.Value is StatEntityComponentState)) };
        var transported = ActorResourceLifecycleTransitions.Enter(player, materialized, run.ResolvedMode?.ActorResourceLifecyclePolicy);
        return transported.IsFailure ? Result<CombatState>.Failure(transported.Error)
            : Result<CombatState>.Success(combat.ReplaceActor(transported.Value));
    }

    public static Result<EntityState> Rebind(EntityState player, ContentRuntime runtime)
        => Rebind(player, runtime, true);

    public static Result<EntityState> RebindAttributes(EntityState player, ContentRuntime runtime)
        => Rebind(player, runtime, false);

    private static Result<EntityState> Rebind(EntityState player, ContentRuntime runtime, bool resources)
    {
        var declared = Create(player.InstanceId, player.DefinitionId, runtime, resources);
        if (declared.IsFailure) return declared;
        if (!player.Components.Keys.Order(StringComparer.Ordinal).SequenceEqual(declared.Value.Components.Keys.Order(StringComparer.Ordinal)))
            return Result<EntityState>.Failure("Persistent attribute component schema changed");
        var components = ImmutableDictionary.CreateBuilder<string, EntityComponentState>(StringComparer.Ordinal);
        foreach (var (id, stored) in player.Components)
        {
            if (stored is ResourceEntityComponentState pools && declared.Value.Components[id] is ResourceEntityComponentState resourceSchema)
            {
                if (pools.State.OwnerId != player.InstanceId ||
                    !pools.State.Resources.Keys.Order(StringComparer.Ordinal).SequenceEqual(resourceSchema.State.Resources.Keys.Order(StringComparer.Ordinal)))
                    return Result<EntityState>.Failure("Persistent resource owner/schema changed");
                var rebound = pools.State.RebindDefinitions(resourceSchema.State.Resources.ToDictionary(pair => pair.Key, pair => pair.Value.Definition));
                if (rebound.IsFailure) return Result<EntityState>.Failure(rebound.Error);
                components.Add(id, pools with { State = rebound.Value });
                continue;
            }
            if (stored is not StatEntityComponentState stats || declared.Value.Components[id] is not StatEntityComponentState schema ||
                !stats.Values.Keys.Order(StringComparer.Ordinal).SequenceEqual(schema.Values.Keys.Order(StringComparer.Ordinal)))
                return Result<EntityState>.Failure("Persistent attribute value schema changed");
            var valid = EntityAttributeTransitions.Validate(stats.Values, schema.ValueRules);
            if (valid.IsFailure) return Result<EntityState>.Failure(valid.Error);
            components.Add(id, stats with { ValueRules = schema.ValueRules });
        }
        return Result<EntityState>.Success(player with { ContentRevision = runtime.Manifest.Revision, Components = components.ToImmutable() });
    }
}
