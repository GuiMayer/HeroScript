using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Reactions;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Run.Branching;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class PriorityResponseLedgerTests
{
    [Fact]
    public void PriorityFrequencySurvivesCommandsSaveLoadAndNestedProposals()
    {
        var (run, combat) = Fixture(); var effects = EffectTransactionTests.Executor();
        var before = CanonicalJson.ComputeHash(new { run, combat });
        var first = Apply(run, combat, 1);
        Assert.Equal(12, first.Execution.State.GetActor("ally")!.GetResource("focus")!.Current);
        var ledger = first.Execution.State.PriorityWindow!.Responses!;
        Assert.Equal(1, Assert.Single(ledger.Counts).Value);
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var restored = JsonSerializer.Deserialize<CombatState>(JsonSerializer.Serialize(first.Execution.State, options), options)!;
        var second = Apply(first.Execution.Run!, restored, 2);
        Assert.Equal(12, second.Execution.State.GetActor("ally")!.GetResource("focus")!.Current);
        Assert.Empty(second.Execution.Steps.Skip(1));
        var nested = new ReactionFlowReducer().Propose(restored, Pending("nested", restored.PriorityWindow!.HolderActorId, 2), Policy()).Value;
        Assert.NotEqual(restored.PriorityWindow.WindowId, nested.PriorityWindow!.WindowId);
        Assert.Equal(ledger.WindowId, nested.PriorityWindow.Responses!.WindowId);
        Assert.Equal(12, Apply(first.Execution.Run!, nested, 3).Execution.State.GetActor("ally")!.GetResource("focus")!.Current);
        Assert.Equal(before, CanonicalJson.ComputeHash(new { run, combat }));

        ObservedEffectExecution Apply(RunState state, CombatState snapshot, int id)
        {
            var observation = GameplayCommandObservation.Create(state, snapshot.CombatId,
                new(new Guid(id, 0, 0, new byte[8]), "TEST", 0, 0)).Value;
            var result = effects.ExecuteObserved(EffectTransactionTests.Request(new EffectDefinition {
                Type = EffectType.REMOVE_STATUS, StatusId = "absent" }) with { Run = state, Combat = snapshot }, observation);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            return result.Value;
        }
    }

    [Fact]
    public void ForksReserveIndependentSuccessorsAndForeignCounterScopeIsRejected()
    {
        var (run, combat) = Fixture(); var effects = EffectTransactionTests.Executor();
        var request = EffectTransactionTests.Request(new EffectDefinition { Type = EffectType.REMOVE_STATUS, StatusId = "absent" })
            with { Run = run, Combat = combat };
        var observation = GameplayObservedExecutorTests.ObservationFor(run, combat);
        var a = effects.ExecuteObserved(request, observation); var b = effects.ExecuteObserved(request, observation);
        Assert.True(a.IsSuccess, a.IsFailure ? a.Error : null); Assert.True(b.IsSuccess, b.IsFailure ? b.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(a.Value), CanonicalJson.ComputeHash(b.Value));
        Assert.Null(combat.PriorityWindow!.Responses);
        var corrupted = a.Value.Execution.State with { PriorityWindow = a.Value.Execution.State.PriorityWindow! with {
            Responses = a.Value.Execution.State.PriorityWindow!.Responses! with { SettingId = "foreign" } } };
        var bad = effects.ExecuteObserved(request with { Combat = corrupted }, observation);
        Assert.True(bad.IsFailure); Assert.Contains("continuation", bad.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualBranchesRetainWindowCountsAndAuthorizedProposalAncestry(bool encounterObserver)
    {
        var (run, combat) = Fixture();
        var affected = encounterObserver ? "hero" : "ally";
        if (encounterObserver)
        {
            var ally = combat.GetActor("ally")!;
            var response = ((EntityResponseComponentState)ally.Components["watch"]).Responses!.Value[0];
            combat = combat.ReplaceActor(ally with { Components = ally.Components.ToImmutableDictionary(StringComparer.Ordinal).Remove("watch") });
            combat = combat with { RunNodeId = "node" };
            run = run with { Map = new() { Nodes = [new() { NodeId = "node", Activity = new() { Type = RunActivityType.Encounter },
                ResponseContentRevision = "revision", Responses = [response with { Bindings = response.Bindings with {
                    Owner = new() { Kind = ResponseActorBindingKind.ExplicitActor, ActorId = "hero" } } }] }] } };
        }
        var id = Guid.Parse("8add3ce8-55f8-422d-89f5-023b58b8097e");
        run = run with { RunId = id, Sequence = 1, Lineage = RunLineage.Root(id) };
        combat = combat with { RunId = id };
        var observation = GameplayCommandObservation.Create(run, combat.CombatId, new(
            Guid.Parse("843fa045-7d18-4763-a505-f00ca81167a9"), "TEST", 0, 0)).Value;
        var proposal = GameplayActionObservation.Begin(observation, "test", "hero", "action.proposed").Value.Events.Occurrences.Last();
        combat = combat with { PendingActions = [combat.PendingActions[0] with { OriginCause = GameplayCausalAnchor.From(proposal) }] };
        var effects = EffectTransactionTests.Executor();
        var request = EffectTransactionTests.Request(new EffectDefinition { Type = EffectType.REMOVE_STATUS, StatusId = "absent" });
        var first = effects.ExecuteObserved(request with { Run = run, Combat = combat }, observation);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        run = first.Value.Execution.Run! with { ActiveEncounterId = combat.CombatId, Encounters = [new() { NodeId = "node", Combat = first.Value.Execution.State }] };
        var original = CanonicalJson.ComputeHash(run);
        var child = Fork(run, "first"); var grandchild = Fork(child, "second");
        foreach (var branch in new[] { child, grandchild })
        {
            var snapshot = branch.GetActiveEncounter()!.Combat;
            var continuation = GameplayCommandObservation.Create(branch, snapshot.CombatId, new(
                Guid.Parse("110c1861-025c-4b5a-9455-96f643b1cdc9"), "TEST", 0, 0)).Value
                .WithActionCause(snapshot.PendingActions[0].OriginCause);
            var result = effects.ExecuteObserved(request with { Run = branch, Combat = snapshot }, continuation);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(12, result.Value.Execution.State.GetActor(affected)!.GetResource("focus")!.Current);
            Assert.Equal(proposal.EventId, result.Value.Observation.Events.Occurrences[0].OriginCause!.EventId);
            Assert.Equal(1, Assert.Single(result.Value.Execution.State.PriorityWindow!.Responses!.Counts).Value);
        }
        Assert.Equal(original, CanonicalJson.ComputeHash(run));
        static RunState Fork(RunState source, string key)
        {
            var result = RunBranchTransitions.Create(source, new(source.RunId, source.Sequence, key,
                CanonicalJson.ComputeHash(source), source.ActiveEncounterId));
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            return result.Value;
        }
    }

    private static (RunState Run, CombatState Combat) Fixture()
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture("effect.application.resolved");
        var ally = combat.GetActor("ally")!; var watcher = (EntityResponseComponentState)ally.Components["watch"];
        combat = combat.ReplaceActor(ally with { Components = ally.Components.ToImmutableDictionary(StringComparer.Ordinal).SetItem("watch",
            watcher with { Responses = [watcher.Responses!.Value[0] with { Frequency = new() { Scope = ResponseFrequencyScope.PriorityWindow } }] }) });
        return (run, new ReactionFlowReducer().Propose(combat, Pending("first", "hero", 1), Policy()).Value);
    }
    private static PendingActionState Pending(string id, string actor, int depth) => new() {
        PendingActionId = id, Depth = depth, ContentRevision = "revision", Command = new() { ActorId = actor, ActionType = ActionType.POWER } };
    private static ReactionPolicyDefinition Policy() => new() { Strategy = ReactionStrategy.PriorityStack,
        StackOrder = ReactionStackOrder.Lifo, Eligibility = ReactionActorEligibility.AllAlive, MaxStackDepth = 8 };
}
