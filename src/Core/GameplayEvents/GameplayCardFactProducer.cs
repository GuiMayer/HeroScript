using System.Collections.Immutable;
using Core.CardZones;
using Core.Common;
using Core.Determinism;

namespace Core.GameplayEvents;

/// <summary>Projects primitive topology facts. Address references come from resolved zones, never string splitting.</summary>
public static class GameplayCardFactProducer
{
    public static Result<ImmutableArray<GameplayOccurrenceDraft>> FromPrimitiveStep(CardZoneFlowStepRecord step,
        GameplayZoneReference? source, GameplayZoneReference? target, GameplayOccurrenceDraft metadata)
    {
        Result<ImmutableArray<GameplayOccurrenceDraft>> Fail(string message) => Result<ImmutableArray<GameplayOccurrenceDraft>>.Failure(message);
        if (step == null || metadata == null || !Enum.IsDefined(step.Operation) || !EventValue.Valid(step.FlowId) ||
            !EventValue.Valid(step.StepId) || !EventValue.Valid(step.PreviousStateHash) || !EventValue.Valid(step.StateHash) ||
            step.RandomDrawEnd < step.RandomDrawStart || step.InstanceIds.IsDefault || step.CreatedInstanceIds.IsDefault || step.DestroyedInstanceIds.IsDefault)
            return Fail("Invalid canonical card-zone primitive record");
        if (!AddressMatches(step.SourceAddress, source) || !AddressMatches(step.TargetAddress, target))
            return Fail("Resolved card-zone references do not match recorded addresses");
        // Selection/random bookkeeping and flow-control records are not another movement of the selected cards.
        if (step.PreviousStateHash == step.StateHash || step.Operation is CardZoneOperation.ExecuteFlow or CardZoneOperation.ForEachParticipant)
            return Result<ImmutableArray<GameplayOccurrenceDraft>>.Success([]);
        var group = CanonicalJson.ComputeHash(new { step.FlowId, step.StepId, step.PreviousStateHash, step.StateHash });
        if (step.Operation is CardZoneOperation.Shuffle or CardZoneOperation.Reorder)
        {
            var zone = source ?? target;
            if (zone == null) return Fail("Zone order transition requires an explicit resolved zone");
            var before = source != null ? step.SourceOrderBefore : step.TargetOrderBefore;
            var after = source != null ? step.SourceOrderAfter : step.TargetOrderAfter;
            if (before.IsDefault || after.IsDefault) return Fail("Order transition requires before/after vectors");
            var payload = new ZoneOrderPayload { Zone = zone, MovementGroupId = group,
                Before = before.Select(id => id.ToString("D")).ToImmutableArray(), After = after.Select(id => id.ToString("D")).ToImmutableArray() };
            var valid = payload.Validate();
            return valid.IsFailure ? Fail(valid.Error) : Result<ImmutableArray<GameplayOccurrenceDraft>>.Success(
                [metadata with { Kind = "zone.order.changed", Payload = payload }]);
        }
        var kind = step.Operation switch
        {
            CardZoneOperation.Create => "card.created", CardZoneOperation.Move => "card.moved",
            CardZoneOperation.Destroy => "card.destroyed", _ => null
        };
        if (kind == null) return Fail("Unsupported card-zone primitive");
        var ids = step.Operation == CardZoneOperation.Create ? step.CreatedInstanceIds :
            step.Operation == CardZoneOperation.Destroy ? step.DestroyedInstanceIds : step.InstanceIds;
        if (ids.Length > 4096 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length ||
            step.Operation == CardZoneOperation.Create && target == null ||
            step.Operation == CardZoneOperation.Destroy && source == null ||
            step.Operation == CardZoneOperation.Move && (source == null || target == null))
            return Fail("Invalid card identities or missing source/target zone");
        var result = ImmutableArray.CreateBuilder<GameplayOccurrenceDraft>();
        foreach (var id in ids)
        {
            var payload = new CardMovementPayload { CardInstanceId = id.ToString("D"), MovementGroupId = group,
                From = step.Operation == CardZoneOperation.Create ? null : source,
                To = step.Operation == CardZoneOperation.Destroy ? null : target };
            var valid = payload.Validate();
            if (valid.IsFailure) return Fail(valid.Error);
            result.Add(metadata with { Kind = kind, Payload = payload });
        }
        return Result<ImmutableArray<GameplayOccurrenceDraft>>.Success(result.ToImmutable());
    }

    private static bool AddressMatches(string? address, GameplayZoneReference? reference) => address == null
        ? reference == null : reference is { IsValid: true } &&
            StringComparer.Ordinal.Equals(address, CardZoneAddress.CreateKey(reference.ZoneId, reference.OwnerId, reference.ScopeId));
}
