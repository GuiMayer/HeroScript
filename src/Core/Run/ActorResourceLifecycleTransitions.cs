using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Resources;

namespace Core.Run;

/// <summary>Pure transfer planner. Neither operation changes wallet, persistent bounds, RNG or history.</summary>
public static class ActorResourceLifecycleTransitions
{
    public static Result<CombatActorState> Enter(EntityState player, CombatActorState actor,
        ActorResourceLifecyclePolicyDefinition? policy)
    {
        if (policy == null) return Result<CombatActorState>.Success(actor);
        var transfer = Transfer(player, actor, policy, true, CombatStatus.ACTIVE, false);
        return transfer.IsFailure ? Result<CombatActorState>.Failure(transfer.Error)
            : Result<CombatActorState>.Success(actor.WithResourceState(transfer.Value));
    }

    public static Result<EntityState> Exit(EntityState player, CombatActorState actor,
        ActorResourceLifecyclePolicyDefinition? policy, CombatStatus outcome, bool retry = false)
    {
        if (policy == null) return Result<EntityState>.Success(player);
        if (outcome == CombatStatus.ACTIVE || !Enum.IsDefined(outcome))
            return Result<EntityState>.Failure("Actor resource exit requires a terminal encounter");
        var transfer = Transfer(player, actor, policy, false, outcome, retry);
        if (transfer.IsFailure) return Result<EntityState>.Failure(transfer.Error);
        var component = player.Component<ResourceEntityComponentState>();
        if (component == null) return Result<EntityState>.Success(player); // Only ignored/encounter-only rules.
        return Result<EntityState>.Success(player with { Components = player.Components.ToImmutableDictionary(StringComparer.Ordinal)
            .SetItem(component.ComponentId, component with { State = transfer.Value }) });
    }

    private static Result<ResourceSet> Transfer(EntityState player, CombatActorState actor,
        ActorResourceLifecyclePolicyDefinition policy, bool entering, CombatStatus outcome, bool retry)
    {
        var valid = ActorResourceLifecyclePolicyValidator.Validate(policy);
        if (valid.IsFailure) return Result<ResourceSet>.Failure(valid.Error);
        if (player.InstanceId != actor.InstanceId || player.DefinitionId != actor.DefinitionId || player.ContentRevision != actor.ContentRevision)
            return Result<ResourceSet>.Failure("Actor resource transfer identity/revision mismatch");
        var persistent = player.Component<ResourceEntityComponentState>()?.State ?? new ResourceSet { OwnerId = player.InstanceId };
        var encounter = actor.ResourceState;
        if (persistent.OwnerId != player.InstanceId || encounter.OwnerId != actor.InstanceId)
            return Result<ResourceSet>.Failure("Actor resource transfer owner mismatch");
        var target = entering ? encounter : persistent;
        var pools = target.Resources.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in policy.Rules.OrderBy(rule => rule.ResourceId, StringComparer.Ordinal))
        {
            var action = entering ? rule.Entry : rule.Exit(outcome, retry);
            if (action == ActorResourceLifecycleAction.EncounterOnly) continue;
            var sourcePool = (entering ? persistent : encounter).Get(rule.ResourceId);
            var targetPool = target.Get(rule.ResourceId);
            if (sourcePool == null || targetPool == null)
            {
                if (rule.MissingResource == MissingActorResourceBehavior.Ignore) continue;
                return Result<ResourceSet>.Failure($"Actor resource '{rule.ResourceId}' is missing during {(entering ? "entry" : "exit")}");
            }
            var basis = entering ? sourcePool : targetPool;
            var current = action switch
            {
                ActorResourceLifecycleAction.PreserveCurrent => sourcePool.Current,
                ActorResourceLifecycleAction.ResetToMaximum => basis.Maximum,
                ActorResourceLifecycleAction.ResetToConfiguredValue => rule.ConfiguredValue!.Value,
                _ => throw new InvalidOperationException("Policy must be validated before transport")
            };
            // On entry persistent bounds win too; temporary encounter bounds are never promoted on exit.
            pools = pools.SetItem(rule.ResourceId, basis.Set(current));
        }
        return Result<ResourceSet>.Success(target with { Resources = pools });
    }
}
