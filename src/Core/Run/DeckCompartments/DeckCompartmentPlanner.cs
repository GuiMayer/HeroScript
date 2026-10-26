using System.Collections.Immutable;
using Core.CardZones;
using Core.Combat.Models;
using Core.Common;
using Core.Content;

namespace Core.Run.DeckCompartments;

public static class DeckCompartmentAccess
{
    public static Result<DeckCompartmentProfileDefinition> Profile(RunState run, ContentRuntime runtime, string actorId)
    {
        if (runtime.Manifest.Revision != run.Determinism.ContentRevision ||
            !string.Equals(runtime.Manifest.ConfigName, run.ConfigName, StringComparison.OrdinalIgnoreCase))
            return Result<DeckCompartmentProfileDefinition>.Failure("Compartment runtime does not match the run revision/setting");
        if (run.ResolvedMode?.Definition.DeckCompartmentPolicyId is not { } policyId)
            return Result<DeckCompartmentProfileDefinition>.Failure("Deck compartments are not enabled by this mode");
        var policy = runtime.GetDefinition<DeckCompartmentPolicyDefinition>("deck-compartment-policies", policyId);
        if (policy.IsFailure) return Result<DeckCompartmentProfileDefinition>.Failure(policy.Error);
        if (actorId != run.PlayerEntityId || run.PlayerEntity?.InstanceId != actorId)
            return Result<DeckCompartmentProfileDefinition>.Failure("Compartment actor is not the run's current character");
        var binding = run.PlayerEntity.Component<DeckCompartmentProfileComponentState>();
        if (binding == null) return Result<DeckCompartmentProfileDefinition>.Failure("Character has no compartment profile");
        var profile = runtime.GetDefinition<DeckCompartmentProfileDefinition>("deck-compartment-profiles", binding.ProfileId);
        if (profile.IsFailure) return profile;
        var valid = profile.Value.Validate(runtime);
        return valid.IsFailure ? Result<DeckCompartmentProfileDefinition>.Failure(valid.Error) : profile;
    }

    public static Result CanChange(RunState run, ContentRuntime runtime)
    {
        if (run.Lifecycle != RunLifecycleState.Active || run.ActiveEncounterId != null)
            return Result.Failure("Compartment changes require an active run outside combat and pending encounter resolution");
        var id = run.ResolvedMode?.Definition.DeckCompartmentPolicyId;
        if (id == null) return Result.Failure("Deck compartments are disabled");
        var policy = runtime.GetDefinition<DeckCompartmentPolicyDefinition>("deck-compartment-policies", id);
        if (policy.IsFailure) return Result.Failure(policy.Error);
        var node = run.Map.Nodes.FirstOrDefault(n => n.NodeId == run.CurrentNodeId);
        if (policy.Value.PreparationOnly && (node?.Activity.Type != RunActivityType.Preparation ||
            run.CompletedActivityNodeIds.Contains(node.NodeId) || run.Map.ResolvedNodeIds.Contains(node.NodeId)))
            return Result.Failure("Compartment changes require an unfinished preparation activity");
        return Result.Success();
    }

    public static bool Owns(RunState run, string actorId, CardInstanceState card) => actorId == run.PlayerEntityId &&
        (card.OwnerId == actorId || card.OwnerId == "$run") && card.Persistence == CardInstancePersistence.Run &&
        card.Lifetime.Strategy == CardInstanceLifetimeStrategy.Run;
}

/// <summary>Plans the final distribution atomically. No RNG, zone movement or writes.</summary>
public static class DeckCompartmentPlanner
{
    public static Result<DeckCompartmentChangePlan> Plan(RunState run, ContentRuntime runtime, string actorId,
        IReadOnlyList<DeckCompartmentAllocation> distribution)
    {
        var profile = DeckCompartmentAccess.Profile(run, runtime, actorId);
        if (profile.IsFailure) return Result<DeckCompartmentChangePlan>.Failure(profile.Error);
        var access = DeckCompartmentAccess.CanChange(run, runtime);
        if (access.IsFailure) return Result<DeckCompartmentChangePlan>.Failure(access.Error);
        if (distribution == null || distribution.Count > 512 || distribution.Any(a => a == null || a.CardInstanceId == Guid.Empty) ||
            distribution.Select(a => a.CardInstanceId).Distinct().Count() != distribution.Count)
            return Result<DeckCompartmentChangePlan>.Failure("Distribution requires at most 512 unique card instance IDs");
        var compartments = profile.Value.Compartments.ToDictionary(c => c.CompartmentId, StringComparer.Ordinal);
        foreach (var allocation in distribution)
        {
            var card = run.Deck.GetCard(allocation.CardInstanceId);
            if (card == null || !DeckCompartmentAccess.Owns(run, actorId, card))
                return Result<DeckCompartmentChangePlan>.Failure($"Card is not an eligible persistent instance of this character: {allocation.CardInstanceId}");
            if (allocation.CompartmentId == null || !compartments.ContainsKey(allocation.CompartmentId))
                return Result<DeckCompartmentChangePlan>.Failure("Unknown compartment in character profile");
        }
        if (distribution.GroupBy(a => a.CompartmentId, StringComparer.Ordinal).Any(g => g.Count() > compartments[g.Key].Capacity))
            return Result<DeckCompartmentChangePlan>.Failure("Compartment capacity exceeded");
        var state = run.DeckCompartments ?? new();
        var before = state.Actors.GetValueOrDefault(actorId) ?? new() { ActorId = actorId, ProfileId = profile.Value.ProfileId };
        if (before.ProfileId != profile.Value.ProfileId)
            return Result<DeckCompartmentChangePlan>.Failure("Character compartment profile changed; explicit reconciliation is required");
        var after = before with { Allocations = distribution.ToImmutableSortedDictionary(a => a.CardInstanceId, a => a.CompartmentId) };
        var candidate = run with { DeckCompartments = state with { Actors = state.Actors.SetItem(actorId, after) } };
        return Result<DeckCompartmentChangePlan>.Success(new(candidate, before, after));
    }

    /// <summary>Called within the same lifecycle transaction as definitive instance destruction.</summary>
    public static RunState ReconcileDestroyedCards(RunState run)
    {
        if (run.DeckCompartments == null) return run;
        var state = run.DeckCompartments;
        foreach (var (id, actor) in state.Actors)
        {
            var missing = actor.Allocations.Keys.Where(card => run.Deck.GetCard(card) == null).ToArray();
            if (missing.Length > 0) state = state with { Actors = state.Actors.SetItem(id,
                actor with { Allocations = actor.Allocations.RemoveRange(missing) }) };
        }
        return ReferenceEquals(state, run.DeckCompartments) ? run : run with { DeckCompartments = state };
    }
}
