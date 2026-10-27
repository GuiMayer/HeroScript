using Core.Combat.Activation;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class ActivationSequenceReducerTests
{
    internal static ActivationSequencePolicy Policy() => new() { Strategy = ActivationSequenceStrategy.Circular,
        Circular = new() { InitialDirection = SequenceDirection.Forward, FirstActor = SequenceFirstActor.FirstInOrder,
            RoundPolicy = SequenceRoundStrategy.Disabled, AllowSelfSuccessor = true } };
    internal static CombatState State() => new() {
        Actors = new[] { "A", "B", "C", "D" }.ToDictionary(id => id, id => new CombatActorState { InstanceId = id }),
        TurnOrderState = new() { Order = ["A", "B", "C", "D"] },
        ActivationState = new() { ActiveActorId = "B", ActivationOrder = ["A", "B", "C", "D"], CompletedActorIds = ["A", "B"],
            Circular = new() { Direction = SequenceDirection.Forward, RoundLength = 4 } }
    };
    [Theory]
    [InlineData(SequenceDirection.Forward, 1, "C")]
    [InlineData(SequenceDirection.Reverse, 1, "A")]
    [InlineData(SequenceDirection.Forward, 2, "D")]
    [InlineData(SequenceDirection.Reverse, 2, "D")]
    public void CircularUsesTheCurrentAnchorEvenWhenTheSuccessorHasAlreadyActed(SequenceDirection direction, int advance, string expected)
    {
        var state = State(); state = state with { ActivationState = state.ActivationState! with
            { Circular = state.ActivationState.Circular! with { Direction = direction, PendingAdvance = advance } } };
        var before = CanonicalJson.ComputeHash(state);
        var result = ActivationSequenceReducer.Next(state, Policy()).Value;
        Assert.Equal(expected, result.ActorId); Assert.Equal(1, result.Circular!.PendingAdvance);
        Assert.Equal(1, result.Round); Assert.Equal(before, CanonicalJson.ComputeHash(state));
    }
    [Fact]
    public void PerRoundStillExcludesCompletedParticipants()
    {
        var state = State();
        Assert.Equal("C", ActivationSequenceReducer.Next(state, new() { Strategy = ActivationSequenceStrategy.PerRound }).Value.ActorId);
        Assert.False(ActivationSequenceReducer.StartsNewRound(state, Policy()));
    }
}
