using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Resources;
using Core.Run;

namespace Core.GameplayEvents;

public sealed record ContentRevisionPayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.ContentRevision;
    public string Before { get; init; } = string.Empty;
    public string After { get; init; } = string.Empty;
    public override Result Validate() => EventValue.Valid(Before) && EventValue.Valid(After) && Before != After
        ? Result.Success() : Result.Failure("Content activation requires two distinct captured revisions");
}

public sealed record ResourceRebindPayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.ResourceRebind;
    public GameplayObjectReference Owner { get; init; } = null!;
    public string ResourceId { get; init; } = string.Empty;
    public string? ComponentId { get; init; }
    public string BeforeDefinitionHash { get; init; } = string.Empty;
    public string AfterDefinitionHash { get; init; } = string.Empty;
    public float Before { get; init; }
    public float After { get; init; }
    public override Result Validate() => Owner is { IsValid: true } && EventValue.Valid(ResourceId) && EventValue.Optional(ComponentId) &&
        EventValue.Valid(BeforeDefinitionHash) && EventValue.Valid(AfterDefinitionHash) && float.IsFinite(Before) && float.IsFinite(After)
        ? Result.Success() : Result.Failure("Invalid resource definition rebind fact");
}

/// <summary>Observes a successful, selected activation only. Availability and activation previews never call this producer.</summary>
public static class GameplayContentObservation
{
    public static Result<GameplayEffectObservation> Append(GameplayEffectObservation observation, RunState before, RunState after)
    {
        if (observation?.Events == null || before.RunId != after.RunId || before.SettingId != after.SettingId || before.ConfigName != after.ConfigName ||
            observation.Events.Scope.ContentRevision != before.Determinism.ContentRevision ||
            observation.Events.Scope.SettingId != before.SettingId || observation.Events.Scope.BranchId != before.RunId.ToString("D"))
            return Result<GameplayEffectObservation>.Failure("Content activation observation requires the source aggregate and revision");
        if (before.Determinism.ContentRevision == after.Determinism.ContentRevision)
            return Result<GameplayEffectObservation>.Success(observation);
        var origin = new GameplayEventOrigin { ContainerKind = "rule", ContainerId = "content.activation", ComponentId = "revision",
            ContentRevision = before.Determinism.ContentRevision };
        var changed = observation.Events.Append(observation.PublisherId, $"content.activation/{observation.Events.Occurrences.Count}", new()
        {
            Kind = "content.revision.activated", ReasonId = observation.ReasonId, Attribution = GameplayAttribution.None, Origin = origin,
            Payload = new ContentRevisionPayload { Before = before.Determinism.ContentRevision, After = after.Determinism.ContentRevision }
        }, observation.ParentEventId);
        if (changed.IsFailure) return Result<GameplayEffectObservation>.Failure(changed.Error);
        var current = observation with { Events = changed.Value.Transaction };
        var parent = changed.Value.Occurrence.EventId;
        var wallet = Record(before.ResourceState, after.ResourceState, "run", null);
        if (wallet.IsFailure) return Result<GameplayEffectObservation>.Failure(wallet.Error);
        if (before.PlayerEntity != null)
        {
            foreach (var component in before.PlayerEntity.Components.Values.OfType<ResourceEntityComponentState>().OrderBy(item => item.ComponentId, StringComparer.Ordinal))
            {
                if (after.PlayerEntity?.Components.GetValueOrDefault(component.ComponentId) is not ResourceEntityComponentState updated)
                    return Result<GameplayEffectObservation>.Failure("Content activation lost a persistent resource component");
                var recorded = Record(component.State, updated.State, "entity", component.ComponentId);
                if (recorded.IsFailure) return Result<GameplayEffectObservation>.Failure(recorded.Error);
            }
        }
        return Result<GameplayEffectObservation>.Success(current);

        Result Record(ResourceSet previous, ResourceSet next, string ownerKind, string? componentId)
        {
            if (previous.OwnerId != next.OwnerId || previous.Resources.Count > Core.Effects.EffectExecutionLimits.MaximumSteps ||
                !previous.Resources.Keys.Order(StringComparer.Ordinal).SequenceEqual(next.Resources.Keys.Order(StringComparer.Ordinal)))
                return Result.Failure("Content resource rebind changed its owner/schema or exceeds budget");
            foreach (var (id, old) in previous.Resources.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var updated = next.Resources[id];
                var previousHash = CanonicalJson.ComputeHash(old.Definition);
                var nextHash = CanonicalJson.ComputeHash(updated.Definition);
                if (previousHash == nextHash && old.Current == updated.Current) continue;
                var appended = current.Events.Append(current.PublisherId, $"content.resource/{current.Events.Occurrences.Count}", new()
                {
                    Kind = "resource.definition.rebound", ReasonId = observation.ReasonId, Attribution = GameplayAttribution.None, Origin = origin,
                    SubjectActorId = ownerKind == "entity" ? previous.OwnerId : null,
                    Payload = new ResourceRebindPayload { Owner = new() { Kind = ownerKind, Id = previous.OwnerId, ScopeId = observation.Events.Scope.BranchId },
                        ComponentId = componentId, ResourceId = id, Before = old.Current, After = updated.Current,
                        BeforeDefinitionHash = previousHash, AfterDefinitionHash = nextHash }
                }, parent);
                if (appended.IsFailure) return Result.Failure(appended.Error);
                current = current with { Events = appended.Value.Transaction };
            }
            return Result.Success();
        }
    }
}
