using Core.Combat.Activation;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class ActivationSequenceReducerTests
{
    [Theory]
    [InlineData(Core.Effects.EffectProvenanceKind.Card)]
    [InlineData(Core.Effects.EffectProvenanceKind.Status)]
    [InlineData(Core.Effects.EffectProvenanceKind.Ability)]
    public void SequenceEffectsHaveIdenticalSemanticsRegardlessOfProvenance(Core.Effects.EffectProvenanceKind kind)
    {
        var state = State(); var before = CanonicalJson.ComputeHash(state);
        var result = new Core.Effects.ImmutableEffectProcessor().Apply(state, [new() {
            EffectInstanceId = "reverse", SourceEntityId = "B", TargetEntityIds = ["B"], Provenance = new() { Kind = kind },
            Definition = new() { Type = Core.Effects.EffectType.TURN_SEQUENCE, Target = Core.Effects.EffectTarget.SELF,
                SequenceMutation = new() { Operation = SequenceOperation.ReverseDirection } } }]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("A", RelativeParticipantSelector.Resolve(result.Value.State, "B").Value);
        Assert.Equal("A", ActivationSequenceReducer.Next(result.Value.State, Policy()).Value.ActorId);
        Assert.Equal(before, CanonicalJson.ComputeHash(state));
    }
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
    [Fact]
    public void RoundCountingUsesRealActivationsRatherThanSkippedSeats()
    {
        var state = State(); var policy = Policy();
        policy = policy with { Circular = policy.Circular! with { RoundPolicy = SequenceRoundStrategy.ActivationCount, ActivationsPerRound = 2 } };
        state = state with { ActivationState = state.ActivationState! with { Circular = state.ActivationState.Circular! with
            { RoundLength = 2, CompletedActivations = 1, PendingAdvance = 7 } } };
        Assert.True(ActivationSequenceReducer.StartsNewRound(state, policy));
        var next = ActivationSequenceReducer.Next(state, policy).Value;
        Assert.Equal(2, next.Round); Assert.Equal(0, next.Circular!.CompletedActivations);
    }
    [Fact]
    public void RelativeOffsetUsesTheSameDirectionAsTraversalWithoutConsumingThePendingAdvance()
    {
        var state = State(); var before = CanonicalJson.ComputeHash(state);
        Assert.Equal("D", RelativeParticipantSelector.Resolve(state, "B", 2).Value);
        Assert.Equal("A", RelativeParticipantSelector.Resolve(state, "B", -1).Value);
        Assert.Equal(before, CanonicalJson.ComputeHash(state));
    }
}
