using Core.Common;
using Core.Effects;
using Core.Entity;
using Core.StatusEffects;

namespace Core.GameplayEvents;

/// <summary>Typed adapters over already resolved facts. Timing and mutation stay with canonical lifecycles/reducers.</summary>
public static class GameplayEntityFactProducer
{
    public static Result<GameplayOccurrenceDraft> Attribute(AttributeMutationOutcome outcome, GameplayOccurrenceDraft metadata)
    {
        if (outcome == null || metadata == null) return Result<GameplayOccurrenceDraft>.Failure("Attribute outcome and metadata are required");
        var payload = new AttributeChangePayload
        {
            ComponentId = outcome.ComponentId, ValueId = outcome.ValueId, Operation = outcome.Operation, Lifetime = outcome.Lifetime,
            Before = outcome.PreviousValue, After = outcome.CurrentValue
        };
        return Make("attribute.change.resolved", payload, metadata);
    }

    public static Result<GameplayOccurrenceDraft> Status(EffectStackChange change, StatusEffectInstance? before,
        StatusEffectInstance? after, GameplayOccurrenceDraft metadata)
    {
        if (change == null || metadata == null || change.Store != EffectStackStore.Status ||
            change.InstanceId == Guid.Empty || before == null && after == null ||
            before != null && (before.InstanceId != change.InstanceId || before.StatusId != change.DefinitionId || before.Stacks != change.PreviousStacks) ||
            after != null && (after.InstanceId != change.InstanceId || after.StatusId != change.DefinitionId || after.Stacks != change.CurrentStacks) ||
            before == null && change.PreviousStacks != 0 || after == null && change.CurrentStacks != 0 ||
            before != null && after != null && before.TargetId != after.TargetId)
            return Result<GameplayOccurrenceDraft>.Failure("Status fact does not match its before/after snapshots");
        var target = (after ?? before)!.TargetId;
        if (!EventValue.Valid(target) || change.Owner is { } owner &&
            (owner.Kind != Core.Combat.Models.GameplayOwnerKind.Entity || owner.Id != target))
            return Result<GameplayOccurrenceDraft>.Failure("Status fact owner does not match its target");
        var kind = change.Reason switch
        {
            EffectStackChangeReason.Apply => "status.applied",
            EffectStackChangeReason.Reapply => "status.reapplied",
            EffectStackChangeReason.Remove or EffectStackChangeReason.Dispel => "status.removed",
            EffectStackChangeReason.Consume => "status.stacks.consumed",
            EffectStackChangeReason.Expire => "status.expired",
            _ => null
        };
        if (kind == null || change.Reason == EffectStackChangeReason.Apply && before != null ||
            change.Reason == EffectStackChangeReason.Reapply && (before == null || after == null))
            return Result<GameplayOccurrenceDraft>.Failure("Status fact has an unsupported or inconsistent transition reason");
        var payload = new StatusChangePayload
        {
            DefinitionId = change.DefinitionId, InstanceId = change.InstanceId.ToString("D"),
            BeforeStacks = change.PreviousStacks, AfterStacks = change.CurrentStacks,
            BeforeDuration = before?.Duration, AfterDuration = after?.Duration
        };
        return Make(kind, payload, metadata with { SubjectActorId = target });
    }

    public static Result<GameplayOccurrenceDraft> StatusDuration(StatusEffectInstance before, StatusEffectInstance after,
        GameplayOccurrenceDraft metadata)
    {
        if (before == null || after == null || metadata == null || before.InstanceId == Guid.Empty ||
            before.InstanceId != after.InstanceId || before.StatusId != after.StatusId || before.TargetId != after.TargetId ||
            before.Stacks != after.Stacks || before.Duration == after.Duration || !EventValue.Valid(after.TargetId))
            return Result<GameplayOccurrenceDraft>.Failure("Duration fact requires an actual transition of the same status instance");
        return Make("status.duration.changed", new StatusChangePayload
        {
            DefinitionId = after.StatusId, InstanceId = after.InstanceId.ToString("D"), BeforeStacks = before.Stacks,
            AfterStacks = after.Stacks, BeforeDuration = before.Duration, AfterDuration = after.Duration
        }, metadata with { SubjectActorId = after.TargetId });
    }

    public static Result<GameplayOccurrenceDraft> Boundary(GameplayBoundary boundary, string boundaryId, long sequence,
        GameplayOccurrenceDraft metadata)
    {
        if (metadata == null) return Result<GameplayOccurrenceDraft>.Failure("Boundary metadata is required");
        var kind = boundary switch
        {
            GameplayBoundary.CombatStarted => "combat.started", GameplayBoundary.CombatEnded => "combat.ended",
            GameplayBoundary.ActivationStarted => "activation.started", GameplayBoundary.ActivationEnded => "activation.ended",
            GameplayBoundary.RoundStarted => "round.started", GameplayBoundary.RoundEnded => "round.ended",
            GameplayBoundary.PhaseEntered => "phase.entered", GameplayBoundary.PhaseExited => "phase.exited",
            GameplayBoundary.RunStarted => "run.started", GameplayBoundary.RunEnded => "run.ended", _ => null
        };
        return kind == null ? Result<GameplayOccurrenceDraft>.Failure("Unsupported gameplay boundary")
            : Make(kind, new BoundaryOccurrencePayload { BoundaryId = boundaryId, Sequence = sequence }, metadata);
    }

    private static Result<GameplayOccurrenceDraft> Make(string kind, GameplayOccurrencePayload payload, GameplayOccurrenceDraft metadata)
    {
        var valid = payload.Validate();
        return valid.IsFailure ? Result<GameplayOccurrenceDraft>.Failure(valid.Error)
            : Result<GameplayOccurrenceDraft>.Success(metadata with { Kind = kind, Payload = payload });
    }
}

public enum GameplayBoundary
{
    Unspecified, CombatStarted, CombatEnded, ActivationStarted, ActivationEnded,
    RoundStarted, RoundEnded, PhaseEntered, PhaseExited, RunStarted, RunEnded
}
