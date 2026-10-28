using Core.Common;
using Core.Combat.Models;
using Core.Effects;
using Core.Run;

namespace Core.GameplayEvents;

public static class GameplayModifierObservation
{
    public static Result<GameplayEffectObservation> AppendTicks(GameplayEffectObservation observation, RunState before, RunState after)
    {
        var remaining = after.Modifiers.ToDictionary(item => item.InstanceId);
        var current = observation;
        foreach (var previous in before.Modifiers.OrderBy(item => item.InstanceId))
        {
            remaining.TryGetValue(previous.InstanceId, out var updated);
            if (updated != null && updated.Duration == previous.Duration) continue;
            var metadata = new GameplayOccurrenceDraft { ReasonId = current.ReasonId, Attribution = GameplayAttribution.None,
                Origin = new() { ContainerKind = "rule", ContainerId = "modifier.lifecycle", ComponentId = previous.InstanceId.ToString("D"),
                    ContentRevision = after.Determinism.ContentRevision } };
            var fact = updated == null ? GameplayRuleFactProducer.Modifier(new()
            {
                Store = EffectStackStore.Modifier, InstanceId = previous.InstanceId, DefinitionId = previous.ModifierId,
                Owner = previous.Owner, PreviousStacks = previous.Stacks, CurrentStacks = 0, Reason = EffectStackChangeReason.Expire
            }, previous, null, metadata) : GameplayRuleFactProducer.ModifierDuration(previous, updated, metadata);
            if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
            var appended = current.Events.Append(current.PublisherId, $"modifier.lifecycle/{current.Events.Occurrences.Count}", fact.Value, current.ParentEventId);
            if (appended.IsFailure) return Result<GameplayEffectObservation>.Failure(appended.Error);
            current = current with { Events = appended.Value.Transaction };
        }
        return Result<GameplayEffectObservation>.Success(current);
    }
}
