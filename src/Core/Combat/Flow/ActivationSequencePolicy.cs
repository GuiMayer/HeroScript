using System.Text.Json.Serialization;
using Core.Common;

namespace Core.Combat.Flow;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivationSequenceStrategy { Unspecified, PerRound, Circular }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SequenceDirection { Unspecified, Forward, Reverse }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SequenceRoundStrategy { Unspecified, Disabled, ActivationCount }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SequenceFirstActor { Unspecified, FirstInOrder, RunOwner }

public sealed record ActivationSequencePolicy
{
    public ActivationSequenceStrategy Strategy { get; init; }
    public CircularSequencePolicy? Circular { get; init; }

    public static Result Validate(ActivationSequencePolicy? policy)
    {
        if (policy == null || policy.Strategy == ActivationSequenceStrategy.Unspecified || !Enum.IsDefined(policy.Strategy))
            return Result.Failure("activationSequence.strategy is required (PerRound or Circular); migrate source content explicitly");
        if (policy.Strategy == ActivationSequenceStrategy.PerRound)
            return policy.Circular == null ? Result.Success() : Result.Failure("PerRound cannot define circular options");
        var circular = policy.Circular;
        if (circular == null || circular.InitialDirection == SequenceDirection.Unspecified || !Enum.IsDefined(circular.InitialDirection) ||
            circular.FirstActor == SequenceFirstActor.Unspecified || !Enum.IsDefined(circular.FirstActor) ||
            circular.RoundPolicy == SequenceRoundStrategy.Unspecified || !Enum.IsDefined(circular.RoundPolicy))
            return Result.Failure("Circular requires initialDirection, firstActor and roundPolicy");
        if (circular.ActivationsPerRound is <= 0 || circular.RoundPolicy == SequenceRoundStrategy.Disabled && circular.ActivationsPerRound != null)
            return Result.Failure("Invalid circular activationsPerRound");
        return Result.Success();
    }
}

public sealed record CircularSequencePolicy
{
    public SequenceDirection InitialDirection { get; init; }
    public SequenceFirstActor FirstActor { get; init; }
    public SequenceRoundStrategy RoundPolicy { get; init; }
    public int? ActivationsPerRound { get; init; }
    public bool AllowSelfSuccessor { get; init; }
}
