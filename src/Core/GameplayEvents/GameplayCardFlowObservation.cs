using Core.Common;
using Core.CardZones;
using Core.Effects;
using Core.Run;
using System.Collections.Immutable;

namespace Core.GameplayEvents;

public static class GameplayCardFlowObservation
{
    public static Result<GameplayEffectObservation> Append(GameplayEffectObservation observation,
        IReadOnlyList<CardZoneFlowStepRecord> steps, RunState before, RunState after, string? actorId = null)
    {
        if (observation?.Events == null || steps == null || before == null || after == null)
            return Result<GameplayEffectObservation>.Failure("Invalid observed card flow or step budget");
        var inputs = steps.Take(EffectExecutionLimits.MaximumSteps + 1).ToImmutableArray();
        if (inputs.Length > EffectExecutionLimits.MaximumSteps || inputs.Any(step => step == null))
            return Result<GameplayEffectObservation>.Failure("Card flow observation exceeds its input budget");
        var current = observation;
        for (var index = 0; index < inputs.Length; index++)
        {
            var step = inputs[index];
            var source = Resolve(step.SourceAddress);
            var target = Resolve(step.TargetAddress);
            if (source.IsFailure || target.IsFailure)
                return Result<GameplayEffectObservation>.Failure(source.IsFailure ? source.Error : target.Error);
            var drafts = GameplayCardFactProducer.FromPrimitiveStep(step, source.Value, target.Value, new()
            {
                ReasonId = observation.ReasonId, Attribution = GameplayAttribution.None, SubjectActorId = actorId,
                Origin = new() { ContainerKind = "rule", ContainerId = step.FlowId, ComponentId = step.StepId,
                    ContentRevision = after.Determinism.ContentRevision }
            });
            if (drafts.IsFailure) return Result<GameplayEffectObservation>.Failure(drafts.Error);
            for (var factIndex = 0; factIndex < drafts.Value.Length; factIndex++)
            {
                var appended = current.Events.Append(current.PublisherId, $"flow/{current.Events.Occurrences.Count}/{index}/{factIndex}",
                    drafts.Value[factIndex], current.ParentEventId);
                if (appended.IsFailure) return Result<GameplayEffectObservation>.Failure(appended.Error);
                current = current with { Events = appended.Value.Transaction };
            }
        }
        return Result<GameplayEffectObservation>.Success(current);

        Result<GameplayZoneReference?> Resolve(string? address)
        {
            if (address == null) return Result<GameplayZoneReference?>.Success(null);
            var zone = after.Deck.Topology.Zones.GetValueOrDefault(address) ?? before.Deck.Topology.Zones.GetValueOrDefault(address);
            var definition = after.ResolvedMode?.CardZoneSystem?.Zones.SingleOrDefault(item => item.ZoneId == zone?.Address.ZoneId)
                ?? before.ResolvedMode?.CardZoneSystem?.Zones.SingleOrDefault(item => item.ZoneId == zone?.Address.ZoneId);
            return zone == null || definition == null ? Result<GameplayZoneReference?>.Failure("Card flow observation requires its captured topology and definition")
                : Result<GameplayZoneReference?>.Success(new() { ZoneId = zone.Address.ZoneId, OwnerId = zone.Address.OwnerId,
                    ScopeId = zone.Address.ScopeId, OwnerScope = definition.OwnerScope });
        }
    }
}
