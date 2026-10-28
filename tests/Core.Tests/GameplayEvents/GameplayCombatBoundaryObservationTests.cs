using Core.CardZones;
using Core.Effects;
using Core.Math;
using Core.Combat.TurnOrder;
using Core.Combat.Modifiers;
using Core.Combat.TurnPhase;
using Core.Tests.Effects;
using Moq;
using Core.Resources;
using Core.StatusEffects;
using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Run;
using Core.Tests.Combat.Flow;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayCombatBoundaryObservationTests
{
    private static (RunState Run, CombatState Combat, CombatBoundaryExecutor Executor) Fixture()
    {
        var zones = CombatFlowPlannerTests.LifecycleZones();
        var seeded = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(zones).Value,
            new CardZoneBootstrapPlan { RunOwnerId = "$run", Batches = [
                new() { ZoneId = "draw", OwnerId = "$run", DefinitionIds = ["future"] },
                new() { ZoneId = "hand", OwnerId = "$run", DefinitionIds = ["strike", "retain", "ethereal"] }] },
            DeterministicContext.Create(99, "revision")).Value;
        var run = new RunState { RunId = Guid.Parse("10000000-0000-0000-0000-000000000001"), PlayerEntityId = "hero",
            Determinism = seeded.Context, Deck = new() { Topology = seeded.State }, ResolvedMode = new() { CardZoneSystem = zones } };
        var combat = CombatTransitions.Create([CombatFlowPlannerTests.Entity("hero", true, 0), CombatFlowPlannerTests.Entity("enemy", false, 0)],
            DeterministicContext.Create(42, "revision"));
        return (run, combat, CombatFlowPlannerTests.Boundaries(CombatFlowPlannerTests.ZoneExecutor()));
    }

    [Fact]
    public void InitializeAndTwoActorActivationsKeepTheWholeCanonicalTraceAndConfiguredCardFlows()
    {
        var (run, combat, executor) = Fixture();
        var sequence = CombatFlowPlannerTests.Sequence(); var policies = CombatFlowPlannerTests.Policies(); var order = CombatFlowPlannerTests.TurnPolicy();
        var input = CanonicalJson.ComputeHash(new { run, combat });
        var observation = GameplayObservedExecutorTests.Observation();
        var expected = executor.InitializeTransaction(run, combat, sequence, policies, order);
        Assert.True(expected.IsSuccess, expected.IsFailure ? expected.Error : null);
        var observed = executor.InitializeObserved(run, combat, sequence, policies, order, observation);
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(expected.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
        Assert.Equal("combat.started", observed.Value.Observation.Events.Occurrences[0].Kind);
        var refreshed = Assert.Single(observed.Value.Observation.Events.Occurrences, item => item.Payload is ResourceChangePayload);
        Assert.Equal(3, Assert.IsType<ResourceChangePayload>(refreshed.Payload).After);
        Assert.Equal("hero", refreshed.SubjectActorId);
        Assert.Null(observed.Value.Observation.ParentEventId);
        var current = expected.Value;
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var first = executor.AdvanceObserved(current.Run, current.Combat, current.Run.Deck, current.Run.Determinism,
                sequence, policies, order, observation);
            Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
            var baseline = executor.AdvanceActivation(current.Run, current.Combat, current.Run.Deck, current.Run.Determinism, sequence, policies, order);
            Assert.True(baseline.IsSuccess, baseline.IsFailure ? baseline.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(baseline.Value), CanonicalJson.ComputeHash(first.Value.Execution));
            Assert.Single(first.Value.Observation.Events.Occurrences, item => item.Kind == "activation.ended");
            Assert.Single(first.Value.Observation.Events.Occurrences, item => item.Kind == "activation.started");
            Assert.Equal(2, first.Value.Observation.Events.Occurrences.Count(item => item.Payload is CardMovementPayload));
            Assert.Null(first.Value.Observation.ParentEventId);
            var last = first.Value.Execution.Steps.Last();
            var nextRun = last.RunSnapshot! with { Deck = last.Deck, Determinism = last.RunDeterminism! };
            var second = executor.AdvanceObserved(nextRun, first.Value.Execution.Combat, nextRun.Deck, nextRun.Determinism,
                sequence, policies, order, observation);
            Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
            Assert.Equal("hero", second.Value.Execution.Combat.ActivationState!.ActiveActorId);
            Assert.Single(second.Value.Observation.Events.Occurrences, item => item.Kind == "round.ended");
            Assert.Single(second.Value.Observation.Events.Occurrences, item => item.Kind == "round.started");
            Assert.Single(second.Value.Observation.Events.Occurrences, item => item.Payload is CardMovementPayload);
            fingerprints.Add(CanonicalJson.ComputeHash(new { first = first.Value.Observation.Events.Occurrences, second = second.Value.Observation.Events.Occurrences }));
        }
        Assert.Single(fingerprints);
        Assert.Empty(observation.Events.Occurrences);
        Assert.Equal(input, CanonicalJson.ComputeHash(new { run, combat }));
    }

    [Fact]
    public void FailedInitializationCannotExposeResourceResetCardsOrPartOfTheObservation()
    {
        var (run, combat, executor) = Fixture();
        var before = CanonicalJson.ComputeHash(new { run, combat });
        var observation = GameplayObservedExecutorTests.Observation(1);
        var result = executor.InitializeObserved(run, combat, CombatFlowPlannerTests.Sequence(), CombatFlowPlannerTests.Policies(),
            CombatFlowPlannerTests.TurnPolicy(), observation);
        Assert.True(result.IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(new { run, combat }));
        Assert.Empty(observation.Events.Occurrences);
        var initialized = executor.InitializeTransaction(run, combat, CombatFlowPlannerTests.Sequence(), CombatFlowPlannerTests.Policies(), CombatFlowPlannerTests.TurnPolicy()).Value;
        var initializedHash = CanonicalJson.ComputeHash(initialized);
        var constrained = GameplayObservedExecutorTests.Observation(3);
        var advanced = executor.AdvanceObserved(initialized.Run, initialized.Combat, initialized.Run.Deck, initialized.Run.Determinism,
            CombatFlowPlannerTests.Sequence(), CombatFlowPlannerTests.Policies(), CombatFlowPlannerTests.TurnPolicy(), constrained);
        Assert.True(advanced.IsFailure);
        Assert.Empty(constrained.Events.Occurrences);
        Assert.Equal(initializedHash, CanonicalJson.ComputeHash(initialized));
    }

    [Fact]
    public void FinalizationEmitsOneBoundaryAndRepeatedFinalizationDoesNotRepeatIt()
    {
        var (run, combat, executor) = Fixture();
        combat = combat with { Status = CombatStatus.VICTORY };
        var observation = GameplayObservedExecutorTests.Observation();
        var actual = executor.CompleteObserved(run, combat, observation);
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(executor.Complete(run, combat).Value), CanonicalJson.ComputeHash(actual.Value.Execution));
        Assert.Equal("combat.ended", Assert.Single(actual.Value.Observation.Events.Occurrences).Kind);
        var repeated = executor.CompleteObserved(actual.Value.Execution.Run!, actual.Value.Execution.Combat, actual.Value.Observation);
        Assert.True(repeated.IsSuccess, repeated.IsFailure ? repeated.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(actual.Value.Observation.Events.Occurrences), CanonicalJson.ComputeHash(repeated.Value.Observation.Events.Occurrences));
        Assert.Null(repeated.Value.Observation.ParentEventId);
    }
    [Fact]
    public void TerminalStatusBoundaryStopsBeforeNextActorAndKeepsTickUnderItsRealBoundary()
    {
        var (run, combat, executor) = Fixture();
        var configured = EffectSequenceBudgetTests.Fixture();
        run = run with { ResolvedMode = run.ResolvedMode! with { Definition = configured.Run.ResolvedMode!.Definition } };
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>();
        executor = new(new TurnOrderResolver(formulas), new CombatStatusLifecycle(configured.Executor),
            new CombatRelicLifecycle(configured.Executor), new CombatResourceLifecycle(configured.Executor),
            new PhaseGraphReducer(formulas, configured.Executor), new CombatOutcomeResolver(), CombatFlowPlannerTests.ZoneExecutor());
        var policies = CombatFlowPlannerTests.Policies() with { StatusTiming = new() { Boundaries = [StatusTriggerBoundary.EndActivation] } };
        var initialized = executor.InitializeTransaction(run, combat, CombatFlowPlannerTests.Sequence(), policies, CombatFlowPlannerTests.TurnPolicy()).Value;
        var hero = initialized.Combat.GetActor("hero")!;
        var health = EffectTransactionTests.Request().Combat.GetActor("hero")!.GetResource("focus")!;
        health = health with { Definition = health.Definition! with { ThresholdPolicies = [new()
            { PolicyId = "defeat", Comparison = ResourceThresholdComparison.LessThanOrEqual, ThresholdSource = ResourceThresholdSource.Minimum,
                Consequence = ResourceThresholdConsequence.DefeatOwner }] } };
        combat = initialized.Combat.ReplaceActor(hero with { ResourceState = hero.ResourceState with
            { Resources = new Dictionary<string, ResourcePool>(hero.ResourceState.Resources) { ["focus"] = health } } });
        combat = combat with { StatusEffects = combat.StatusEffects.SetItem("hero", [new()
        {
            InstanceId = DeterministicId.Create(42, 1, "fatal"), StatusId = "fatal", TargetId = "hero", SourceId = "enemy",
            ContentRevision = "revision", Stacks = 1, Duration = 1, Definition = new() { StatusId = "fatal",
                DurationTickBoundary = StatusTriggerBoundary.EndActivation, Triggers = [new() { TriggerId = "pulse", Boundary = "EndActivation",
                    Effects = [EffectSequenceBudgetTests.Damage(99, 1) with { Target = EffectTarget.SELF }] }] }
        }]) };
        var baseline = executor.AdvanceActivation(initialized.Run, combat, initialized.Run.Deck, initialized.Run.Determinism,
            CombatFlowPlannerTests.Sequence(), policies, CombatFlowPlannerTests.TurnPolicy());
        Assert.True(baseline.IsSuccess, baseline.IsFailure ? baseline.Error : null);
        var actual = executor.AdvanceObserved(initialized.Run, combat, initialized.Run.Deck, initialized.Run.Determinism,
            CombatFlowPlannerTests.Sequence(), policies, CombatFlowPlannerTests.TurnPolicy(), GameplayObservedExecutorTests.Observation());
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(baseline.Value), CanonicalJson.ComputeHash(actual.Value.Execution));
        Assert.False(actual.Value.Execution.Combat.IsActive);
        Assert.DoesNotContain(actual.Value.Observation.Events.Occurrences, item => item.Kind == "activation.started");
        var ended = Assert.Single(actual.Value.Observation.Events.Occurrences, item => item.Kind == "activation.ended");
        var resource = Assert.Single(actual.Value.Observation.Events.Occurrences, item => item.Payload is ResourceChangePayload);
        Assert.Equal(ended.EventId, resource.ParentEventId);
        Assert.Contains(actual.Value.Observation.Events.Occurrences, item => item.Kind == "actor.defeat.changed");
    }

    [Theory]
    [InlineData(ModifierDurationBoundary.Activation, 1)]
    [InlineData(ModifierDurationBoundary.Activation, 2)]
    [InlineData(ModifierDurationBoundary.Combat, 1)]
    [InlineData(ModifierDurationBoundary.Combat, 2)]
    public void ModifierDurationAndExpiryFollowOnlyTheirCanonicalBoundary(ModifierDurationBoundary boundary, int duration)
    {
        var (run, combat, executor) = Fixture();
        var instance = new ScriptModifierInstance { InstanceId = DeterministicId.Create(42, 5, "timer"), ModifierId = "timer",
            Owner = new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }, OwnerId = "hero", Duration = duration, Stacks = 1,
            ContentRevision = "revision", Definition = new() { ModifierId = "timer", DurationBoundary = boundary } };
        run = run with { Modifiers = [instance] };
        var initialized = executor.InitializeTransaction(run, combat, CombatFlowPlannerTests.Sequence(), CombatFlowPlannerTests.Policies(), CombatFlowPlannerTests.TurnPolicy()).Value;
        var observation = GameplayObservedExecutorTests.Observation();
        GameplayEffectObservation observed;
        if (boundary == ModifierDurationBoundary.Combat)
        {
            combat = initialized.Combat with { Status = CombatStatus.VICTORY };
            var actual = executor.CompleteObserved(initialized.Run, combat, observation);
            Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(executor.Complete(initialized.Run, combat).Value), CanonicalJson.ComputeHash(actual.Value.Execution));
            observed = actual.Value.Observation;
        }
        else
        {
            var actual = executor.AdvanceObserved(initialized.Run, initialized.Combat, initialized.Run.Deck, initialized.Run.Determinism,
                CombatFlowPlannerTests.Sequence(), CombatFlowPlannerTests.Policies(), CombatFlowPlannerTests.TurnPolicy(), observation);
            Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(executor.AdvanceActivation(initialized.Run, initialized.Combat, initialized.Run.Deck, initialized.Run.Determinism,
                CombatFlowPlannerTests.Sequence(), CombatFlowPlannerTests.Policies(), CombatFlowPlannerTests.TurnPolicy()).Value), CanonicalJson.ComputeHash(actual.Value.Execution));
            observed = actual.Value.Observation;
        }
        var fact = Assert.Single(observed.Events.Occurrences, item => item.Kind.StartsWith("modifier.", StringComparison.Ordinal));
        if (duration == 1)
        {
            var payload = Assert.IsType<ModifierChangePayload>(fact.Payload);
            Assert.Equal(EffectStackChangeReason.Expire, payload.Change);
            Assert.Equal(0, payload.AfterStacks);
        }
        else
        {
            var payload = Assert.IsType<InstanceDurationPayload>(fact.Payload);
            Assert.Equal(2, payload.Before); Assert.Equal(1, payload.After);
        }
        Assert.Null(fact.SourceActorId);
        Assert.Equal("hero", fact.SubjectActorId);
        var frontier = Assert.Single(observed.Events.Occurrences, item => item.Kind == (boundary == ModifierDurationBoundary.Combat ? "combat.ended" : "activation.ended"));
        Assert.Equal(frontier.EventId, fact.ParentEventId);
        Assert.Equal(duration, run.Modifiers[0].Duration);
        Assert.Empty(observation.Events.Occurrences);
    }

}
