using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Combat.Flow;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Combat.Reactions;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Tests.Combat.Flow;
using Core.Tests.Effects;
using Core.Effects;
using Core.Run;
using Core.GameplayEvents.Responses;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class ActionLifecycleResponseTests
{
    [Fact]
    public void ProposalUsesExistingStackAndPersistsItsCauseWithoutExecutingTheAction()
    {
        var (run, combat) = Fixture("action.proposed");
        var command = new CombatActionCommand { ActorId = "hero", ActionType = ActionType.POWER, PowerId = "test", TargetIds = ["enemy"] };
        var pending = new PendingActionState { PendingActionId = "pending", Depth = 1, Command = command, ContentRevision = "revision" };
        var policy = new ReactionPolicyDefinition { Strategy = ReactionStrategy.PriorityStack, StackOrder = ReactionStackOrder.Lifo,
            Eligibility = ReactionActorEligibility.AllAlive, MaxStackDepth = 8 };
        var proposed = new ReactionFlowReducer().Propose(combat, pending, policy);
        Assert.True(proposed.IsSuccess, proposed.IsFailure ? proposed.Error : null);
        var candidate = new LegalActionCandidate { CandidateId = "authorized", Command = command, PendingAction = pending,
            ReactionTransition = ReactionTransitionKind.Proposed, SuccessorRun = run, SuccessorCombat = proposed.Value };
        var reducer = new CombatActionStateReducer(new CombatOutcomeResolver(), occurrences: EffectSequenceBudgetTests.Fixture().Executor);
        var input = CanonicalJson.ComputeHash(new { run, combat }); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var observed = reducer.ApplyObserved(run, combat, candidate, command, CombatFlowPlannerTests.Policies(),
                GameplayObservedExecutorTests.ObservationFor(run, combat));
            Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
            var result = observed.Value.Execution;
            Assert.Null(result.ResolvedCommand);
            Assert.Equal(12, result.Combat.GetActor("ally")!.GetResource("focus")!.Current);
            var proposal = Assert.Single(observed.Value.Observation.Events.Occurrences, fact => fact.Kind == "action.proposed");
            Assert.Equal(proposal.EventId, Assert.Single(result.Combat.PendingActions).OriginCause!.EventId);
            Assert.Equal("pending", Assert.IsType<ActionOccurrencePayload>(proposal.Payload).PendingActionId);
            var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            var restored = JsonSerializer.Deserialize<CombatState>(JsonSerializer.Serialize(result.Combat, options), options)!;
            Assert.Equal(CanonicalJson.ComputeHash(result.Combat), CanonicalJson.ComputeHash(restored));
            Assert.Equal(proposal.EventId, Assert.Single(restored.PendingActions).OriginCause!.EventId);
            var normal = reducer.Apply(run, combat, candidate, command, CombatFlowPlannerTests.Policies());
            Assert.True(normal.IsSuccess, normal.IsFailure ? normal.Error : null);
            // Normal execution has its own command scope, but neither mode mutates the input stack.
            Assert.Equal(result.Combat.GetActor("ally")!.GetResource("focus")!.Current,
                normal.Value.Combat.GetActor("ally")!.GetResource("focus")!.Current);
            hashes.Add(CanonicalJson.ComputeHash(observed.Value.Observation.Events.Occurrences));
        }
        Assert.Single(hashes); Assert.Empty(combat.PendingActions); Assert.Equal(input, CanonicalJson.ComputeHash(new { run, combat }));
    }

    [Theory]
    [InlineData(ReactionTransitionKind.StackActionResolved, "action.resolved")]
    [InlineData(ReactionTransitionKind.StackActionFizzled, "action.cancelled")]
    public void TerminalTransitionKeepsHistoricalProposalSeparateFromCurrentCommand(ReactionTransitionKind transition, string kind)
    {
        var (run, combat) = Fixture(kind);
        var observation = GameplayObservedExecutorTests.ObservationFor(run, combat);
        var command = new CombatActionCommand { ActorId = "hero", ActionType = ActionType.POWER, PowerId = "test" };
        var proposalContext = GameplayCommandObservation.Create(run, combat.CombatId, new(Guid.Parse("6b8b6d28-34f7-4bea-946a-7128defd65dd"), "TEST", 0, 0)).Value;
        var proposal = GameplayActionObservation.Begin(proposalContext, "test", "hero", "action.proposed").Value.Events.Occurrences.Last();
        var pass = new CombatActionCommand { ActorId = "enemy", ActionType = ActionType.PASS_PRIORITY };
        var candidate = new LegalActionCandidate { CandidateId = "authorized", Command = pass,
            ResolvedCommand = transition == ReactionTransitionKind.StackActionResolved ? command : null,
            ReactionTransition = transition, SuccessorRun = run, SuccessorCombat = combat,
            PendingAction = new() { PendingActionId = "pending", Command = command, OriginCause = GameplayCausalAnchor.From(proposal) } };
        var result = new CombatActionStateReducer(new CombatOutcomeResolver(), occurrences: EffectSequenceBudgetTests.Fixture().Executor)
            .ApplyObserved(run, combat, candidate, pass, CombatFlowPlannerTests.Policies(), observation);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var terminal = Assert.Single(result.Value.Observation.Events.Occurrences, fact => fact.Kind == kind);
        Assert.Equal("hero", terminal.SourceActorId); Assert.Equal(proposal.EventId, terminal.OriginCause!.EventId);
        Assert.Equal(12, result.Value.Execution.Combat.GetActor("ally")!.GetResource("focus")!.Current);
        Assert.Empty(observation.Events.Occurrences);
    }
    private static (RunState Run, CombatState Combat) Fixture(string kind)
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture(kind);
        var ally = combat.GetActor("ally")!;
        var watcher = (EntityResponseComponentState)ally.Components["watch"];
        combat = combat.ReplaceActor(ally with { Components = ally.Components.ToImmutableDictionary(StringComparer.Ordinal).SetItem("watch",
            watcher with { Responses = [watcher.Responses!.Value[0] with { Effects = [EffectSequenceBudgetTests.Damage(2, 1) with {
                Type = EffectType.HEAL, Target = EffectTarget.SELF }] }] }) });
        return (run with { ResolvedMode = EffectSequenceBudgetTests.Fixture().Run.ResolvedMode }, combat);
    }

}
