using System.Collections.Immutable;
using System.Text.Json;
using Core.CardZones;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.Intents;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Run;
using Core.Tests.Combat.Flow;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayAutomaticObservationTests
{
    [Theory]
    [InlineData(64, true)]
    [InlineData(2, false)]
    public void SameDriverPreservesAutomaticTurnAndRollbackThroughRealBoundaries(int maximum, bool succeeds)
    {
        var graph = CombatFlowPlannerTests.Sequence();
        const string path = "phase-sequences/test.json";
        var runtime = ContentRuntime.Create(new()
        {
            Manifest = new() { Revision = "revision", ConfigName = "default",
                Artifacts = [new() { Kind = "phase-sequences", Path = path, DefinitionCount = 1 }] },
            Artifacts = ImmutableDictionary<string, JsonElement>.Empty.Add(path,
                JsonSerializer.SerializeToElement(new Dictionary<string, PhaseSequenceDefinition> { [graph.SequenceId] = graph }))
        }).Value;
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve("revision", "default")).Returns(Result<ContentRuntime>.Success(runtime));
        var policies = CombatFlowPlannerTests.Policies() with
        {
            AutomaticResolution = new() { Strategy = AutomaticResolutionStrategy.ToNextPlayerInput, MaxAutomaticSteps = maximum },
            Ai = new() { Enabled = true, PublishIntents = false, AutoEndAfterAction = false,
                Intent = new() { Refresh = IntentRefreshStrategy.RecomputeOnPublish, WhenInvalid = InvalidIntentStrategy.Recompute } }
        };
        var zones = CombatFlowPlannerTests.LifecycleZones();
        var seeded = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(zones).Value,
            new() { RunOwnerId = "$run", Batches = [new() { ZoneId = "hand", OwnerId = "$run", DefinitionIds = ["strike"] }] },
            DeterministicContext.Create(1, "revision")).Value;
        var run = new RunState { PlayerEntityId = "hero", Determinism = seeded.Context, Deck = new() { Topology = seeded.State }, ResolvedMode = new()
        { CardZoneSystem = zones, CombatRules = new() { DefaultPhaseSequenceId = graph.SequenceId, Flow = policies, TurnOrder = CombatFlowPlannerTests.TurnPolicy() } } };
        var combat = CombatTransitions.Create([CombatFlowPlannerTests.Entity("hero", true, 0), CombatFlowPlannerTests.Entity("enemy", false, 0)],
            DeterministicContext.Create(2, "revision"));
        var planner = new CombatFlowPlanner(runtimes.Object, CombatFlowPlannerTests.Boundaries(CombatFlowPlannerTests.ZoneExecutor()), Mock.Of<IIntentResolver>());
        var initialized = planner.InitializeTransaction(run, combat).Value;
        var observedInitialization = planner.InitializeObserved(run, combat, Core.Tests.GameplayEvents.GameplayObservedExecutorTests.Observation());
        Assert.True(observedInitialization.IsSuccess, observedInitialization.IsFailure ? observedInitialization.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(initialized), CanonicalJson.ComputeHash(observedInitialization.Value.Execution));
        var legal = new Mock<ILegalActionResolver>();
        LegalActionEvaluation Evaluate(RunState state, CombatState current, CombatActionCommand command) => new()
        { Candidate = new() { CandidateId = "selected", Source = LegalActionSource.System, Command = command, ResolvedCommand = command,
            SuccessorRun = state, SuccessorCombat = current, ResolutionFingerprint = "selected" } };
        legal.Setup(service => service.Evaluate(It.IsAny<RunState>(), It.IsAny<CombatState>(), It.IsAny<CombatActionCommand>(), It.IsAny<CombatCommandOrigin>()))
            .Returns((RunState state, CombatState current, CombatActionCommand command, CombatCommandOrigin _) => Result<LegalActionEvaluation>.Success(Evaluate(state, current, command)));
        legal.Setup(service => service.EvaluateObserved(It.IsAny<RunState>(), It.IsAny<CombatState>(), It.IsAny<CombatActionCommand>(), It.IsAny<CombatCommandOrigin>(), It.IsAny<GameplayEffectObservation>()))
            .Returns((RunState state, CombatState current, CombatActionCommand command, CombatCommandOrigin _, GameplayEffectObservation context) =>
                Result<ObservedGameplayExecution<LegalActionEvaluation>>.Success(new(Evaluate(state, current, command), context)));
        var handler = new CombatCommandHandler(legal.Object, new CombatActionStateReducer(new CombatOutcomeResolver(), CombatFlowPlannerTests.ZoneExecutor()));
        var decisions = new Mock<IDecisionPolicyRegistry>();
        decisions.Setup(service => service.Decide(It.IsAny<ControllerBinding>(), It.IsAny<DecisionPolicyRequest>()))
            .Returns((ControllerBinding _, DecisionPolicyRequest request) => Result<DecisionPolicyResult>.Success(new()
            { Candidate = new() { Command = new() { RunId = request.Run.RunId, ActorId = request.ActorId, ActionType = ActionType.END_TURN } }, RuleId = "end" }));
        var driver = new AutomaticFlowDriver(planner, decisions.Object, handler);
        var request = new AutomaticFlowRequest { CombatId = initialized.Combat.CombatId, Run = initialized.Run, Combat = initialized.Combat, RequestsActivationAdvance = true };
        var hash = CanonicalJson.ComputeHash(request);
        var observation = GameplayObservedExecutorTests.Observation();
        var baseline = driver.Drive(request);
        Assert.Equal(succeeds, baseline.IsSuccess);
        var fingerprints = new HashSet<string>();
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var actual = driver.DriveObserved(request, observation);
            Assert.Equal(succeeds, actual.IsSuccess);
            if (succeeds)
            {
                Assert.Equal(CanonicalJson.ComputeHash(baseline.Value), CanonicalJson.ComputeHash(actual.Value.Execution));
                Assert.Equal("hero", actual.Value.Execution.Combat.ActivationState!.ActiveActorId);
                Assert.True(actual.Value.Execution.Combat.ActivationState.WaitingForInput);
                Assert.Single(actual.Value.Observation.Events.Occurrences, item => item.Kind == "action.started" && item.SourceActorId == "enemy");
                Assert.Single(actual.Value.Observation.Events.Occurrences, item => item.Kind == "action.completed" && item.SourceActorId == "enemy");
                Assert.Equal(2, actual.Value.Observation.Events.Occurrences.Count(item => item.Kind == "activation.started"));
                fingerprints.Add(CanonicalJson.ComputeHash(actual.Value.Observation.Events.Occurrences));
            }
            else Assert.Equal(baseline.Error, actual.Error);
        }
        if (succeeds) Assert.Single(fingerprints);
        Assert.Equal(hash, CanonicalJson.ComputeHash(request));
        Assert.Empty(observation.Events.Occurrences);
        var noOpRequest = request with { RequestsActivationAdvance = false };
        var noOp = driver.DriveObserved(noOpRequest, observation);
        Assert.True(noOp.IsSuccess, noOp.IsFailure ? noOp.Error : null);
        Assert.Empty(noOp.Value.Observation.Events.Occurrences);
    }
}
