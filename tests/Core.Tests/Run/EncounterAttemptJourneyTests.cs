using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Core.Run.Runtime;
using Xunit;

namespace Core.Tests.Run;

public sealed partial class ActorResourceJourneyTests
{
    [Theory]
    [InlineData(CombatStatus.DEFEAT, true)]
    [InlineData(CombatStatus.DRAW, true)]
    [InlineData(CombatStatus.ABANDONED, true)]
    [InlineData(CombatStatus.VICTORY, false)]
    public async Task AttemptOutcomeRestoresOnlyConfiguredPoolsAndSurvivesRestartAndReplay(CombatStatus outcome, bool retry)
    {
        var parent = Attempt(outcome);
        var originalHash = CanonicalJson.ComputeHash(parent);
        await Durable(parent, branch => Envelope(branch, RunCommandTypes.ResolveCombat,
            new ResolveCombatCommand(branch.ActiveEncounterId!.Value)), state =>
        {
            Assert.Null(state.ActiveEncounterId);
            Assert.Equal(RunLifecycleState.Active, state.Lifecycle);
            Assert.Equal(retry ? 60 : 9, Current(state));
            Assert.Equal(50, state.ResourceState.Current("resolve"));
            Assert.Equal(CanonicalJson.ComputeHash(parent.Deck), CanonicalJson.ComputeHash(state.Deck));
            Assert.Equal(CanonicalJson.ComputeHash(parent.Metadata), CanonicalJson.ComputeHash(state.Metadata));
            Assert.Equal(CanonicalJson.ComputeHash(parent.Modifiers), CanonicalJson.ComputeHash(state.Modifiers));
            Assert.Equal(CanonicalJson.ComputeHash(parent.Relics), CanonicalJson.ComputeHash(state.Relics));
            Assert.Equal(CanonicalJson.ComputeHash(parent.DeckLoadout), CanonicalJson.ComputeHash(state.DeckLoadout));
            Assert.Equal(CanonicalJson.ComputeHash(parent.Loadouts), CanonicalJson.ComputeHash(state.Loadouts));
            Assert.Empty(state.CardSelections);
            Assert.Empty(state.CompletedActivityNodeIds);
            var recorded = Assert.Single(state.Encounters);
            Assert.True(recorded.Resolved);
            Assert.Equal(outcome.ToString(), recorded.Outcome);
            Assert.NotEqual(Guid.Empty, recorded.Combat.CombatId);
            Assert.Equal(state.RunId, recorded.Combat.RunId);
            Assert.Equal(9, recorded.Combat.GetActor("hero")!.ResourceState.Current("resolve"));
            Assert.Equal(parent.Determinism.RandomState, state.Determinism.RandomState);
            var commands = new RunProgressionService(RunActivityRegistry.CreateDefault()).GetAvailableCommands(state).Value;
            Assert.Equal(retry ? RunCommandTypes.StartEncounter : RunCommandTypes.AdvanceNode, Assert.Single(commands).Type);
            Assert.Equal(!retry, state.Map.ResolvedNodeIds.Contains("fight"));
        });
        Assert.Equal(originalHash, CanonicalJson.ComputeHash(parent));
    }

