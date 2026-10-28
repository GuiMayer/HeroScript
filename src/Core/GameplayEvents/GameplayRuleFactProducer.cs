using System.Collections.Immutable;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Effects;

namespace Core.GameplayEvents;

public static class GameplayRuleFactProducer
{
    public static Result<GameplayOccurrenceDraft> Modifier(EffectStackChange change, ScriptModifierInstance? before,
        ScriptModifierInstance? after, GameplayOccurrenceDraft metadata)
    {
        if (change == null || metadata == null || change.Store != EffectStackStore.Modifier || change.Owner == null ||
            !Enum.IsDefined(change.Owner.Kind) || change.InstanceId == Guid.Empty || before == null && after == null ||
            before != null && (before.InstanceId != change.InstanceId || before.ModifierId != change.DefinitionId || before.Stacks != change.PreviousStacks || before.Owner != change.Owner) ||
            after != null && (after.InstanceId != change.InstanceId || after.ModifierId != change.DefinitionId || after.Stacks != change.CurrentStacks || after.Owner != change.Owner) ||
            before == null && change.PreviousStacks != 0 || after == null && change.CurrentStacks != 0)
            return Result<GameplayOccurrenceDraft>.Failure("Modifier change does not match its canonical snapshots");
        var payload = new ModifierChangePayload
        {
            DefinitionId = change.DefinitionId, InstanceId = change.InstanceId.ToString("D"),
            Owner = change.Owner,
            Change = change.Reason, BeforeStacks = change.PreviousStacks, AfterStacks = change.CurrentStacks,
            BeforeDuration = before?.Duration, AfterDuration = after?.Duration
        };
        var valid = payload.Validate();
        return valid.IsFailure ? Result<GameplayOccurrenceDraft>.Failure(valid.Error)
            : Result<GameplayOccurrenceDraft>.Success(metadata with { Kind = "modifier.stacks.changed", Payload = payload,
                SubjectActorId = change.Owner.Kind == Core.Combat.Models.GameplayOwnerKind.Entity ? change.Owner.Id : null });
    }

    public static Result<ImmutableArray<GameplayOccurrenceDraft>> ResourceRules(EffectResourceOutcome outcome, GameplayOccurrenceDraft metadata)
    {
        if (outcome == null || metadata == null || outcome.ThresholdFacts.IsDefault || outcome.ThresholdFacts.Length > 4096)
            return Result<ImmutableArray<GameplayOccurrenceDraft>>.Failure("Invalid canonical resource rules outcome");
        var facts = ImmutableArray.CreateBuilder<GameplayOccurrenceDraft>();
        foreach (var threshold in outcome.ThresholdFacts)
        {
            var payload = new ResourceThresholdPayload { Fact = threshold };
            var valid = payload.Validate();
            if (valid.IsFailure) return Result<ImmutableArray<GameplayOccurrenceDraft>>.Failure(valid.Error);
            facts.Add(metadata with { Kind = "resource.threshold.matched", Payload = payload });
        }
        if (outcome.OwnerDefeatedBefore != outcome.OwnerDefeatedAfter)
            facts.Add(metadata with { Kind = "actor.defeat.changed", Payload = new ActorDefeatChangePayload
            { BeforeDefeated = outcome.OwnerDefeatedBefore, AfterDefeated = outcome.OwnerDefeatedAfter } });
        return Result<ImmutableArray<GameplayOccurrenceDraft>>.Success(facts.ToImmutable());
    }
}
