using System.Collections.Immutable;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.StatusEffects;
using Core.Tests.Effects;
using Core.Combat.TurnPhase;
using Core.Math;
using Moq;

namespace Core.Tests.GameplayEvents;

internal static class BoundaryMigrationFixtures
{
    internal static (RunState Run, CombatState Combat, PhaseGraphReducer Reducer, PhaseSequenceDefinition Graph) Phase()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var gain = EffectSequenceBudgetTests.Damage(1, 1) with { Type = EffectType.MODIFY_RESOURCE, Operation = ResourceEffectOperation.ADD, Target = EffectTarget.SELF };
        var graph = new PhaseSequenceDefinition { SequenceId = "graph", EntryPhaseId = "begin", Phases = [
            new() { PhaseId = "begin", Role = PhaseRole.Start, Order = 0, EntryEffects = [gain], Edges = [new() { EdgeId = "to-main", TargetPhaseId = "main", Trigger = PhaseEdgeTrigger.Automatic }] },
            new() { PhaseId = "main", Role = PhaseRole.Middle, Order = 1, ExitEffects = [gain], AllowedActions = [ActionType.PASS, ActionType.END_TURN], Edges = [
                new() { EdgeId = "end", TargetPhaseId = "finish", Trigger = PhaseEdgeTrigger.ActivationExit },
                new() { EdgeId = "pass", TargetPhaseId = "finish", Trigger = PhaseEdgeTrigger.Command, ActionTypes = [ActionType.PASS] }] },
            new() { PhaseId = "finish", Role = PhaseRole.End, Order = 2 }] };
        return (fixture.Run, EffectTransactionTests.Request().Combat with { ActivationState = new() { ActiveActorId = "hero" } },
            new(Mock.Of<IRuntimeFormulaEvaluator>(), fixture.Executor), graph);
    }

    internal static (RunState Run, CombatState Combat, CombatStatusLifecycle Lifecycle, StatusTriggerBoundary Boundary) Status(string scenario)
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var boundary = scenario == "round" ? StatusTriggerBoundary.EndRound : StatusTriggerBoundary.EndActivation;
        StatusEffectInstance Make(string id, string target, int priority, int duration) => new()
        {
            InstanceId = DeterministicId.Create(42, (ulong)priority, id + target), StatusId = id, TargetId = target,
            SourceId = scenario == "missing-source" ? "absent" : "hero", ContentRevision = "revision", Stacks = 2, Duration = duration,
            Definition = new() { StatusId = id, Priority = priority, DurationTickBoundary = boundary, Triggers = [new()
                { TriggerId = "pulse", Boundary = boundary.ToString(), Effects = [EffectSequenceBudgetTests.Damage(1, 1) with { Target = EffectTarget.SELF }] }] }
        };
        var high = Make("high", "enemy", 3, scenario == "permanent" ? -1 : 1);
        var low = Make("low", "enemy", scenario == "tie" ? 3 : 2, scenario == "permanent" ? -1 : 2);
        if (scenario == "empty") high = high with { Definition = high.Definition with { Triggers = [new() { TriggerId = "empty", Boundary = boundary.ToString(), Effects = [] }] } };
        if (scenario == "remove") high = high with { Definition = high.Definition with { Triggers = [new()
            { TriggerId = "remove", Boundary = boundary.ToString(), Effects = [new() { Type = EffectType.REMOVE_STATUS, Target = EffectTarget.SELF, StatusId = "low" }] }] } };
        var statuses = ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>>.Empty.Add("enemy", [low, high]);
        if (scenario == "round") statuses = statuses.Add("hero", [Make("other", "hero", 1, 1)]);
        var combat = EffectTransactionTests.Request().Combat with { ActivationState = new() { ActiveActorId = "enemy", ActivationOrder = ["enemy", "hero"] }, StatusEffects = statuses };
        return (fixture.Run, combat, new(fixture.Executor), boundary);
    }

    internal static (RunState Run, CombatState Combat, CombatRelicLifecycle Lifecycle) Relics()
    {
        var fixture = EffectSequenceBudgetTests.Fixture(); var run = fixture.Run;
        foreach (var id in new[] { "one", "two" }) run = RelicTransitions.Acquire(run, new() { RelicId = id, Triggers = [
            new() { TriggerId = "last", Boundary = "CombatStart", Priority = 0, Effects = [EffectSequenceBudgetTests.Damage(1, 1) with { Target = EffectTarget.SELF }] },
            new() { TriggerId = "first", Boundary = "CombatStart", Priority = 4, Effects = [EffectSequenceBudgetTests.Damage(2, 1) with { Target = EffectTarget.SELF }] }] },
            new() { Kind = GameplayOwnerKind.Global }).Value.State;
        return (run, EffectTransactionTests.Request().Combat, new(fixture.Executor));
    }
}
