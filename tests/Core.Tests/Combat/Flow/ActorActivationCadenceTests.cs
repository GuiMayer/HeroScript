using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat;
using Core.Combat.Activation;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class ActorActivationCadenceTests
{
    [Fact]
    public void CanonicalBoundariesScheduleAfterExactlyThreeOwnerActivationsAndKeepSnapshotsImmutable()
    {
        var hero = CombatFlowPlannerTests.Entity("a-owner", true, 3);
        var support = CombatFlowPlannerTests.Entity("b-support", true, 3) with { CountsForOutcome = false,
            ControllerBinding = new() { Kind = ControllerKind.AI, PolicyId = "gambit", Cadence = new(hero.InstanceId, 3) } };
        var enemy = CombatFlowPlannerTests.Entity("c-opponent", false, 3);
        var initial = CombatTransitions.Create([hero, support, enemy], DeterministicContext.Create(81, "cadence"));
        var run = new RunState { PlayerEntityId = hero.InstanceId, Determinism = initial.Determinism,
            ResolvedMode = new() { CardZoneSystem = new() { CardZoneSystemId = "cadence", Zones = [new()
                { ZoneId = "pocket", OwnerScope = Core.CardZones.CardZoneOwnerScope.Actor, Ordering = Core.CardZones.CardZoneOrdering.Ordered }] } } };
        var before = CanonicalJson.ComputeHash(initial);
        string? final = null;
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var executor = CombatFlowPlannerTests.Boundaries(new Core.CardZones.CardZoneFlowExecutor());
            var initialized = executor.InitializeTransaction(run, initial, CombatFlowPlannerTests.Sequence(), CombatFlowPlannerTests.Policies(), CombatFlowPlannerTests.TurnPolicy());
            Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
            var state = initialized.Value.Combat;
            var currentRun = initialized.Value.Run;
            var order = new List<string>();
            for (var i = 0; i < 15; i++)
            {
                order.Add(state.ActivationState!.ActiveActorId!);
                var result = executor.AdvanceActivation(currentRun, state, currentRun.Deck, currentRun.Determinism,
                    CombatFlowPlannerTests.Sequence(), CombatFlowPlannerTests.Policies(), CombatFlowPlannerTests.TurnPolicy());
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
                state = result.Value.Combat;
                currentRun = result.Value.Steps[^1].RunSnapshot!;
            }
            Assert.Equal(new[] { "a-owner", "c-opponent", "a-owner", "c-opponent", "a-owner", "b-support", "c-opponent",
                "a-owner", "c-opponent", "a-owner", "c-opponent", "a-owner", "b-support", "c-opponent", "a-owner" }, order);
            Assert.Equal(2, state.ActivationState!.CadenceProgress!.Completed["b-support"]);
            var hash = CanonicalJson.ComputeHash(state);
            if (final != null) Assert.Equal(final, hash);
            final = hash;
            var restored = JsonSerializer.Deserialize<CombatState>(JsonSerializer.Serialize(state))!;
            Assert.Equal(hash, CanonicalJson.ComputeHash(restored));
            Assert.Equal(ActivationSequenceReducer.Next(state, CombatFlowPlannerTests.Policies().ActivationSequence!).Value.ActorId,
                ActivationSequenceReducer.Next(restored, CombatFlowPlannerTests.Policies().ActivationSequence!).Value.ActorId);
        }
        Assert.Equal(before, CanonicalJson.ComputeHash(initial));
        Assert.Null(initial.ActivationState);
    }

    [Fact]
    public void EligibilityStopsWithAnchorAndOutcomeParticipationIsIndependentFromControl()
    {
        var hero = CombatFlowPlannerTests.Entity("hero", true, 3);
        var support = CombatFlowPlannerTests.Entity("support", true, 3) with { CountsForOutcome = false,
            ControllerBinding = new() { Kind = ControllerKind.AI, PolicyId = "gambit", Cadence = new("hero", 3) } };
        var enemy = CombatFlowPlannerTests.Entity("enemy", false, 3);
        var combat = CombatTransitions.Create([hero, support, enemy], DeterministicContext.Create(1, "cadence")) with { ActivationState = new() };
        for (var i = 0; i < 3; i++) combat = ActorActivationCadenceReducer.Complete(combat, "hero");
        Assert.True(ActorActivationCadenceReducer.Eligible(combat, "support"));
        var pools = hero.ResourceState.Resources.ToDictionary(pair => pair.Key, pair => pair.Value);
        pools["health"] = pools["health"] with { Current = 0, Definition = pools["health"].Definition with
            { ThresholdPolicies = [new() { PolicyId = "empty", Comparison = ResourceThresholdComparison.LessThanOrEqual,
                ThresholdSource = ResourceThresholdSource.Minimum, Consequence = ResourceThresholdConsequence.DefeatOwner }] } };
        combat = combat with { Actors = combat.Actors.ToImmutableDictionary().SetItem("hero", hero with { ResourceState = hero.ResourceState with { Resources = pools } }) };
        Assert.False(ActorActivationCadenceReducer.Eligible(combat, "support"));
        Assert.True(combat.GetActor("support")!.IsAlive);
        Assert.Equal(CombatStatus.DEFEAT, new CombatOutcomeResolver().Evaluate(combat, CombatFlowPlannerTests.Policies().Outcome,
            "hero", CombatOutcomeEvaluationPoint.ActionResolution).Status);
    }

    [Fact]
    public void LegacyActorsDoNotAcquireCounterStateAndInvalidAnchorsAreRejected()
    {
        var combat = CombatTransitions.Create([CombatFlowPlannerTests.Entity("a", true, 1), CombatFlowPlannerTests.Entity("b", false, 1)],
            DeterministicContext.Create(1, "cadence")) with { ActivationState = new() };
        Assert.Same(combat, ActorActivationCadenceReducer.Complete(combat, "a"));
        Assert.DoesNotContain("CadenceProgress", JsonSerializer.Serialize(combat));
        Assert.True(ActorActivationCadenceReducer.Validate([("a", new() { Cadence = new("missing", 3) })]).IsFailure);
        Assert.True(ActorActivationCadenceReducer.Validate([("a", new() { Cadence = new("a", 3) })]).IsFailure);
        Assert.True(ActorActivationCadenceReducer.Validate([("a", new() { Cadence = new("b", 0) }), ("b", new())]).IsFailure);
        Assert.True(ActorActivationCadenceReducer.Validate([("a", new() { Cadence = new("b", 3) }), ("b", new() { Cadence = new("a", 3) })]).IsFailure);
    }
}
