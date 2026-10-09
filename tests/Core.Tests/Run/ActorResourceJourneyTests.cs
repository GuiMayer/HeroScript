using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Calculations;
using Core.CardZones;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Events;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Branching;
using Core.Run.Content;
using Core.Run.Replay;
using Core.Run.Runtime;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class ActorResourceJourneyTests
{
    [Fact]
    public void ExplicitParticipantBindingSupportsCustomIdentityAndRejectsAliasOrOwnerCollisions()
    {
        CombatParticipantReference player = new("template", "operator", "allies", new() { Kind = ControllerKind.Player })
            { IdentityBinding = CombatParticipantIdentityBinding.RunPlayer };
        CombatParticipantReference enemy = new("enemy", "operator", "opposition", new() { Kind = ControllerKind.AI });
        var result = CombatParticipantBindings.Resolve([player, enemy], "custom-profile");
        Assert.True(result.IsSuccess);
        Assert.Equal("custom-profile", result.Value[0].InstanceId);
        Assert.Equal("enemy", result.Value[1].InstanceId);
        Assert.Equal("template", player.InstanceId);
        Assert.True(CombatParticipantBindings.Resolve([player, enemy with { InstanceId = "custom-profile" }], "custom-profile").IsFailure);
        Assert.True(CombatParticipantBindings.Resolve([player, enemy with { InstanceId = "template" }], "custom-profile").IsFailure);
        Assert.True(CombatParticipantBindings.Resolve([player with { IdentityBinding = (CombatParticipantIdentityBinding)42 }, enemy], "custom-profile").IsFailure);
    }

    [Fact]
    public void ActivityBindingKeepsIdenticalResourceIdsSeparateAndUsesTheSameCalculationProcessor()
    {
        var fixture = Fixture();
        var initial = Prepared();
        var player = fixture.Activities.Execute(initial, new() { NodeId = "recovery", EntryEffectOwner = RunActivityEffectOwner.PlayerEntity,
            EntryEffects = [Heal(10)] }, RunActivityBoundary.Entry);
        Assert.True(player.IsSuccess, player.IsFailure ? player.Error : null);
        Assert.Equal(47, Current(player.Value.State));
        Assert.Equal(50, player.Value.State.ResourceState.Current("resolve"));
        Assert.Single(player.Value.State.PlayerEntity!.Components.Values.OfType<ResourceEntityComponentState>());
        Assert.Equal("recovery", Assert.Single(player.Value.Steps).Calculation!.Channel);
        var wallet = fixture.Activities.Execute(initial, new() { NodeId = "recovery", EntryEffects = [Heal(5)] }, RunActivityBoundary.Entry);
        Assert.True(wallet.IsSuccess, wallet.IsFailure ? wallet.Error : null);
        Assert.Equal(55, wallet.Value.State.ResourceState.Current("resolve"));
        Assert.Equal(37, Current(wallet.Value.State));
        Assert.Equal(37, Current(initial));
        Assert.True(fixture.Activities.Execute(initial with { ActiveEncounterId = Guid.NewGuid() },
            new() { NodeId = "recovery", EntryEffectOwner = RunActivityEffectOwner.PlayerEntity, EntryEffects = [Heal(10)] }, RunActivityBoundary.Entry).IsFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaidRecoveryRejectsLastEffectOrPaymentWithoutPublishingPartialState(bool paymentFails)
    {
        var fixture = Fixture();
        var run = Prepared(paymentFails ? 100 : 5, paymentFails ? [Heal(10)] : [Heal(10), Heal(2) with { TargetResource = "missing" }]);
        var runtime = fixture.Factory.Create(new(GameplayPersistenceMode.Ephemeral));
        Assert.True(runtime.Runs.HydrateForReplay(run).IsSuccess);
        var before = CanonicalJson.ComputeHash(run);
        var result = runtime.Gateway.Execute(run.RunId, PreparationEnvelope(run));
        Assert.True(result.IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(runtime.Runs.GetRun(run.RunId).Value));
        Assert.Equal(37, Current(run));
        Assert.Equal(50, run.ResourceState.Current("resolve"));
    }

    [Fact]
    public void DurabilityFailureDoesNotPublishPaidRecovery()
    {
        var fixture = Fixture();
        var store = new Mock<IRunCommitStore>();
        store.Setup(item => item.AppendAsync(It.IsAny<RunCommit>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk unavailable"));
        var run = Prepared();
        var runtime = fixture.Factory.Create(new(GameplayPersistenceMode.Authoritative, store.Object));
        Assert.True(runtime.Runs.HydrateForReplay(run).IsSuccess);
        var result = runtime.Gateway.Execute(run.RunId, PreparationEnvelope(run));
        Assert.True(result.IsFailure);
        Assert.Contains("disk unavailable", result.Error);
        Assert.Equal(CanonicalJson.ComputeHash(run), CanonicalJson.ComputeHash(runtime.Runs.GetRun(run.RunId).Value));
    }

    [Fact]
    public async Task PaidRecoverySurvivesRestartDuplicateReceiptAndTenSemanticReplaysWithoutTouchingParent()
    {
        await Durable(Prepared(), branch => PreparationEnvelope(branch), state =>
        {
            Assert.Equal(47, Current(state));
            Assert.Equal(45, state.ResourceState.Current("resolve"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EncounterResolutionPromotesOnceWithIndependentRetryPolicyAndReplays(bool retry)
    {
        var parent = Prepared() with { Preparations = [], CurrentNodeId = "fight",
            Map = new() { Nodes = [new() { NodeId = "fight", Activity = new() { Type = RunActivityType.Encounter }, NextNodeIds = ["rest"] },
                new() { NodeId = "rest", Activity = new() { Type = RunActivityType.Preparation, DefinitionId = "recovery" } }], VisitedNodeIds = ["fight"] } };
        var combatId = DeterministicId.Create(123, 5, "encounter");
        var combat = new CombatState { CombatId = combatId, RunId = parent.RunId, RunNodeId = "fight",
            Actors = new Dictionary<string, CombatActorState> { ["hero"] = ActorResourceLifecycleTests.Actor(9) },
            Status = retry ? CombatStatus.DEFEAT : CombatStatus.VICTORY, Determinism = parent.Determinism };
        parent = parent with { ActiveEncounterId = combatId, Encounters = [new() { NodeId = "fight", Combat = combat }],
            ResolvedMode = parent.ResolvedMode! with
            { ActorResourceLifecyclePolicy = ActorResourceLifecycleTests.Policy() with
                { Rules = [ActorResourceLifecycleTests.Rule() with { Retry = ActorResourceLifecycleAction.EncounterOnly }] },
                ProgressionPolicy = new() { AllowOutOfActivityCommands = true, EncounterRetry = retry ? RunEncounterRetryPolicy.RestartActivity : RunEncounterRetryPolicy.Disabled,
                    RetryableEncounterOutcomes = retry ? [CombatStatus.DEFEAT] : [] } } };
        await Durable(parent, branch => Envelope(branch, RunCommandTypes.ResolveCombat, new ResolveCombatCommand(branch.ActiveEncounterId!.Value)), state =>
        {
            Assert.Null(state.ActiveEncounterId);
            Assert.True(state.Encounters[0].Resolved);
            Assert.Equal(retry ? 37 : 9, Current(state));
            Assert.Equal(50, state.ResourceState.Current("resolve"));
            Assert.Equal(retry ? [] : new[] { "fight" }, state.Map.ResolvedNodeIds);
            var next = PersistentPlayerTransitions.Materialize(state, combat with { Status = CombatStatus.ACTIVE });
            Assert.True(next.IsSuccess, next.IsFailure ? next.Error : null);
            Assert.Equal(retry ? 37 : 9, next.Value.GetActor("hero")!.ResourceState.Current("resolve"));
        });
    }

    [Fact]
    public void DefeatIsOwnedByArbitraryResourceDefinitionAndEvaluatedOnlyOutsideEncounter()
    {
        var run = Prepared();
        var component = run.PlayerEntity!.Component<ResourceEntityComponentState>()!;
        var pool = component.State.Get("resolve")!;
        pool = pool with { Current = 0, Definition = pool.Definition with { ThresholdPolicies = [new()
            { PolicyId = "exhausted", Comparison = ResourceThresholdComparison.LessThanOrEqual,
                ThresholdSource = ResourceThresholdSource.Minimum, Consequence = ResourceThresholdConsequence.DefeatOwner }] } };
        run = run with { PlayerEntity = run.PlayerEntity with { Components = run.PlayerEntity.Components.ToImmutableDictionary()
            .SetItem(component.ComponentId, component with { State = component.State with { Resources = new Dictionary<string, ResourcePool> { ["resolve"] = pool } } }) } };
        Assert.Equal(RunLifecycleState.Failed, PersistentPlayerTransitions.ApplyResourceConsequences(run).Lifecycle);
        Assert.Equal(RunLifecycleState.Active, PersistentPlayerTransitions.ApplyResourceConsequences(run with { ActiveEncounterId = Guid.NewGuid() }).Lifecycle);
        Assert.Equal(RunLifecycleState.Active, run.Lifecycle);
    }

    [Fact]
    public void EncounterExitRecoveryTargetsPromotedValueAndLastFailureKeepsEncounterOpen()
    {
        var fixture = Fixture();
        var initial = Prepared();
        var combatId = DeterministicId.Create(123, 7, "exit");
        var fight = new RunMapNodeState { NodeId = "fight", Activity = new() { Type = RunActivityType.Encounter }, NextNodeIds = ["rest"],
            ExitEffectOwner = RunActivityEffectOwner.PlayerEntity, ExitEffects = [Heal(7)] };
        var combat = new CombatState { CombatId = combatId, RunId = initial.RunId, RunNodeId = "fight", Status = CombatStatus.VICTORY,
            Determinism = initial.Determinism, Actors = new Dictionary<string, CombatActorState> { ["hero"] = ActorResourceLifecycleTests.Actor(9) } };
        var run = initial with { ActiveEncounterId = combatId, CurrentNodeId = "fight", Encounters = [new() { NodeId = "fight", Combat = combat }],
            Map = new() { Nodes = [fight, initial.Map.Nodes[0]], VisitedNodeIds = ["fight"] } };
        var failed = run with { Map = run.Map with { Nodes = [fight with { ExitEffects = [Heal(7), Heal(1) with { TargetResource = "missing" }] }, initial.Map.Nodes[0]] } };
        var runtime = fixture.Factory.Create(new(GameplayPersistenceMode.Ephemeral));
        Assert.True(runtime.Runs.HydrateForReplay(failed).IsSuccess);
        var rejected = runtime.Gateway.Execute(failed.RunId, Envelope(failed, RunCommandTypes.ResolveCombat, new ResolveCombatCommand(combatId)), combatId);
        Assert.True(rejected.IsFailure);
        Assert.Equal(CanonicalJson.ComputeHash(failed), CanonicalJson.ComputeHash(runtime.Runs.GetRun(failed.RunId).Value));
        Assert.True(runtime.Runs.HydrateForReplay(run).IsSuccess);
        var accepted = runtime.Gateway.Execute(run.RunId, Envelope(run, RunCommandTypes.ResolveCombat, new ResolveCombatCommand(combatId)), combatId);
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error : null);
        Assert.Equal(16, Current(accepted.Value.Receipt.State));
        Assert.Null(accepted.Value.Receipt.State.ActiveEncounterId);
        Assert.Equal(50, accepted.Value.Receipt.State.ResourceState.Current("resolve"));
        Assert.Equal(37, Current(run));
    }

    private static async Task Durable(RunState parent, Func<RunState, GameplayCommandEnvelope> command, Action<RunState> validate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "heroscript-actor-resources-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = Fixture();
            var branchCommand = new RunBranchStartCommand(parent.RunId, parent.Sequence, "resources", CanonicalJson.ComputeHash(parent), parent.ActiveEncounterId);
            var branch = RunBranchTransitions.Create(parent, branchCommand).Value;
            var envelope = command(branch);
            string hash;
            using (var store = new FileRunCommitStore(directory, NullLogger.Instance))
            {
                await store.AppendAsync(Initial(parent, "TEST", new { }));
                await store.AppendAsync(Initial(branch, RunCommandTypes.CreateBranchFromHistory, branchCommand, parent.Determinism.Step));
                var live = fixture.Factory.Create(new(GameplayPersistenceMode.Authoritative, store));
                Assert.True(live.Runs.HydrateForReplay(branch).IsSuccess);
                var result = live.Gateway.Execute(branch.RunId, envelope, branch.ActiveEncounterId);
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
                validate(result.Value.Receipt.State);
                hash = result.Value.Receipt.StateHash;
                var duplicate = live.Gateway.Execute(branch.RunId, envelope, branch.ActiveEncounterId);
                Assert.True(duplicate.IsSuccess, duplicate.IsFailure ? duplicate.Error : null);
                Assert.True(duplicate.Value.Receipt.Duplicate);
                Assert.Equal(hash, duplicate.Value.Receipt.StateHash);
            }
            using var restarted = new FileRunCommitStore(directory, NullLogger.Instance);
            var restored = (await restarted.LoadLatestStateAsync(branch.RunId))!;
            validate(restored);
            Assert.Equal(hash, CanonicalJson.ComputeHash(restored));
            var retry = fixture.Factory.Create(new(GameplayPersistenceMode.Authoritative, restarted)).Gateway.Execute(branch.RunId, envelope, branch.ActiveEncounterId);
            Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error : null);
            Assert.True(retry.Value.Receipt.Duplicate);
            var replays = new RunSemanticReplayService(restarted, fixture.Factory);
            for (var repeat = 0; repeat < 10; repeat++)
            {
                var replay = await replays.VerifyAsync(branch.RunId);
                Assert.True(replay.IsValid, string.Join("; ", replay.Errors));
                Assert.Equal(hash, replay.ActualFinalHash);
            }
            Assert.Single(await restarted.LoadCommitsAsync(parent.RunId));
            Assert.Equal(37, Current(parent));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static RunState Prepared(float cost = 5, EffectDefinition[]? effects = null)
    {
        var runId = DeterministicId.Create(123, 1, "resources");
        var run = new RunState { RunId = runId, PlayerEntityId = "hero", PlayerEntity = ActorResourceLifecycleTests.Player(), ConfigName = "test",
            CurrentNodeId = "rest", Lineage = RunLineage.Root(runId), Determinism = DeterministicContext.Create(123, "resources"),
            ResourceState = new() { OwnerId = "wallet", Resources = new Dictionary<string, ResourcePool>
            { ["resolve"] = ResourcePool.Materialize(ActorResourceLifecycleTests.Definition(), 50, 60) } },
            ResolvedMode = new() { Definition = new() { CalculationPipelineIds = ["recovery"] },
                CombatRules = new() { Flow = new() { EncounterResolution = new() { Strategy = EncounterResolutionStrategy.ManualAck } } },
                ActorResourceLifecyclePolicy = ActorResourceLifecycleTests.Policy(), ProgressionPolicy = new() { AllowOutOfActivityCommands = true },
                CardZoneSystem = new() { CardZoneSystemId = "test-zones", Zones = [new() { ZoneId = "draw", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }] } },
            Map = new() { Nodes = [new() { NodeId = "rest", Activity = new() { Type = RunActivityType.Preparation, DefinitionId = "recovery" } }] } };
        run = PreparationTransitions.Create(run, new() { PreparationId = "recovery", Options = [new()
            { OptionId = "recover", Costs = [new() { ResourceId = "resolve", Amount = cost }], EffectOwner = RunActivityEffectOwner.PlayerEntity,
                Effects = (effects ?? [Heal(10)]).ToImmutableArray() }] }, "rest").State;
        var zones = CardZoneTransitions.CreateEmpty([new() { ZoneId = "draw", OwnerId = "$run" }]);
        return run with { Sequence = 1, Deck = new() { Topology = zones.Value } };
    }

    private static float Current(RunState run) => run.PlayerEntity!.Component<ResourceEntityComponentState>()!.State.Current("resolve");
    private static EffectDefinition Heal(float amount) => new() { EffectId = "recover", Type = EffectType.HEAL, Target = EffectTarget.SELF,
        TargetResource = "resolve", Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = amount, UnitId = "points", Channel = "recovery", PipelineId = "recovery" }] };
    private static GameplayCommandEnvelope PreparationEnvelope(RunState run) => Envelope(run, RunCommandTypes.ApplyPreparationOption, new PreparationCommand(run.Preparations[0].PreparationInstanceId, "recover"));
    private static GameplayCommandEnvelope Envelope(RunState run, string type, object payload) => new(new(DeterministicId.Create(123, 4, type), type, run.Sequence,
        type == RunCommandTypes.ResolveCombat ? run.GetActiveEncounter()!.Combat.Determinism.Step : run.Determinism.Step), JsonSerializer.SerializeToElement(payload));

    private static (RunActivityEffectExecutor Activities, GameplayRuntimeFactory Factory) Fixture()
    {
        var content = ActorResourceLifecycleTests.Runtime();
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve("resources", "test")).Returns(Result<ContentRuntime>.Success(content));
        var formulas = new RuntimeFormulaEvaluator(Mock.Of<IMathEngine>(), new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance);
        var executor = new EffectTriggerExecutor(formulas, new ImmutableEffectProcessor(), runtimes.Object, new CalculationEngine(formulas), new CompositeCalculationInfluenceProvider([]));
        var manifests = new Mock<IContentManifestProvider>();
        manifests.Setup(service => service.GetByRevision("resources")).Returns(Result<ContentManifest>.Success(content.Manifest));
        var factory = new GameplayRuntimeFactory(Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>(), Mock.Of<ICardPoolResolver>(), Mock.Of<ICardContentCatalog>(),
            Mock.Of<IPinnedContentCatalog<ScriptModifierDefinition>>(), manifests.Object, Mock.Of<IResourceCatalog<GameModeDefinition>>(), Mock.Of<IGameModeResolver>(),
            Mock.Of<IContentPublicationService>(), runtimes.Object, Mock.Of<IResourceManager>(), Mock.Of<ICombatFactory>(), Mock.Of<ICombatFlowPlanner>(),
            Mock.Of<ICombatCommandHandler>(), Mock.Of<IAutomaticFlowDriver>(), executor, new GameEventContextAccessor(), GameplayCommandCodec.CreateDefault(), new CardZoneFlowExecutor());
        return (new(executor), factory);
    }

    private static RunCommit Initial(RunState state, string type, object command, ulong beforeStep = 0)
    {
        var payload = JsonSerializer.SerializeToElement(command);
        var hash = CanonicalJson.ComputeHash(state);
        var frames = new[] { new RunCommitFrame { FrameIndex = 0, Step = state.Determinism.Step, Kind = type, ResultHash = hash, Resolution = payload } };
        return new() { RunId = state.RunId, Sequence = 1, RootCommand = new(DeterministicId.Create(123, 0, type), type, 0, beforeStep, CanonicalJson.ComputeHash(payload)),
            Command = payload, StateHash = hash, StateAfter = state, Lineage = state.Lineage, BeforeStep = beforeStep, AfterStep = state.Determinism.Step,
            LogicalTimestamp = state.Determinism.LogicalTimestamp.UtcDateTime, Frames = frames, Facts = RunCommitFacts.FromFrames(frames) };
    }
}
