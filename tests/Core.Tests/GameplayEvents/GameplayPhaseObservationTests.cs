using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.Math;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayPhaseObservationTests
{
    private static PhaseSequenceDefinition Graph(bool effects) => new()
    {
        SequenceId = "graph", EntryPhaseId = "begin", Phases = [
            new() { PhaseId = "begin", Role = PhaseRole.Start, Order = 0, EntryEffects = effects ? [Gain()] : [],
                Edges = [new() { EdgeId = "to-main", TargetPhaseId = "main", Trigger = PhaseEdgeTrigger.Automatic }] },
            new() { PhaseId = "main", Role = PhaseRole.Middle, Order = 1, AllowedActions = [ActionType.PASS, ActionType.END_TURN],
                ExitEffects = effects ? [Gain()] : [], Edges = [
                    new() { EdgeId = "end-activation", TargetPhaseId = "finish", Trigger = PhaseEdgeTrigger.ActivationExit },
                    new() { EdgeId = "pass", TargetPhaseId = "finish", Trigger = PhaseEdgeTrigger.Command, ActionTypes = [ActionType.PASS] }] },
            new() { PhaseId = "finish", Role = PhaseRole.End, Order = 2 } ]
    };
    private static EffectDefinition Gain() => EffectSequenceBudgetTests.Damage(1, 1) with
        { Type = EffectType.MODIFY_RESOURCE, Operation = ResourceEffectOperation.ADD, Target = EffectTarget.SELF };
    private static PhaseGraphReducer Reducer(EffectTriggerExecutor effects) => new(Mock.Of<IRuntimeFormulaEvaluator>(), effects);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualPhaseBoundariesAreRecordedOnceEvenWithoutEffectsAndOwnTheirEffectFacts(bool effects)
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var reducer = Reducer(fixture.Executor);
        var request = EffectTransactionTests.Request();
        var combat = request.Combat with { ActivationState = new() { ActiveActorId = "hero" } };
        var graph = Graph(effects);
        var hash = CanonicalJson.ComputeHash(combat);
        var observation = GameplayObservedExecutorTests.ObservationFor(fixture.Run, combat);
        var expected = reducer.Enter(fixture.Run, combat, graph);
        Assert.True(expected.IsSuccess, expected.IsFailure ? expected.Error : null);
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var actual = reducer.EnterObserved(fixture.Run, combat, graph, observation);
            Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(expected.Value), CanonicalJson.ComputeHash(actual.Value.Execution));
            var facts = actual.Value.Observation.Events.Occurrences;
            Assert.Equal(new[] { "phase.entered:begin", "phase.exited:begin", "phase.entered:main" },
                facts.Where(item => item.Payload is BoundaryOccurrencePayload).Select(item => item.Kind + ":" + ((BoundaryOccurrencePayload)item.Payload).BoundaryId));
            if (effects)
            {
                var resource = Assert.Single(facts, item => item.Payload is ResourceChangePayload);
                Assert.Equal(facts[0].EventId, resource.ParentEventId);
            }
            Assert.Null(actual.Value.Observation.ParentEventId);
            Assert.Empty(actual.Value.Observation.Unobserved);
            fingerprints.Add(CanonicalJson.ComputeHash(facts));
        }
        Assert.Single(fingerprints);
        Assert.Equal(hash, CanonicalJson.ComputeHash(combat));
        Assert.Empty(observation.Events.Occurrences);
    }

    [Fact]
    public void ExitAndCommandUseTheSameGraphAndAutomaticNoopDoesNotEmitAnotherBoundary()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var reducer = Reducer(fixture.Executor);
        var combat = EffectTransactionTests.Request().Combat with { ActivationState = new() { ActiveActorId = "hero" } };
        var graph = Graph(true);
        var entered = reducer.Enter(fixture.Run, combat, graph).Value;
        var observation = GameplayObservedExecutorTests.ObservationFor(fixture.Run, combat);
        var exit = reducer.ExitObserved(entered.Run, entered.Combat, graph, observation);
        Assert.True(exit.IsSuccess, exit.IsFailure ? exit.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(reducer.Exit(entered.Run, entered.Combat, graph).Value), CanonicalJson.ComputeHash(exit.Value.Execution));
        var command = new CombatActionCommand { ActorId = "hero", ActionType = ActionType.PASS };
        var handled = reducer.HandleCommandObserved(entered.Run, entered.Combat, graph, command, new HashSet<string>(), observation);
        Assert.True(handled.IsSuccess, handled.IsFailure ? handled.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(reducer.HandleCommand(entered.Run, entered.Combat, graph, command, new HashSet<string>()).Value),
            CanonicalJson.ComputeHash(handled.Value.Execution));
        Assert.Equal(new[] { "phase.exited", "resource.change.resolved", "phase.entered" }, exit.Value.Observation.Events.Occurrences.Select(item => item.Kind));
        var noOp = reducer.AdvanceAutomaticObserved(entered.Run, entered.Combat, graph, observation);
        Assert.True(noOp.IsSuccess, noOp.IsFailure ? noOp.Error : null);
        Assert.Empty(noOp.Value.Observation.Events.Occurrences);
    }

    [Fact]
    public void ObservationExhaustionAndGraphCycleReturnNoPartialCandidate()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var reducer = Reducer(fixture.Executor);
        var combat = EffectTransactionTests.Request().Combat with { ActivationState = new() { ActiveActorId = "hero" } };
        var observation = GameplayObservedExecutorTests.ObservationFor(fixture.Run, combat, 1);
        var hash = CanonicalJson.ComputeHash(combat);
        Assert.True(reducer.EnterObserved(fixture.Run, combat, Graph(true), observation).IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(combat));
        Assert.Empty(observation.Events.Occurrences);
        var graph = Graph(false);
        graph = graph with { MaxAutomaticTransitions = 4, Phases = graph.Phases.Select(phase => phase.PhaseId != "main" ? phase
            : phase with { Edges = phase.Edges.Append(new PhaseEdgeDefinition { EdgeId = "loop", TargetPhaseId = "main", Trigger = PhaseEdgeTrigger.Automatic }).ToArray() }).ToArray() };
        var unboundedInput = GameplayObservedExecutorTests.ObservationFor(fixture.Run, combat);
        Assert.True(reducer.Enter(fixture.Run, combat, graph).IsFailure);
        Assert.True(reducer.EnterObserved(fixture.Run, combat, graph, unboundedInput).IsFailure);
        Assert.Empty(unboundedInput.Events.Occurrences);
        Assert.Equal(hash, CanonicalJson.ComputeHash(combat));
    }
}
