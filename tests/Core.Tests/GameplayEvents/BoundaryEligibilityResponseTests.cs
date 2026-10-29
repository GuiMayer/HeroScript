using System.Collections.Immutable;
using Core.Combat.Flow;
using Core.Combat.Modifiers;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Combat.TurnPhase;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.StatusEffects;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class BoundaryEligibilityResponseTests
{
    [Fact]
    public void StatusCreatedAfterBoundaryCaptureDoesNotRetroactivelyTriggerOrExpire()
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture("status.expired");
        var captured = combat.StatusEffects;
        var status = new StatusEffectInstance { InstanceId = Guid.Parse("d8a4f59c-9ee3-48a6-b055-dfcb03b2e222"), StatusId = "new",
            TargetId = "enemy", SourceId = "hero", ContentRevision = "revision", Stacks = 1, Duration = 1,
            Definition = new() { StatusId = "new", DurationTickBoundary = StatusTriggerBoundary.EndActivation, Triggers = [new()
                { TriggerId = "pulse", Boundary = "EndActivation", Effects = [new() { Type = EffectType.DAMAGE, TargetResource = "focus", FlatValue = 9 }] }] } };
        combat = combat with { StatusEffects = combat.StatusEffects.Add("enemy", [status]) };
        var before = CanonicalJson.ComputeHash(combat);
        var observation = GameplayObservedExecutorTests.ObservationFor(run, combat) with { BoundaryStatusSnapshot = captured };
        var result = new CombatStatusLifecycle(EffectTransactionTests.Executor()).ProcessObserved(run, combat,
            StatusTriggerBoundary.EndActivation, observation, "enemy");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(1, Assert.Single(result.Value.Execution.Combat.StatusEffects["enemy"]).Duration);
        Assert.Empty(result.Value.Execution.Events); Assert.Empty(result.Value.Observation.Events.Occurrences);
        Assert.Equal(before, CanonicalJson.ComputeHash(result.Value.Execution.Combat));
        Assert.Equal(before, CanonicalJson.ComputeHash(combat));
    }

    [Theory]
    [InlineData(1, "modifier.stacks.changed")]
    [InlineData(2, "modifier.duration.changed")]
    public void ModifierTimingUsesTheCommonDispatcherAndKeepsTheInputImmutable(int duration, string kind)
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture(kind);
        combat = combat with { Status = CombatStatus.VICTORY };
        run = run with { Modifiers = [new() { InstanceId = Guid.Parse("f0a1873d-e77b-4920-b4b4-679932ec9084"), ModifierId = "timer",
            Owner = new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }, OwnerId = "hero", Duration = duration, Stacks = 1,
            ContentRevision = "revision", Definition = new() { ModifierId = "timer", DurationBoundary = ModifierDurationBoundary.Combat } }] };
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>(); var triggers = EffectTransactionTests.Executor();
        var boundaries = new CombatBoundaryExecutor(new TurnOrderResolver(formulas), new CombatStatusLifecycle(triggers),
            new CombatRelicLifecycle(triggers), new CombatResourceLifecycle(triggers), new PhaseGraphReducer(formulas, triggers),
            new CombatOutcomeResolver(), occurrences: triggers);
        var input = CanonicalJson.ComputeHash(new { run, combat });
        var normal = boundaries.Complete(run, combat);
        Assert.True(normal.IsSuccess, normal.IsFailure ? normal.Error : null);
        Assert.Equal(12, normal.Value.Combat.GetActor("ally")!.GetResource("focus")!.Current);
        Assert.Single(normal.Value.ResponseApplications);
        var observed = boundaries.CompleteObserved(run, combat, GameplayObservedExecutorTests.ObservationFor(run, combat));
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(normal.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
        Assert.Single(observed.Value.Observation.Events.Occurrences, fact => fact.Kind == kind);
        Assert.Equal(input, CanonicalJson.ComputeHash(new { run, combat }));
    }
}
