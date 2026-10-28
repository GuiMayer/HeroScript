using Core.Common;
using Core.Run;

namespace Core.GameplayEvents;

/// <summary>A lifecycle copy/reset, not a damage/heal/cost or a second resource reducer operation.</summary>
public sealed record ResourceTransportPayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.ResourceTransport;
    public string ResourceId { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public bool EnteringEncounter { get; init; }
    public ActorResourceLifecycleAction Policy { get; init; }
    public float Before { get; init; }
    public float RequestedCurrent { get; init; }
    public float After { get; init; }
    public float BeforeMinimum { get; init; }
    public float AfterMinimum { get; init; }
    public float BeforeMaximum { get; init; }
    public float AfterMaximum { get; init; }
    public override Result Validate() => EventValue.Valid(ResourceId) && EventValue.Valid(ActorId) &&
        Policy is ActorResourceLifecycleAction.PreserveCurrent or ActorResourceLifecycleAction.ResetToMaximum or ActorResourceLifecycleAction.ResetToConfiguredValue &&
        new[] { Before, RequestedCurrent, After, BeforeMinimum, AfterMinimum, BeforeMaximum, AfterMaximum }.All(float.IsFinite) &&
        BeforeMinimum <= BeforeMaximum && AfterMinimum <= AfterMaximum
        ? Result.Success() : Result.Failure("Invalid resource lifecycle transport payload");
}
