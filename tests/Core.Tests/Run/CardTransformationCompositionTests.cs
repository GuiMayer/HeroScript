using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Events;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Resources;
using Core.Run;
using Core.Run.Branching;
using Core.Run.Content;
using Core.Run.Replay;
using Core.Run.Runtime;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed partial class CardTransformationCompositionTests
{
    private const string Revision = "revision-b";
    private static readonly Guid CardId = Guid.Parse("10000000-0000-8000-8000-000000000006");
    private static readonly Guid RunId = Guid.Parse("20000000-0000-8000-8000-000000000006");

    [Fact]
    public void Bundles_NamespaceLocalBindingsAliasesAndResultFormulas()
    {
        var bundle = new CardComponentBundleDefinition
        {
            BundleId = "pulse", Components =
            [
                Impact(3) with { Effect = Impact(3).Effect with { OutputId = "hit", ChainedEffects =
                    [new() { Type = EffectType.MODIFY_RESOURCE, TargetResource = "health", FormulaValue = "results.hit.target.last.applied_change + source_stat_other" }] } },
                new CardEffectComponentDefinition { ComponentId = "residual", Effect = new()
                {
                    Type = EffectType.APPLY_STATUS, StatusId = "burn", PayloadBindings =
                    [new() { ParameterId = "potency", CardEffectComponentId = "impact" }]
                } }
            ]
        };
        var first = CardBundleCompiler.Expand(bundle, "fire");
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        var effect = Assert.IsType<CardEffectComponentDefinition>(first.Value[0]).Effect;
        Assert.Equal("fire__hit", effect.OutputId);
        Assert.Equal("results.fire__hit.target.last.applied_change + source_stat_other", effect.ChainedEffects![0].FormulaValue);
        Assert.Equal("fire.impact", Assert.IsType<CardEffectComponentDefinition>(first.Value[1]).Effect.PayloadBindings[0].CardEffectComponentId);
        var second = CardBundleCompiler.Expand(bundle, "ice").Value;
        var compiled = new CardContentCompiler().Compile(new() { CardId = "strike", Components = first.Value.Concat(second).ToArray() });
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        Assert.Equal("hit", Assert.IsType<CardEffectComponentDefinition>(bundle.Components[0]).Effect.OutputId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad.scope")]
    [InlineData("1bad")]
    [InlineData("bad-scope")]
    public void Bundles_RejectUnsafeNamespace(string scope) =>
        Assert.True(CardBundleCompiler.Expand(Bundle(3), scope).IsFailure);

    [Fact]
    public void Compiler_RejectsDuplicateNamespaceAndMemberCollision()
    {
        var card = Base() with { ComponentBundles = [new("pulse", "fire"), new("pulse", "fire")] };
        Assert.True(new CardContentCompiler().Compile(card, new Dictionary<string, CardComponentBundleDefinition> { ["pulse"] = Bundle(3) }).IsFailure);
        card = Base() with { ComponentBundles = [new("pulse", "fire")], Components = [Impact(6), Impact(9) with { ComponentId = "fire.impact" }] };
        Assert.True(new CardContentCompiler().Compile(card, new Dictionary<string, CardComponentBundleDefinition> { ["pulse"] = Bundle(3) }).IsFailure);
    }

    [Fact]
    public void BundleSnapshots_DoNotFollowLaterCatalogChanges()
    {
        var upgrade = BundleUpgrade("pulse", CardComponentPatchOperation.Add, "pulse");
        var oldRuntime = Runtime([upgrade], Bundle(3));
        var changed = Runtime([upgrade], Bundle(90));
        var run = State();
        var planned = new CardTransformationPlanner(oldRuntime).Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "pulse");
        Assert.True(planned.IsSuccess, planned.IsFailure ? planned.Error : null);
        var snapshot = Assert.IsType<CardBundleSnapshotPatchDefinition>(Assert.Single(planned.Value.Upgrades).Patches[0]);
        Assert.Equal("behavior.impact", snapshot.Components[0].ComponentId);
        var effective = Resolve(planned.Value, changed);
        Assert.Equal(3, effective.All<CardEffectComponentDefinition>().Single(item => item.ComponentId == "behavior.impact").Effect.FlatValue);
        Assert.Equal(Revision, planned.Value.Upgrades[0].ContentRevision);
    }

    [Fact]
    public void BundleOperations_ReplaceAndRemoveBaseNamespace()
    {
        var replace = BundleUpgrade("replace", CardComponentPatchOperation.Replace, "pulse");
        var remove = BundleUpgrade("remove", CardComponentPatchOperation.Remove, null);
        var runtime = Runtime([replace, remove], Bundle(8), Base() with { ComponentBundles = [new("pulse", "behavior")] });
        var run = State();
        var planner = new CardTransformationPlanner(runtime);
        var replaced = planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "replace");
        Assert.True(replaced.IsSuccess, replaced.IsFailure ? replaced.Error : null);
        Assert.Equal(2, Resolve(replaced.Value, runtime).Components.Count);
        run = WithCard(run, replaced.Value);
        var removed = planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "remove");
        Assert.True(removed.IsSuccess, removed.IsFailure ? removed.Error : null);
        Assert.Single(Resolve(removed.Value, runtime).Components);
        Assert.Equal(2, removed.Value.Upgrades.Count);
    }

    [Fact]
    public void BundleAddCollision_IsNotOfferedAndDoesNotMutateState()
    {
        var upgrade = BundleUpgrade("pulse", CardComponentPatchOperation.Add, "pulse") with { MaxApplications = 2 };
        var runtime = Runtime([upgrade], Bundle(3));
        var planner = new CardTransformationPlanner(runtime);
        var run = State();
        var first = planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "pulse").Value;
        run = WithCard(run, first);
        var hash = CanonicalJson.ComputeHash(run);
        Assert.True(planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "pulse").IsFailure);
        Assert.DoesNotContain(planner.Options(run).Value, option => option.Operation == CardTransformationOperation.Apply);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
    }

    [Theory]
    [InlineData(0, CardTransformationCategory.Behavior, "behavior")]
    [InlineData(1, CardTransformationCategory.Affinity, "behavior")]
    [InlineData(1, CardTransformationCategory.Behavior, "missing")]
    public void Slots_RejectInvalidCapacityCategoryOrAddress(int capacity, CardTransformationCategory category, string slotId)
    {
        var upgrade = Numeric("plus", 2) with { Category = category, SlotId = slotId };
        var runtime = Runtime([upgrade], card: Base() with
        {
            TransformationSlots = [new() { SlotId = "behavior", Capacity = capacity, AllowedCategories = [CardTransformationCategory.Behavior] }]
        });
        Assert.True(new CardTransformationPlanner(runtime).Plan(State(), CardId, CardTransformationOperation.Apply, upgradeId: "plus").IsFailure);
    }

    [Fact]
    public void Slots_ReplacementKeepsCapacityAndRemovalFreesIt()
    {
        var slot = new CardTransformationSlotDefinition { SlotId = "behavior", AllowedCategories = [CardTransformationCategory.Behavior] };
        var plus = Numeric("plus", 2) with { SlotId = "behavior", Category = CardTransformationCategory.Behavior };
        var more = plus with { UpgradeId = "more", Patches = Numeric("more", 4).Patches };
        var runtime = Runtime([plus, more], card: Base() with { TransformationSlots = [slot] });
        var planner = new CardTransformationPlanner(runtime);
        var run = WithCard(State(), planner.Plan(State(), CardId, CardTransformationOperation.Apply, upgradeId: "plus").Value);
        Assert.DoesNotContain(planner.Options(run).Value, option => option.Operation == CardTransformationOperation.Apply);
        var replaced = planner.Plan(run, CardId, CardTransformationOperation.Replace, 1, "more");
        Assert.True(replaced.IsSuccess, replaced.IsFailure ? replaced.Error : null);
        Assert.Equal(10, Resolve(replaced.Value, runtime).All<CardEffectComponentDefinition>()[0].Effect.FlatValue);
        run = WithCard(run, replaced.Value);
        Assert.True(planner.Plan(run, CardId, CardTransformationOperation.Remove, 1).IsFailure);
        var removed = planner.Plan(run, CardId, CardTransformationOperation.Remove, 2).Value;
        Assert.Equal("behavior", removed.Upgrades.Last().SlotId);
        Assert.Empty(Resolve(removed, runtime).AppliedUpgrades);
        Assert.Contains(planner.Options(WithCard(run, removed)).Value, option => option.Operation == CardTransformationOperation.Apply && option.UpgradeId == "plus");
    }

    [Fact]
    public void Discovery_UsesCompositionNotJustDefinitionAndMaxApplications()
    {
        var add = new CardUpgradeDefinition { UpgradeId = "add", Patches = [new CardComponentPatchDefinition
            { ComponentId = "extra", Component = Impact(3) with { ComponentId = "extra" } }] };
        var dependent = Numeric("dependent", 1) with { Patches = [new CardEffectNumericPatchDefinition
            { ComponentId = "extra", Value = 1 }] };
        var runtime = Runtime([add, dependent]);
        var planner = new CardTransformationPlanner(runtime);
        var run = State();
        Assert.DoesNotContain(planner.Options(run).Value, option => option.UpgradeId == "dependent");
        run = WithCard(run, planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "add").Value);
        Assert.Contains(planner.Options(run).Value, option => option.Operation == CardTransformationOperation.Apply && option.UpgradeId == "dependent");
        run = WithCard(run, planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "dependent").Value);
        Assert.DoesNotContain(planner.Options(run).Value, option => option.Operation == CardTransformationOperation.Remove && option.TargetTransformationId == 1);
        Assert.True(planner.Plan(run, CardId, CardTransformationOperation.Remove, 1).IsFailure);
    }

    [Fact]
    public void Discovery_RejectsCrossRevisionOrCrossSettingRuntime()
    {
        var planner = new CardTransformationPlanner(Runtime([Numeric("plus", 2)]));
        Assert.True(planner.Options(State() with { ConfigName = "other" }).IsFailure);
        Assert.True(planner.Options(State() with { Determinism = DeterministicContext.Create(42, "new-revision") }).IsFailure);
    }

    [Fact]
    public void Activity_RequiresWhitelistAndOptInRemovalReplacement()
    {
        var runtime = Runtime([Numeric("plus", 2), Numeric("not-offered", 9)]);
        var node = new RunMapNodeState { NodeId = "forge", Activity = new()
        {
            Type = RunActivityType.CardUpgrade, Parameters = new Dictionary<string, JsonElement>
            {
                ["upgradeIds"] = JsonSerializer.SerializeToElement(new[] { "plus" })
            }
        } };
        var run = State() with { CurrentNodeId = "forge", Map = new() { Nodes = [node] }, ResolvedMode = new() };
        var planner = new CardTransformationPlanner(runtime);
        Assert.True(planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "not-offered").IsFailure);
        var first = planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "plus").Value;
        run = WithCard(run, first);
        Assert.True(planner.Plan(run, CardId, CardTransformationOperation.Remove, 1).IsFailure);
        node = node with { Activity = node.Activity with { Parameters = node.Activity.Parameters.ToImmutableDictionary().Add("allowRemoval", JsonSerializer.SerializeToElement(true)).Add("allowReplacement", JsonSerializer.SerializeToElement(true)) } };
        run = run with { Map = new() { Nodes = [node] } };
        Assert.True(planner.Plan(run, CardId, CardTransformationOperation.Remove, 1).IsSuccess);
        Assert.True(planner.Plan(run, CardId, CardTransformationOperation.Replace, 1, "plus").IsSuccess);
        Assert.True(planner.Plan(run with { CompletedActivityNodeIds = ["forge"] }, CardId, CardTransformationOperation.Remove, 1).IsFailure);
    }

    [Fact]
    public void PermanentTransformations_RejectActiveEncounterEvenInDeveloperMode()
    {
        var planner = new CardTransformationPlanner(Runtime([Numeric("plus", 2)]));
        var run = State() with { ActiveEncounterId = Guid.NewGuid() };
        Assert.Empty(planner.Options(run).Value);
        Assert.True(planner.Plan(run, CardId, CardTransformationOperation.Apply, upgradeId: "plus").IsFailure);
    }

    [Fact]
    public void Publication_RejectsForgedSnapshotsAndMissingBundles()
    {
        var forged = Numeric("forged", 2) with { Patches = [new CardBundleSnapshotPatchDefinition
            { Namespace = "behavior", BundleId = "pulse", Components = [Impact(3) with { ComponentId = "behavior.impact" }] }] };
        Assert.True(CardBundleCompiler.Seal(forged, Runtime([forged])).IsFailure);
        Assert.True(CardBundleCompiler.Seal(BundleUpgrade("missing", CardComponentPatchOperation.Add, "missing"), Runtime([])).IsFailure);
    }

    [Fact]
    public void CanonicalCommands_ReplaceRemoveAreIdempotentAndRollbackInvalidDependencies()
    {
        var runtime = Runtime([Numeric("plus", 2), Numeric("more", 4)]);
        var manager = Manager(runtime);
        var run = State();
        Assert.True(manager.HydrateForReplay(run).IsSuccess);
        run = Execute(manager, run, RunCommandTypes.UpgradeCard, new CardUpgradeCommand(CardId, "plus"), 1).State;
        var envelope = Envelope(run, RunCommandTypes.ReplaceCardTransformation, new CardTransformationReplaceCommand(CardId, 1, "more"), 2);
        var receipt = manager.Execute(run.RunId, envelope).Value;
        var duplicate = manager.Execute(run.RunId, envelope).Value;
        Assert.True(duplicate.Duplicate);
        Assert.Equal(receipt.StateHash, duplicate.StateHash);
        run = receipt.State;
        Assert.Equal(2, run.Deck.Topology.GetCard(CardId)!.Upgrades.Count);
        var invalid = manager.Execute(run.RunId, Envelope(run, RunCommandTypes.RemoveCardTransformation, new CardTransformationRemoveCommand(CardId, 1), 3));
        Assert.True(invalid.IsFailure);
        Assert.Equal(receipt.StateHash, CanonicalJson.ComputeHash(manager.GetRun(run.RunId).Value));
        var removed = Execute(manager, run, RunCommandTypes.RemoveCardTransformation, new CardTransformationRemoveCommand(CardId, 2), 4);
        Assert.Empty(Resolve(removed.State.Deck.Topology.GetCard(CardId)!, runtime).AppliedUpgrades);
        Assert.Equal(3, removed.State.Deck.Topology.GetCard(CardId)!.Upgrades.Count);
    }

    [Fact]
    public void DiscoveryAndCommandsShareExactPinnedOptionsAndActivityCompletion()
    {
        var content = Runtime([Numeric("plus", 2), Numeric("forbidden", 9)]);
        var manager = Manager(content);
        var node = new RunMapNodeState { NodeId = "forge", Activity = new()
        {
            Type = RunActivityType.CardUpgrade,
            Parameters = new Dictionary<string, JsonElement>
            {
                ["upgradeIds"] = JsonSerializer.SerializeToElement(new[] { "plus" }),
                ["allowRemoval"] = JsonSerializer.SerializeToElement(true),
                ["allowReplacement"] = JsonSerializer.SerializeToElement(true)
            }
        } };
        var run = State() with { CurrentNodeId = node.NodeId, Map = new() { Nodes = [node] }, ResolvedMode = new() };
        Assert.True(manager.HydrateForReplay(run).IsSuccess);
        var commands = manager.GetAvailableCommands(run.RunId);
        Assert.True(commands.IsSuccess, commands.IsFailure ? commands.Error : null);
        var option = Assert.Single(commands.Value.Single(command => command.Type == RunCommandTypes.UpgradeCard).ValidPayload.GetProperty("options").EnumerateArray());
        Assert.Equal("plus", option.GetProperty("upgradeId").GetString());
        Assert.Equal(CardId, option.GetProperty("cardInstanceId").GetGuid());
        var hash = CanonicalJson.ComputeHash(run);
        Assert.True(manager.Execute(run.RunId, Envelope(run, RunCommandTypes.UpgradeCard, new CardUpgradeCommand(CardId, "forbidden"), 15)).IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(manager.GetRun(run.RunId).Value));
        var accepted = Execute(manager, run, RunCommandTypes.UpgradeCard, new CardUpgradeCommand(CardId, "plus"), 16);
        Assert.Contains("forge", accepted.State.CompletedActivityNodeIds);
        Assert.Equal(RunCommandTypes.ResolveNode, Assert.Single(manager.GetAvailableCommands(run.RunId).Value).Type);
        Assert.Empty(manager.GetCardTransformationOptions(run.RunId, CardId).Value);
    }

    [Fact]
    public void Compiler_RejectsDanglingOutputAliasAfterNamespaceRemoval()
    {
        var card = Base() with { Components =
        [
            Impact(6) with { ComponentId = "behavior.impact", Effect = Impact(6).Effect with { OutputId = "behavior__hit" } },
            Impact(1) with { ComponentId = "followup", Effect = Impact(1).Effect with { FlatValue = null, FormulaValue = "results.behavior__hit.target.last.applied_change" } }
        ] };
        var runtime = Runtime([BundleUpgrade("remove", CardComponentPatchOperation.Remove, null)], card: card);
        Assert.True(new CardContentCompiler().Compile("strike", runtime).IsSuccess);
        var planned = new CardTransformationPlanner(runtime).Plan(State(), CardId, CardTransformationOperation.Apply, upgradeId: "remove");
        Assert.True(planned.IsFailure);
        Assert.Contains("missing output alias", planned.Error);
    }

    [Fact]
    public void Activity_RejectsNonBooleanRemovalAndReplacementConfiguration()
    {
        var registry = RunActivityRegistry.CreateDefault();
        foreach (var key in new[] { "allowRemoval", "allowReplacement" })
            Assert.True(registry.Validate(new() { Type = RunActivityType.CardUpgrade, Parameters =
                new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement("true") } }).IsFailure);
    }

    [Fact]
    public void Discovery_WithoutPinnedRuntimeFailsInsteadOfPublishingRawPairs()
    {
        var manager = new RunManager(Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>());
        var run = State() with { CurrentNodeId = "forge", Map = new() { Nodes = [new()
            { NodeId = "forge", Activity = new() { Type = RunActivityType.CardUpgrade } }] } };
        Assert.True(manager.HydrateForReplay(run).IsSuccess);
        Assert.True(manager.GetAvailableCommands(run.RunId).IsFailure);
        Assert.True(manager.GetCardTransformationOptions(run.RunId, CardId).IsFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DurableGateway_RestartSemanticReplayAndBranchIsolationPreserveAllCommands(bool grammar)
    {
        var directory = Path.Combine(Path.GetTempPath(), "heroscript-transformation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = grammar ? GrammarUpgrade(Rule()) with { UpgradeId = "pulse" }
                : BundleUpgrade("pulse", CardComponentPatchOperation.Add, "pulse");
            var content = Runtime([first, Numeric("plus", 2)], Bundle(3));
            var factory = Factory(content);
            var parent = State();
            var branchCommand = new RunBranchStartCommand(parent.RunId, parent.Sequence, "transformed", CanonicalJson.ComputeHash(parent));
            var branch = RunBranchTransitions.Create(parent, branchCommand).Value;
            string hash;
            GameplayCommandEnvelope removal;
            using (var store = new FileRunCommitStore(directory, NullLogger.Instance))
            {
                await store.AppendAsync(InitialCommit(parent, "TEST", new { }));
                await store.AppendAsync(InitialCommit(branch, RunCommandTypes.CreateBranchFromHistory, branchCommand, parent.Determinism.Step));
                var live = factory.Create(new(GameplayPersistenceMode.Authoritative, store));
                Assert.True(live.Runs.HydrateForReplay(branch).IsSuccess);
                var applied = live.Gateway.Execute(branch.RunId, Envelope(branch, RunCommandTypes.UpgradeCard, new CardUpgradeCommand(CardId, "pulse"), 10));
                Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
                var state = applied.Value.Receipt.State;
                if (grammar)
                {
                    var effective = Resolve(state.Deck.Topology.GetCard(CardId)!, content);
                    Assert.Single(effective.CompositionTrace);
                    Assert.Single(effective.All<CardEffectComponentDefinition>()[0].Effect.ChainedEffects!);
                }
                var replaced = live.Gateway.Execute(branch.RunId, Envelope(state, RunCommandTypes.ReplaceCardTransformation, new CardTransformationReplaceCommand(CardId, 1, "plus"), 11));
                Assert.True(replaced.IsSuccess, replaced.IsFailure ? replaced.Error : null);
                state = replaced.Value.Receipt.State;
                removal = Envelope(state, RunCommandTypes.RemoveCardTransformation, new CardTransformationRemoveCommand(CardId, 2), 12);
                var removed = live.Gateway.Execute(branch.RunId, removal);
                Assert.True(removed.IsSuccess, removed.IsFailure ? removed.Error : null);
                hash = removed.Value.Receipt.StateHash;
                Assert.Empty(parent.Deck.Topology.GetCard(CardId)!.Upgrades);
                Assert.Equal(3, removed.Value.Receipt.State.Deck.Topology.GetCard(CardId)!.Upgrades.Count);
            }
            using var restarted = new FileRunCommitStore(directory, NullLogger.Instance);
            Assert.Equal(hash, CanonicalJson.ComputeHash((await restarted.LoadLatestStateAsync(branch.RunId))!));
            var restored = factory.Create(new(GameplayPersistenceMode.Authoritative, restarted));
            var retry = restored.Gateway.Execute(branch.RunId, removal);
            Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error : null);
            Assert.True(retry.Value.Receipt.Duplicate);
            Assert.Equal(hash, retry.Value.Receipt.StateHash);
            var replay = new RunSemanticReplayService(restarted, factory);
            for (var index = 0; index < 10; index++)
            {
                var verified = await replay.VerifyAsync(branch.RunId);
                Assert.True(verified.IsValid, string.Join("; ", verified.Errors));
                Assert.True(verified.Reexecuted);
                Assert.Equal(4, verified.CommandsReplayed);
                Assert.Equal(hash, verified.ActualFinalHash);
            }
            Assert.Single(await restarted.LoadCommitsAsync(parent.RunId));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static RunCommit InitialCommit(RunState state, string type, object command, ulong beforeStep = 0)
    {
        var payload = JsonSerializer.SerializeToElement(command);
        var hash = CanonicalJson.ComputeHash(state);
        var frames = new[] { new RunCommitFrame { FrameIndex = 0, Step = state.Determinism.Step, Kind = type, ResultHash = hash, Resolution = payload } };
        return new()
        {
            RunId = state.RunId, Sequence = 1, RootCommand = new(DeterministicId.Create(42, 0, type), type, 0, beforeStep, CanonicalJson.ComputeHash(payload)),
            Command = payload, StateHash = hash, StateAfter = state, Lineage = state.Lineage,
            BeforeStep = beforeStep, AfterStep = state.Determinism.Step, LogicalTimestamp = state.Determinism.LogicalTimestamp.UtcDateTime,
            Frames = frames, Facts = RunCommitFacts.FromFrames(frames)
        };
    }

    private static GameplayRuntimeFactory Factory(ContentRuntime content) => new(
        Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>(), Mock.Of<ICardPoolResolver>(), Mock.Of<ICardContentCatalog>(),
        Mock.Of<IPinnedContentCatalog<ScriptModifierDefinition>>(), Manifests(content),
        Mock.Of<IResourceCatalog<GameModeDefinition>>(), Mock.Of<IGameModeResolver>(), Mock.Of<IContentPublicationService>(),
        Runtimes(content), Mock.Of<IResourceManager>(), Mock.Of<ICombatFactory>(), Mock.Of<ICombatFlowPlanner>(),
        Mock.Of<ICombatCommandHandler>(), Mock.Of<IAutomaticFlowDriver>(), Mock.Of<IEffectTriggerExecutor>(),
        new GameEventContextAccessor(), GameplayCommandCodec.CreateDefault());

    private static IContentManifestProvider Manifests(ContentRuntime content)
    {
        var mock = new Mock<IContentManifestProvider>(MockBehavior.Strict);
        mock.Setup(service => service.GetByRevision(Revision)).Returns(Result<ContentManifest>.Success(content.Manifest));
        return mock.Object;
    }

    private static IContentRuntimeResolver Runtimes(ContentRuntime content)
    {
        var mock = new Mock<IContentRuntimeResolver>(MockBehavior.Strict);
        mock.Setup(service => service.Resolve(Revision, "test")).Returns(Result<ContentRuntime>.Success(content));
        return mock.Object;
    }

    private static RunManager Manager(ContentRuntime content) => new(Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>(), contentRuntimes: Runtimes(content));
    private static RunCommandReceipt Execute(RunManager manager, RunState run, string type, object payload, ulong ordinal)
    {
        var result = manager.Execute(run.RunId, Envelope(run, type, payload, ordinal));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }
    private static GameplayCommandEnvelope Envelope(RunState run, string type, object payload, ulong ordinal) => new(
        new(DeterministicId.Create(42, ordinal, "test-command"), type, run.Sequence, run.Determinism.Step), JsonSerializer.SerializeToElement(payload));

    private static RunState State() => new()
    {
        RunId = RunId, ConfigName = "test", Sequence = 1, Lineage = RunLineage.Root(RunId),
        Determinism = DeterministicContext.Create(42, Revision),
        Deck = TestCardZones.WithInstance("cards", new() { CardInstanceId = CardId, DefinitionId = "strike" }),
        ResolvedMode = new() { ProgressionPolicy = new() { AllowOutOfActivityCommands = true } }
    };
    private static RunState WithCard(RunState run, CardInstanceState card) => run with
        { Deck = TestCardZones.WithInstance("cards", card) };
    private static EffectiveCardDefinition Resolve(CardInstanceState card, ContentRuntime runtime)
    {
        var compiled = new CardContentCompiler().Compile("strike", runtime);
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        var effective = new EffectiveCardResolver().Resolve(compiled.Value, card);
        Assert.True(effective.IsSuccess, effective.IsFailure ? effective.Error : null);
        return effective.Value;
    }
    private static CardContentDefinition Base() => new() { CardId = "strike", Tags = ["attack"], Components = [Impact(6)] };
    private static CardEffectComponentDefinition Impact(float value) => new()
        { ComponentId = "impact", Effect = new() { Type = EffectType.DAMAGE, TargetResource = "health", FlatValue = value } };
    private static CardComponentBundleDefinition Bundle(float value) => new() { BundleId = "pulse", Components = [Impact(value)] };
    private static CardUpgradeDefinition Numeric(string id, float value) => new()
        { UpgradeId = id, Patches = [new CardEffectNumericPatchDefinition { ComponentId = "impact", Value = value }] };
    private static CardUpgradeDefinition BundleUpgrade(string id, CardComponentPatchOperation operation, string? bundleId) => new()
    {
        UpgradeId = id, Category = CardTransformationCategory.Behavior,
        Patches = [new CardBundlePatchDefinition { Namespace = "behavior", Operation = operation, BundleId = bundleId }]
    };
    private static ContentRuntime Runtime(CardUpgradeDefinition[] upgrades, CardComponentBundleDefinition? bundle = null, CardContentDefinition? card = null)
    {
        var artifacts = new List<(string Kind, string Path, Dictionary<string, object> Definitions)>
        {
            ("cards", "cards/catalog.json", new() { ["strike"] = card ?? Base() }),
            ("resources", "resources/catalog.json", new() { ["health"] = new ResourceDefinition { ResourceId = "health" } }),
            ("card-upgrades", "card-upgrades/catalog.json", upgrades.ToDictionary(item => item.UpgradeId, item => (object)item))
        };
        if (bundle != null) artifacts.Add(("card-component-bundles", "card-component-bundles/catalog.json", new() { [bundle.BundleId] = bundle }));
        var result = ContentRuntime.Create(new ContentBundle
        {
            Manifest = new() { ConfigName = "test", Revision = Revision, Artifacts = artifacts.Select(item =>
                new ContentArtifactManifest { Kind = item.Kind, Path = item.Path, DefinitionCount = item.Definitions.Count }).ToImmutableArray() },
            Artifacts = artifacts.ToImmutableDictionary(item => item.Path, item => JsonSerializer.SerializeToElement(item.Definitions))
        });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }
}
