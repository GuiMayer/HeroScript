using System.Collections.Immutable;
using Core.Combat.Flow;
using Core.Combat.TurnPhase;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.Math;
using Core.StatusEffects;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class PostFactLifecycleResponseTests
{
    [Theory]
    [InlineData(1, "status.expired")]
    [InlineData(2, "status.duration.changed")]
    public void StatusTimingProducesAnActualFactAndExecutesItsResponseInNormalAndObservedPaths(int duration, string kind)
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture(kind);
        var status = new StatusEffectInstance { InstanceId = Guid.Parse("b0c9d462-64b5-4b38-b4f8-f1843be2a4a1"), StatusId = "test",
            TargetId = "enemy", SourceId = "hero", ContentRevision = "revision", Stacks = 1, Duration = duration,
            Definition = new() { StatusId = "test", DurationTickBoundary = StatusTriggerBoundary.EndActivation } };
        combat = combat with { StatusEffects = combat.StatusEffects.Add("enemy", [status]) };
        var original = CanonicalJson.ComputeHash(combat);
        var lifecycle = new CombatStatusLifecycle(EffectTransactionTests.Executor());
        var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var normal = lifecycle.Process(run, combat, StatusTriggerBoundary.EndActivation, "enemy");
            Assert.True(normal.IsSuccess, normal.IsFailure ? normal.Error : null);
            Assert.Equal(12, normal.Value.Combat.GetActor("ally")!.GetResource("focus")!.Current);
            Assert.Single(normal.Value.ResponseSteps); Assert.Single(normal.Value.ResponseApplications);
            var observed = lifecycle.ProcessObserved(run, combat, StatusTriggerBoundary.EndActivation,
                GameplayObservedExecutorTests.ObservationFor(run, combat), "enemy");
            Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(normal.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
            Assert.Equal(kind, observed.Value.Observation.Events.Occurrences[0].Kind);
            Assert.Equal(observed.Value.Observation.Events.Occurrences[0].EventId,
                observed.Value.Observation.Events.Occurrences[1].ParentEventId);
            hashes.Add(CanonicalJson.ComputeHash(normal.Value));
        }
        Assert.Single(hashes); Assert.Equal(original, CanonicalJson.ComputeHash(combat));
    }

    [Fact]
    public void EmptyPhaseStillBroadcastsItsSingleCanonicalBoundary()
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture("phase.entered");
        combat = combat with { ActivationState = new() { ActiveActorId = "hero" } };
        var graph = new PhaseSequenceDefinition { SequenceId = "graph", EntryPhaseId = "start", Phases = [
            new() { PhaseId = "start", Role = PhaseRole.Start, Edges = [new() { EdgeId = "middle", TargetPhaseId = "main", Trigger = PhaseEdgeTrigger.Automatic }] },
            new() { PhaseId = "main", Order = 1, Role = PhaseRole.Middle, Edges = [new() { EdgeId = "end", TargetPhaseId = "end", Trigger = PhaseEdgeTrigger.ActivationExit }] },
            new() { PhaseId = "end", Order = 2, Role = PhaseRole.End }] };
        var reducer = new PhaseGraphReducer(Mock.Of<IRuntimeFormulaEvaluator>(), EffectTransactionTests.Executor());
        var normal = reducer.Enter(run, combat, graph);
        Assert.True(normal.IsSuccess, normal.IsFailure ? normal.Error : null);
        Assert.Equal(12, normal.Value.Combat.GetActor("ally")!.GetResource("focus")!.Current);
        var observed = reducer.EnterObserved(run, combat, graph, GameplayObservedExecutorTests.ObservationFor(run, combat));
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(normal.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
        Assert.Equal(2, observed.Value.Observation.Events.Occurrences.Count(fact => fact.Kind == "phase.entered"));
        Assert.Single(observed.Value.Execution.Applications);
    }
}