    [Fact]
    public void NewAttemptContinuesCampaignStreamAndPreservesPriorAttemptWhenOpeningFails()
    {
        var runtime = Fixture().Factory.Create(new(GameplayPersistenceMode.Ephemeral));
        var parent = Attempt(CombatStatus.DEFEAT);
        Assert.True(runtime.Runs.HydrateForReplay(parent).IsSuccess);
        var resolved = runtime.Gateway.Execute(parent.RunId, Envelope(parent, RunCommandTypes.ResolveCombat,
            new ResolveCombatCommand(parent.ActiveEncounterId!.Value)), parent.ActiveEncounterId);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : null);
        var pending = resolved.Value.Receipt.State;
        var before = CanonicalJson.ComputeHash(pending);
        var draw = pending.Determinism.DrawUInt64();
        var next = parent.Encounters[0].Combat with
        {
            CombatId = DeterministicId.Create(draw.Value, 0, "next-attempt"),
            Status = CombatStatus.ACTIVE,
            Determinism = DeterministicContext.Create(draw.Value, pending.Determinism.ContentRevision)
        };
        var materialized = PersistentPlayerTransitions.Materialize(pending, next);
        Assert.True(materialized.IsSuccess, materialized.IsFailure ? materialized.Error : null);
        next = materialized.Value;
        Assert.Equal(60, next.GetActor("hero")!.ResourceState.Current("resolve"));
        var rejected = runtime.Runs.AttachEncounter(pending.RunId, pending.Sequence, pending.Determinism.Step,
            next with { Determinism = DeterministicContext.Create(draw.Value ^ 1UL, pending.Determinism.ContentRevision) });
        Assert.True(rejected.IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(runtime.Runs.GetRun(pending.RunId).Value));
        var attached = runtime.Runs.AttachEncounter(pending.RunId, pending.Sequence, pending.Determinism.Step, next);
        Assert.True(attached.IsSuccess, attached.IsFailure ? attached.Error : null);
        Assert.Equal(2, attached.Value.Encounters.Length);
        Assert.Equal(pending.Encounters[0], attached.Value.Encounters[0]);
        Assert.Equal(draw.Context.RandomState, attached.Value.Determinism.RandomState);
        Assert.NotEqual(pending.Encounters[0].Combat.CombatId, attached.Value.ActiveEncounterId);
        Assert.Equal(before, CanonicalJson.ComputeHash(pending));
    }

    [Theory]
    [InlineData(CombatStatus.DEFEAT, true, false)]
    [InlineData(CombatStatus.DRAW, true, false)]
    [InlineData(CombatStatus.ABANDONED, true, false)]
    [InlineData(CombatStatus.VICTORY, true, true)]
    [InlineData(CombatStatus.DEFEAT, false, true)]
    public void CompletionQueryUsesLatestAttemptAndPinnedRetryPolicy(CombatStatus outcome, bool enabled, bool complete)
    {
        var run = Attempt(outcome);
        if (!enabled) run = run with { ResolvedMode = run.ResolvedMode! with
            { ProgressionPolicy = run.ResolvedMode.ProgressionPolicy! with { EncounterRetry = RunEncounterRetryPolicy.Disabled } } };
        var service = new RunProgressionService(RunActivityRegistry.CreateDefault());
        Assert.Equal(complete, service.CanResolve(run, run.Map.Nodes[0]).IsSuccess);
        var reopened = run with { Encounters = run.Encounters.Add(run.Encounters[0] with
            { Combat = run.Encounters[0].Combat with { Status = CombatStatus.ACTIVE } }) };
        Assert.True(service.CanResolve(reopened, reopened.Map.Nodes[0]).IsFailure);
    }

    private static RunState Attempt(CombatStatus outcome)
    {
        var run = Prepared() with { Preparations = [], CurrentNodeId = "fight",
            Metadata = ImmutableDictionary<string, System.Text.Json.JsonElement>.Empty.Add("permanent-choice",
                System.Text.Json.JsonSerializer.SerializeToElement("retained")),
            Map = new() { Nodes = [new() { NodeId = "fight", Activity = new() { Type = RunActivityType.Encounter }, NextNodeIds = ["rest"] },
                new() { NodeId = "rest", Activity = new() { Type = RunActivityType.Preparation, DefinitionId = "recovery" } }], VisitedNodeIds = ["fight"] } };
        var id = DeterministicId.Create(123, 18, "attempt");
        return run with { ActiveEncounterId = id, Encounters = [new() { NodeId = "fight", Combat = new()
            { CombatId = id, RunId = run.RunId, RunNodeId = "fight", Status = outcome, Determinism = run.Determinism,
                Actors = new Dictionary<string, CombatActorState> { ["hero"] = ActorResourceLifecycleTests.Actor(9) } } }],
            ResolvedMode = run.ResolvedMode! with
            { ActorResourceLifecyclePolicy = ActorResourceLifecycleTests.Policy() with
                { Rules = [ActorResourceLifecycleTests.Rule() with { Retry = ActorResourceLifecycleAction.ResetToMaximum }] },
                ProgressionPolicy = new() { EncounterRetry = RunEncounterRetryPolicy.RestartActivity,
                    RetryableEncounterOutcomes = [CombatStatus.DEFEAT, CombatStatus.DRAW, CombatStatus.ABANDONED] } } };
    }
}
