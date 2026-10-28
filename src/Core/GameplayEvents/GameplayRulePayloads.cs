using Core.Common;
using Core.Effects;
using Core.Resources;
using Core.Combat.Flow;

namespace Core.GameplayEvents;

public sealed record InstanceDurationPayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.InstanceDuration;
    public string DefinitionId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public Core.Combat.Models.GameplayOwner Owner { get; init; } = null!;
    public int Before { get; init; }
    public int After { get; init; }
    public override Result Validate() => EventValue.Valid(DefinitionId) && EventValue.Valid(InstanceId) &&
        Owner != null && Enum.IsDefined(Owner.Kind) && (Owner.Kind == Core.Combat.Models.GameplayOwnerKind.Global || EventValue.Valid(Owner.Id)) &&
        Before >= -1 && After >= -1 && Before != After
        ? Result.Success() : Result.Failure("Invalid instance duration transition");
}

public sealed record ModifierChangePayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.ModifierChange;
    public string DefinitionId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public Core.Combat.Models.GameplayOwner Owner { get; init; } = null!;
    public EffectStackChangeReason Change { get; init; }
    public int BeforeStacks { get; init; }
    public int AfterStacks { get; init; }
    public int? BeforeDuration { get; init; }
    public int? AfterDuration { get; init; }
    public override Result Validate() => EventValue.Valid(DefinitionId) && EventValue.Valid(InstanceId) && Owner != null && Enum.IsDefined(Owner.Kind) &&
        (Owner.Kind == Core.Combat.Models.GameplayOwnerKind.Global || EventValue.Valid(Owner.Id)) &&
        Enum.IsDefined(Change) && BeforeStacks >= 0 && AfterStacks >= 0 && BeforeDuration is null or >= -1 && AfterDuration is null or >= -1
        ? Result.Success() : Result.Failure("Invalid modifier change payload");
}

/// <summary>A condition matched; the canonical resolver still decides which policy has authority.</summary>
public sealed record ResourceThresholdPayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.ResourceThreshold;
    public ResourceThresholdFact Fact { get; init; } = null!;
    public override Result Validate() => Fact != null && EventValue.Valid(Fact.ResourceId) && EventValue.Valid(Fact.PolicyId) &&
        Enum.IsDefined(Fact.Comparison) && Fact.Comparison != ResourceThresholdComparison.Unspecified &&
        Enum.IsDefined(Fact.ThresholdSource) && Fact.ThresholdSource != ResourceThresholdSource.Unspecified &&
        Enum.IsDefined(Fact.Consequence) && Fact.Consequence != ResourceThresholdConsequence.Unspecified &&
        float.IsFinite(Fact.Value) && float.IsFinite(Fact.Threshold)
        ? Result.Success() : Result.Failure("Invalid canonical threshold fact");
}

public sealed record ActorDefeatChangePayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.ActorDefeatChange;
    public bool BeforeDefeated { get; init; }
    public bool AfterDefeated { get; init; }
    public override Result Validate() => BeforeDefeated != AfterDefeated
        ? Result.Success() : Result.Failure("Actor defeat change requires an actual transition");
}

public sealed record SequenceChangePayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.SequenceChange;
    public SequenceMutation Mutation { get; init; } = null!;
    public CircularSequenceState Before { get; init; } = null!;
    public CircularSequenceState After { get; init; } = null!;
    public override Result Validate() => Mutation != null && Enum.IsDefined(Mutation.Operation) &&
        Mutation.Operation != SequenceOperation.Unspecified && Valid(Before) && Valid(After) && MatchesMutation()
        ? Result.Success() : Result.Failure("Invalid sequence change payload");
    private bool MatchesMutation() => Mutation.Operation switch
    {
        SequenceOperation.ReverseDirection => After == Before with
            { Direction = Before.Direction == SequenceDirection.Forward ? SequenceDirection.Reverse : SequenceDirection.Forward },
        SequenceOperation.SetDirection => Mutation.Direction is SequenceDirection.Forward or SequenceDirection.Reverse &&
            After == Before with { Direction = Mutation.Direction },
        SequenceOperation.AddAdvance => (long)Before.PendingAdvance + Mutation.Amount == After.PendingAdvance &&
            After with { PendingAdvance = Before.PendingAdvance } == Before,
        _ => false
    };
    private static bool Valid(CircularSequenceState? state) => state != null &&
        state.Direction is SequenceDirection.Forward or SequenceDirection.Reverse && state.PendingAdvance is >= 1 and <= 10000 &&
        state.CompletedActivations >= 0 && state.RoundLength >= 0;
}
