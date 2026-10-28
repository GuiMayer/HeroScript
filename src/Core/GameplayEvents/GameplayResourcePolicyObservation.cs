using Core.Common;
using Core.Combat.Models;
using Core.Resources;
using Core.Run;

namespace Core.GameplayEvents;

public sealed record ResourceRestrictionPayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.ResourceRestriction;
    public GameplayObjectReference Owner { get; init; } = null!;
    public string ComponentId { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public float? BeforeCeiling { get; init; }
    public float? AfterCeiling { get; init; }
    public override Result Validate() => Owner is { IsValid: true } && Owner.Kind == "entity" && EventValue.Valid(ComponentId) && EventValue.Valid(ResourceId) &&
        (!BeforeCeiling.HasValue || float.IsFinite(BeforeCeiling.Value)) && (!AfterCeiling.HasValue || float.IsFinite(AfterCeiling.Value))
        ? Result.Success() : Result.Failure("Invalid resource restriction fact");
}

/// <summary>Projects already materialized policies and their real Set operand. Does not resolve the ceiling again.</summary>
public static class GameplayResourcePolicyObservation
{
    public static Result<GameplayEffectObservation> Append(GameplayEffectObservation observation, CombatState before, CombatState after)
    {
        if (ReferenceEquals(before, after)) return Result<GameplayEffectObservation>.Success(observation);
        var current = observation;
        foreach (var actor in before.GetAllActors())
        {
            var updated = after.GetActor(actor.InstanceId);
            if (updated == null) return Result<GameplayEffectObservation>.Failure("Policy materialization lost an actor");
            var observed = Actor(current, actor.InstanceId, actor.Components, updated.Components,
                before.CombatId.ToString("D"));
            if (observed.IsFailure) return observed;
            current = observed.Value;
        }
        return Result<GameplayEffectObservation>.Success(current);
    }

    public static Result<GameplayEffectObservation> Append(GameplayEffectObservation observation, RunState before, RunState after)
    {
        if (ReferenceEquals(before, after)) return Result<GameplayEffectObservation>.Success(observation);
        var current = observation;
        if (before.PlayerEntity is { } player)
        {
            if (after.PlayerEntity is not { } updated) return Result<GameplayEffectObservation>.Failure("Policy materialization lost its persistent actor");
            var observed = Actor(current, player.InstanceId, player.Components, updated.Components, observation.Events.Scope.BranchId);
            if (observed.IsFailure) return observed;
            current = observed.Value;
        }
        foreach (var encounter in before.Encounters.Where(item => !item.Resolved))
        {
            var updated = after.GetEncounter(encounter.Combat.CombatId);
            if (updated == null) return Result<GameplayEffectObservation>.Failure("Policy materialization lost an encounter");
            var observed = Append(current, encounter.Combat, updated.Combat);
            if (observed.IsFailure) return observed;
            current = observed.Value;
        }
        return Result<GameplayEffectObservation>.Success(current);
    }

    private static Result<GameplayEffectObservation> Actor(GameplayEffectObservation observation, string actorId,
        IReadOnlyDictionary<string, EntityComponentState> before, IReadOnlyDictionary<string, EntityComponentState> after, string scopeId)
    {
        if (ReferenceEquals(before, after)) return Result<GameplayEffectObservation>.Success(observation);
        var current = observation;
        foreach (var component in before.Values.OfType<ResourceEntityComponentState>().OrderBy(item => item.ComponentId, StringComparer.Ordinal))
        {
            // Synthetic run wallets are not actor pools and the canonical materializer ignores them.
            if (component.State.OwnerId != actorId) continue;
            if (after.GetValueOrDefault(component.ComponentId) is not ResourceEntityComponentState updated || updated.State.OwnerId != actorId)
                return Result<GameplayEffectObservation>.Failure("Policy materialization changed resource owner/component");
            foreach (var (id, pool) in component.State.Resources.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var next = updated.State.Get(id);
                if (next == null) return Result<GameplayEffectObservation>.Failure("Policy materialization removed a resource");
                if (pool.CurrentCeiling == next.CurrentCeiling && pool.Current == next.Current) continue;
                var metadata = new GameplayOccurrenceDraft { ReasonId = current.ReasonId, Attribution = GameplayAttribution.None,
                    SubjectActorId = actorId, Origin = new() { ContainerKind = "rule", ContainerId = "resource.restrictions",
                        ComponentId = component.ComponentId, ContentRevision = observation.Events.Scope.ContentRevision } };
                var policy = current.Events.Append(current.PublisherId, $"resource.policy/{current.Events.Occurrences.Count}", metadata with
                {
                    Kind = "resource.restriction.resolved", Payload = new ResourceRestrictionPayload { Owner = new() { Kind = "entity", Id = actorId, ScopeId = scopeId },
                        ComponentId = component.ComponentId, ResourceId = id, BeforeCeiling = pool.CurrentCeiling, AfterCeiling = next.CurrentCeiling }
                }, current.ParentEventId);
                if (policy.IsFailure) return Result<GameplayEffectObservation>.Failure(policy.Error);
                var mutation = new ResolvedResourceMutation { MutationId = policy.Value.Occurrence.EventId, ResourceId = id,
                    Operation = ResourceMutationOperation.Set, Value = pool.Current };
                var record = new ResourceMutationRecord(mutation.MutationId, id, mutation.Field, mutation.Operation,
                    PreviousValue: pool.Current, CurrentValue: next.Current, RequestedValue: pool.Current);
                var fact = GameplayResourceFactProducer.FromMutation(mutation, record, new() { Kind = "entity", Id = actorId, ScopeId = scopeId }, metadata);
                if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
                var balance = policy.Value.Transaction.Append(current.PublisherId, $"resource.policy.balance/{policy.Value.Transaction.Occurrences.Count}",
                    fact.Value, policy.Value.Occurrence.EventId);
                if (balance.IsFailure) return Result<GameplayEffectObservation>.Failure(balance.Error);
                current = current with { Events = balance.Value.Transaction };
            }
        }
        return Result<GameplayEffectObservation>.Success(current);
    }
}
