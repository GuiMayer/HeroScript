using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Calculations;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Entity;
using Core.Entity.Definitions;
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
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class PersistentPlayerAttributeTests
{
    [Theory]
    [InlineData(AttributeOperation.Add, 3, 5)]
    [InlineData(AttributeOperation.Multiply, 3, 6)]
    [InlineData(AttributeOperation.Set, 3, 3)]
    public void TypedOperationsAreImmutableAuthorizedAndBounded(AttributeOperation operation, float amount, float expected)
    {
        var player = Player();
        var before = CanonicalJson.ComputeHash(player);
        var mutation = new AttributeMutationDefinition { ComponentId = "stats", ValueId = "power", Operation = operation };
        var changed = EntityAttributeTransitions.Apply(player, mutation, amount);
        Assert.True(changed.IsSuccess, changed.IsFailure ? changed.Error : null);
        Assert.Equal(expected, Power(changed.Value.State));
        Assert.Equal(before, CanonicalJson.ComputeHash(player));
        Assert.True(EntityAttributeTransitions.Apply(player, mutation, 100).IsFailure);
        Assert.True(EntityAttributeTransitions.Apply(player, mutation with { ValueId = "unknown" }, amount).IsFailure);
        Assert.True(EntityAttributeTransitions.Apply(player, mutation with { ComponentId = "unknown" }, amount).IsFailure);
        Assert.True(EntityAttributeTransitions.Apply(player, mutation, float.NaN).IsFailure);
    }

    [Fact]
    public void AttributeRulesFailPublicationForUnknownValuesOrInvalidBounds()
    {
        var definition = Definition();
        var stats = definition.Component<StatEntityComponentDefinition>()!;
        Assert.True(EntityDefinitionValidator.Validate(definition).IsSuccess);
        var bad = stats with { ValueRules = stats.ValueRules.SetItem("absent", Rule()) };
        Assert.True(EntityDefinitionValidator.Validate(definition with { Components = [bad] }).IsFailure);
        bad = stats with { ValueRules = stats.ValueRules.SetItem("power", Rule() with { Maximum = 1 }) };
        Assert.True(EntityDefinitionValidator.Validate(definition with { Components = [bad] }).IsFailure);
        bad = stats with { ValueRules = stats.ValueRules.SetItem("power", Rule() with { Minimum = float.NaN }) };
        Assert.True(EntityDefinitionValidator.Validate(definition with { Components = [bad] }).IsFailure);
    }

    [Fact]
    public void PreparationSnapshotsCapturePublishedEffectsAndExecuteThroughTheCommonProcessor()
    {
        var fixture = Fixture();
        var option = new PreparationDefinition { PreparationId = "training", Options = [new() { OptionId = "power", Effects = [Mutation(3)] }] };
        var offered = PreparationTransitions.Create(State(), option);
        Assert.Equal(3, offered.Value.Options[0].Effects[0].Parameters[0].FlatValue);
        var execution = fixture.Activities.Execute(offered.State, new() { NodeId = "training", EntryEffects = offered.Value.Options[0].Effects }, RunActivityBoundary.Entry);
        Assert.True(execution.IsSuccess, execution.IsFailure ? execution.Error : null);
        Assert.Equal(5, Power(execution.Value.State.PlayerEntity!));
        Assert.Equal(2, Power(offered.State.PlayerEntity!));
        var step = Assert.Single(execution.Value.Steps);
        Assert.Equal("attribute_points", step.Calculation!.Quantity.UnitId);
        Assert.Equal(AttributeLifetime.RunBase, Assert.Single(step.Applications).AttributeOutcome!.Lifetime);
        Assert.NotEqual(step.RunBeforeHash, step.RunAfterHash);
    }

    [Fact]
    public void LastFailureDiscardsPersistentChangesAndTemporaryAttributeCannotLeakFromRunBoundary()
    {
        var fixture = Fixture();
        var run = State();
        var hash = CanonicalJson.ComputeHash(run);
        Assert.True(fixture.Activities.Execute(run, new() { NodeId = "training", EntryEffects = [Mutation(3), Mutation(100)] }, RunActivityBoundary.Entry).IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
        Assert.True(fixture.Activities.Execute(run, new() { NodeId = "training", EntryEffects = [Mutation(3) with
            { AttributeMutation = Mutation(3).AttributeMutation! with { Lifetime = AttributeLifetime.Encounter } }] }, RunActivityBoundary.Entry).IsFailure);
        Assert.True(fixture.Activities.Execute(run, new() { NodeId = "training", EntryEffects = [Mutation(3) with
            { ChainedEffects = [Mutation(3) with { AttributeMutation = Mutation(3).AttributeMutation! with { Lifetime = AttributeLifetime.Encounter } }] }] }, RunActivityBoundary.Entry).IsFailure);
    }

    [Fact]
    public void EncounterOnlyAttributesNeverOverwritePersistentBaseAndNextEncounterUsesInvestment()
    {
        var fixture = Fixture();
        var trained = fixture.Activities.Execute(State(), new() { NodeId = "training", EntryEffects = [Mutation(3)] }, RunActivityBoundary.Entry).Value.State;
        var fresh = Combat();
        var first = PersistentPlayerTransitions.Materialize(trained, fresh);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(5, Power(first.Value.GetActor("hero")!));
        var transient = Mutation(2) with { AttributeMutation = Mutation(2).AttributeMutation! with { Lifetime = AttributeLifetime.Encounter } };
        var result = fixture.Executor.Execute(EffectTransactionTests.Request(transient) with { Combat = first.Value, Run = trained, ContentRevision = "attributes" });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(7, Power(result.Value.State.GetActor("hero")!));
        Assert.Equal(5, Power(result.Value.Run!.PlayerEntity!));
        var next = PersistentPlayerTransitions.Materialize(result.Value.Run!, fresh).Value;
        Assert.Equal(5, Power(next.GetActor("hero")!));
        Assert.True(fixture.Executor.Execute(EffectTransactionTests.Request(Mutation(2)) with
            { Combat = first.Value, Run = trained, ContentRevision = "attributes" }).IsFailure);
    }

    [Fact]
    public void EligibleCalculationsReadPersistedStatsAndOtherTagsRemainUnchanged()
    {
        var fixture = Fixture();
        var trained = fixture.Activities.Execute(State(), new() { NodeId = "training", EntryEffects = [Mutation(3)] }, RunActivityBoundary.Entry).Value.State;
        var combat = PersistentPlayerTransitions.Materialize(trained, Combat()).Value;
        foreach (var tag in new[] { "strike", "spell", "utility" })
        {
            var effects = new EffectDefinition { Type = EffectType.DAMAGE, TargetResource = "focus", Tags = [tag],
                Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = 1, Channel = "magnitude", PipelineId = "magnitude", UnitId = "points" }] };
            var result = fixture.Executor.Execute(EffectTransactionTests.Request(effects) with
            { Combat = combat, Run = trained, ContentRevision = "attributes" });
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(tag == "utility" ? 1 : 6, result.Value.Steps[0].Calculation!.Value);
            Assert.Equal(5, Power(trained.PlayerEntity!));
        }
    }

    [Fact]
    public void TemporaryBuffExpiryDoesNotEraseThePersistentInvestment()
    {
        var fixture = Fixture();
        var trained = fixture.Activities.Execute(State(), new() { NodeId = "training", EntryEffects = [Mutation(3)] }, RunActivityBoundary.Entry).Value.State;
        var buff = ModifierTransitions.Apply(trained, new() { ModifierId = "temporary", DefaultDuration = 1, DurationBoundary = ModifierDurationBoundary.Command,
            Influences = [new() { InfluenceId = "power", Channel = "magnitude", Bucket = "power", Value = 2 }] },
            new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }, "test").Value.Run;
        var combat = PersistentPlayerTransitions.Materialize(buff, Combat()).Value;
        var effect = new EffectDefinition { Type = EffectType.DAMAGE, TargetResource = "focus", Tags = ["strike"],
            Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = 1, Channel = "magnitude", PipelineId = "magnitude", UnitId = "points" }] };
        var active = fixture.Executor.Execute(EffectTransactionTests.Request(effect) with { Combat = combat, Run = buff, ContentRevision = "attributes" });
        Assert.True(active.IsSuccess, active.IsFailure ? active.Error : null);
        Assert.Equal(8, active.Value.Steps[0].Calculation!.Value);
        var expired = ModifierTransitions.Tick(buff, ModifierDurationBoundary.Command, combat);
        var after = fixture.Executor.Execute(EffectTransactionTests.Request(effect) with { Combat = combat, Run = expired, ContentRevision = "attributes" });
        Assert.True(after.IsSuccess, after.IsFailure ? after.Error : null);
        Assert.Equal(6, after.Value.Steps[0].Calculation!.Value);
        Assert.Equal(5, Power(expired.PlayerEntity!));
    }

    [Fact]
    public void ContentRebindPreservesValuesRejectsSchemaOrBoundsChangesAndIdentityMismatch()
    {
        var player = EntityAttributeTransitions.Apply(Player(), Mutation(3).AttributeMutation!, 3).Value.State;
        var next = Content(Definition() with { Components = [Definition().Component<StatEntityComponentDefinition>()! with
            { Values = new Dictionary<string, float> { ["power"] = 1 } }] }, "next");
        var rebound = PersistentPlayerTransitions.Rebind(player, next);
        Assert.True(rebound.IsSuccess);
        Assert.Equal(5, Power(rebound.Value));
        Assert.Equal("next", rebound.Value.ContentRevision);
        var stats = Definition().Component<StatEntityComponentDefinition>()!;
        var bad = Content(Definition() with { Components = [stats with { ValueRules = stats.ValueRules.SetItem("power", Rule() with { Maximum = 4 }) }] });
        Assert.True(PersistentPlayerTransitions.Rebind(player, bad).IsFailure);
        bad = Content(Definition() with { Components = [stats with { Values = new Dictionary<string, float> { ["other"] = 2 }, ValueRules = ImmutableSortedDictionary<string, AttributeValueRule>.Empty }] });
        Assert.True(PersistentPlayerTransitions.Rebind(player, bad).IsFailure);
        Assert.True(PersistentPlayerTransitions.Materialize(State() with { PlayerEntity = player with { DefinitionId = "other" } }, Combat()).IsFailure);
        var restored = JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(State() with { PlayerEntity = player }))!;
        Assert.Equal(CanonicalJson.ComputeHash(State() with { PlayerEntity = player }), CanonicalJson.ComputeHash(restored));
    }

    [Fact]
    public async Task DurablePreparationProgressionSurvivesRestartRetryTenSemanticReplaysAndIsolatedFork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "heroscript-attributes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = Fixture();
            var preparation = PreparationTransitions.Create(State(), new() { PreparationId = "training", Options = [new() { OptionId = "power", Effects = [Mutation(3)] }] });
            var parent = preparation.State with { Sequence = 1, CurrentNodeId = "training", Map = new() { Nodes = [new()
                { NodeId = "training", Activity = new() { Type = RunActivityType.Preparation, DefinitionId = "training" } }] } };
            var branchCommand = new RunBranchStartCommand(parent.RunId, parent.Sequence, "trained", CanonicalJson.ComputeHash(parent));
            var branch = RunBranchTransitions.Create(parent, branchCommand).Value;
            var payload = JsonSerializer.SerializeToElement(new PreparationCommand(preparation.Value.PreparationInstanceId, "power"));
            var envelope = new GameplayCommandEnvelope(new(DeterministicId.Create(123, 1, "training"), RunCommandTypes.ApplyPreparationOption,
                branch.Sequence, branch.Determinism.Step), payload);
            string hash;
            using (var store = new FileRunCommitStore(directory, NullLogger.Instance))
            {
                await store.AppendAsync(Initial(parent, "TEST", new { }));
                await store.AppendAsync(Initial(branch, RunCommandTypes.CreateBranchFromHistory, branchCommand, parent.Determinism.Step));
                var live = fixture.Factory.Create(new(GameplayPersistenceMode.Authoritative, store));
                Assert.True(live.Runs.HydrateForReplay(branch).IsSuccess);
                var applied = live.Gateway.Execute(branch.RunId, envelope);
                Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
                hash = applied.Value.Receipt.StateHash;
                Assert.Equal(5, Power(applied.Value.Receipt.State.PlayerEntity!));
                Assert.Equal(2, Power(parent.PlayerEntity!));
            }
            using var restarted = new FileRunCommitStore(directory, NullLogger.Instance);
            Assert.Equal(hash, CanonicalJson.ComputeHash((await restarted.LoadLatestStateAsync(branch.RunId))!));
            var retry = fixture.Factory.Create(new(GameplayPersistenceMode.Authoritative, restarted)).Gateway.Execute(branch.RunId, envelope);
            Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error : null);
            Assert.True(retry.Value.Receipt.Duplicate);
            var replays = new RunSemanticReplayService(restarted, fixture.Factory);
            for (var repeat = 0; repeat < 10; repeat++)
            {
                var replay = await replays.VerifyAsync(branch.RunId);
                Assert.True(replay.IsValid, string.Join("; ", replay.Errors));
                Assert.True(replay.Reexecuted);
                Assert.Equal(hash, replay.ActualFinalHash);
            }
            Assert.Single(await restarted.LoadCommitsAsync(parent.RunId));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static AttributeValueRule Rule() => new() { Minimum = 0, Maximum = 10, AllowedOperations = [AttributeOperation.Add, AttributeOperation.Multiply, AttributeOperation.Set] };
    private static EntityDefinition Definition() => new() { DefinitionId = "player", DisplayName = "Player", Components = [new StatEntityComponentDefinition
        { ComponentId = "stats", Values = new Dictionary<string, float> { ["power"] = 2 }, ValueRules = ImmutableSortedDictionary<string, AttributeValueRule>.Empty.Add("power", Rule()) }] };
    private static EntityState Player() => PersistentPlayerTransitions.Create("hero", "player", Content()).Value;
    private static float Power(EntityState player) => player.Component<StatEntityComponentState>("stats")!.Values["power"];
    private static EffectDefinition Mutation(float amount) => new() { Type = EffectType.MODIFY_ATTRIBUTE, Target = EffectTarget.SELF,
        AttributeMutation = new() { ComponentId = "stats", ValueId = "power", Operation = AttributeOperation.Add, Lifetime = AttributeLifetime.RunBase },
        Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = amount, UnitId = "attribute_points", Channel = "attributes", PipelineId = "attributes" }] };
    private static RunState State() => new() { RunId = DeterministicId.Create(123, 1, "attributes"), PlayerEntityId = "hero", PlayerEntity = Player(),
        Lineage = RunLineage.Root(DeterministicId.Create(123, 1, "attributes")),
        ConfigName = "test", Determinism = DeterministicContext.Create(123, "attributes"), ResolvedMode = new()
        { Definition = new() { CalculationPipelineIds = ["attributes", "magnitude"] }, ProgressionPolicy = new() { AllowOutOfActivityCommands = true } } };
    private static CombatState Combat()
    {
        var combat = GameplayOwnershipTests.State() with { Determinism = DeterministicContext.Create(123, "attributes") };
        return combat with { Actors = combat.Actors.ToDictionary(pair => pair.Key, pair => pair.Value with
        { ContentRevision = "attributes", DefinitionId = pair.Key == "hero" ? "player" : "enemy" }) };
    }
    private static ContentRuntime Content(EntityDefinition? player = null, string revision = "attributes")
    {
        var artifacts = new Dictionary<string, JsonElement>
        {
            ["entities/catalog.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, EntityDefinition> { ["player"] = player ?? Definition() }),
            ["calculation-pipelines/catalog.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, CalculationPipelineDefinition>
            { ["attributes"] = new() { PipelineId = "attributes", Channel = "attributes", UnitId = "attribute_points", Buckets = [new() { BucketId = "base" }] },
                ["magnitude"] = new() { PipelineId = "magnitude", Channel = "magnitude", UnitId = "points", Buckets = [new() { BucketId = "power" }],
                    StatInfluenceBindings = [new() { BindingId = "strike", ComponentId = "stats", ValueId = "power", Channel = "magnitude", Bucket = "power", RequiredTags = ["strike"] },
                        new() { BindingId = "spell", ComponentId = "stats", ValueId = "power", Channel = "magnitude", Bucket = "power", RequiredTags = ["spell"] }] } })
        };
        return ContentRuntime.Create(new() { Manifest = new() { ConfigName = "test", Revision = revision, Artifacts = artifacts.Select(item => new ContentArtifactManifest
        { Kind = item.Key.Split('/')[0], Path = item.Key, DefinitionCount = item.Value.EnumerateObject().Count() }).ToArray() }, Artifacts = artifacts.ToImmutableDictionary() }).Value;
    }
    private static (EffectTriggerExecutor Executor, RunActivityEffectExecutor Activities, GameplayRuntimeFactory Factory) Fixture()
    {
        var content = Content();
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve("attributes", "test")).Returns(Result<ContentRuntime>.Success(content));
        var formulas = new RuntimeFormulaEvaluator(Mock.Of<IMathEngine>(), new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance);
        var executor = new EffectTriggerExecutor(formulas, new ImmutableEffectProcessor(), runtimes.Object, new CalculationEngine(formulas),
            new CompositeCalculationInfluenceProvider([new EntityStatInfluenceProvider(), new RunModifierInfluenceProvider(formulas)]));
        var manifests = new Mock<IContentManifestProvider>();
        manifests.Setup(service => service.GetByRevision("attributes")).Returns(Result<ContentManifest>.Success(content.Manifest));
        var factory = new GameplayRuntimeFactory(Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>(), Mock.Of<ICardPoolResolver>(), Mock.Of<ICardContentCatalog>(),
            Mock.Of<IPinnedContentCatalog<ScriptModifierDefinition>>(), manifests.Object, Mock.Of<IResourceCatalog<GameModeDefinition>>(), Mock.Of<IGameModeResolver>(),
            Mock.Of<IContentPublicationService>(), runtimes.Object, Mock.Of<IResourceManager>(), Mock.Of<ICombatFactory>(), Mock.Of<ICombatFlowPlanner>(),
            Mock.Of<ICombatCommandHandler>(), Mock.Of<IAutomaticFlowDriver>(), executor, new GameEventContextAccessor(), GameplayCommandCodec.CreateDefault());
        return (executor, new(executor), factory);
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
