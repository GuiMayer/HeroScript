using System.Collections.Immutable;
using Core.CardZones;
using Core.Effects;
using Core.Run;
using Core.GameplayEvents.Responses;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Combat.TurnPhase;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Math;
using Core.Tests.Combat.Flow;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class CanonicalBoundaryBroadcastTests
{
    [Theory]
    [InlineData("combat.started")]
    [InlineData("round.started")]
    [InlineData("activation.started")]
    public void TimingOwnerBroadcastsOnceAcrossStatusRelicAndPhaseSubflows(string kind)
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture(kind);
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>(); var triggers = EffectTransactionTests.Executor();
        var ally = combat.GetActor("ally")!;
        var watcher = (EntityResponseComponentState)ally.Components["watch"];
        combat = combat.ReplaceActor(ally with { Components = ally.Components.ToImmutableDictionary(StringComparer.Ordinal).SetItem("watch",
            watcher with { Responses = [watcher.Responses!.Value[0] with { Effects = [new() { Type = EffectType.REMOVE_STATUS, Target = EffectTarget.SELF, StatusId = "absent" }] }] }) });
        var zones = new CardZoneSystemDefinition { CardZoneSystemId = "empty", Zones = [new() { ZoneId = "reserve", Ordering = CardZoneOrdering.Ordered, OwnerScope = CardZoneOwnerScope.RunOwner }] };
        var topology = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(zones).Value, new() { RunOwnerId = "$run" }, run.Determinism).Value;
        run = run with { ResolvedMode = new() { CardZoneSystem = zones }, Deck = new() { Topology = topology.State }, Determinism = topology.Context };
        var boundaries = new CombatBoundaryExecutor(new TurnOrderResolver(formulas), new CombatStatusLifecycle(triggers),
            new CombatRelicLifecycle(triggers), new CombatResourceLifecycle(triggers), new PhaseGraphReducer(formulas, triggers),
            new CombatOutcomeResolver(), CombatFlowPlannerTests.ZoneExecutor(), occurrences: triggers);
        var policy = CombatFlowPlannerTests.Policies() with { ResourceCycle = new() { ResourceId = "focus", StartActivation = ResourceRefreshStrategy.Preserve } };
        var input = CanonicalJson.ComputeHash(new { run, combat }); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var result = boundaries.InitializeTransaction(run, combat, CombatFlowPlannerTests.Sequence(), policy, CombatFlowPlannerTests.TurnPolicy());
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(10, result.Value.Combat.GetActor("ally")!.GetResource("focus")!.Current);
            Assert.Single(result.Value.EffectSteps);
            var observed = boundaries.InitializeObserved(run, combat, CombatFlowPlannerTests.Sequence(), policy, CombatFlowPlannerTests.TurnPolicy(),
                GameplayObservedExecutorTests.ObservationFor(run, combat));
            Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(result.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
            Assert.Single(observed.Value.Observation.Events.Occurrences, fact => fact.Kind == kind);
            hashes.Add(CanonicalJson.ComputeHash(result.Value));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(new { run, combat }));
    }

    [Fact]
    public void CompletedCombatDoesNotBroadcastAgain()
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture("combat.ended");
        combat = combat with { Status = CombatStatus.VICTORY };
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>(); var triggers = EffectTransactionTests.Executor();
        var boundaries = new CombatBoundaryExecutor(new TurnOrderResolver(formulas), new CombatStatusLifecycle(triggers),
            new CombatRelicLifecycle(triggers), new CombatResourceLifecycle(triggers), new PhaseGraphReducer(formulas, triggers),
            new CombatOutcomeResolver(), occurrences: triggers);
        var first = boundaries.Complete(run, combat);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(12, first.Value.Combat.GetActor("ally")!.GetResource("focus")!.Current);
        Assert.Single(first.Value.ResponseApplications);
        var second = boundaries.Complete(first.Value.Run!, first.Value.Combat);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(12, second.Value.Combat.GetActor("ally")!.GetResource("focus")!.Current);
        Assert.Empty(second.Value.ResponseApplications);
    }
}
