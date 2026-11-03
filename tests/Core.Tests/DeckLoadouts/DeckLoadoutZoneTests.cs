using System.Collections.Immutable;
using Core.CardZones;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckLoadouts;
using Core.Run.Loadouts;
using Core.Common;
using Core.Config;
using Core.Effects;
using Moq;
using Xunit;

namespace Core.Tests.DeckLoadouts;

public sealed class DeckLoadoutZoneTests
{
    [Fact]
    public void RunCreationValidatesBuildBeforeStartupAndRejectsMissingOwnedCards()
    {
        var baseGraph = Graph(CardZoneOwnerScope.Actor);
        var graph = baseGraph with { FlowOwnerBinding = CardZoneOwnerBinding.ActiveActor,
            Flows = [baseGraph.Flows[0] with { Triggers = ["run.started"] }] };
        var mode = new GameModeDefinition { ModeId = "mode", DeckCompositionPolicyId = "build" };
        var definition = new RunDefinition { RunId = "campaign", InitialCardZoneId = "neutral",
            InitialCardOwner = CardZoneOwnerBinding.ActiveActor, StartingResources = new Dictionary<string, float> { ["gold"] = 25 }, StartingCards = ["card", "card", "card", "card"],
            MapNodes = [new() { NodeId = "opening", Activity = new() { Type = RunActivityType.Encounter } }] };
        var runtime = ContentRuntime.Create(BuildContractTests.Bundle(("deck-composition-policies", "build", BuildContractTests.Policy),
            ("modes", "mode", mode), ("runs", "campaign", definition))).Value;
        RunManager Manager() {
            var resolver = new Mock<IContentRuntimeResolver>();
            resolver.Setup(r => r.Resolve(It.IsAny<string>(), It.IsAny<string>())).Returns(Result<ContentRuntime>.Success(runtime));
            var modes = new Mock<IGameModeResolver>();
            modes.Setup(r => r.Resolve("mode", "test")).Returns(Result<ResolvedGameMode>.Success(new() { Definition = mode, CardZoneSystem = graph }));
            var config = new Mock<IConfigManager>(); config.Setup(r => r.ResolveInheritanceChain("test")).Returns(["test"]);
            var loader = new Mock<IResourceLoader>();
            loader.Setup(r => r.LoadResource("runs/campaign.json", It.IsAny<IEnumerable<string>>(), true))
                .Returns(new Dictionary<string, System.Text.Json.JsonElement> { ["campaign"] = System.Text.Json.JsonSerializer.SerializeToElement(definition) });
            return new(config.Object, loader.Object, gameModeResolver: modes.Object, contentRuntimes: resolver.Object,
                resources: TestDataBuilders.MockResourceManager().Object, activityEffects: new RunActivityEffectExecutor(Mock.Of<IEffectTriggerExecutor>()), cardZoneFlows: new CardZoneFlowExecutor());
        }
        var options = new RunStartOptions("test", "campaign", "hero", Seed: 71, ContentRevision: runtime.Manifest.Revision, ModeId: "mode");
        var manager = Manager();
        var first = manager.StartRun(options);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.NotNull(first.Value.DeckLoadout);
        Assert.Equal(3, first.Value.Deck.Topology.GetZone(new() { ZoneId = "ready", OwnerId = "hero" })!.InstanceIds.Count);
        Assert.Single(first.Value.Deck.Topology.GetZone(new() { ZoneId = "neutral", OwnerId = "hero" })!.InstanceIds);
        Assert.Equal(CanonicalJson.ComputeHash(first.Value), CanonicalJson.ComputeHash(Manager().StartRun(options).Value));
        var editing = Manager(); var original = editing.StartRun(options).Value;
        var edit = new DeckLoadoutEdit("hero", "primary", original.DeckLoadout!.Actors["hero"].Groups["primary"][0],
            DeckLoadoutZoneSets.Resolve(original)["deck.reserve"].Single());
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(edit);
        var envelope = new GameplayCommandEnvelope(new(Guid.Parse("60000000-0000-4000-8000-000000000001"), RunCommandTypes.EditDeckLoadout,
            original.Sequence, original.Determinism.Step, CanonicalJson.ComputeHash(payload)), payload);
        var committed = editing.Execute(original.RunId, envelope);
        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error : null);
        Assert.True(editing.Execute(original.RunId, envelope).Value.Duplicate);
        var edited = editing.GetRun(original.RunId).Value;
        Assert.Equal(original.Sequence + 1, edited.Sequence);
        Assert.Contains(edit.ReplacementCardId!.Value, edited.DeckLoadout!.Actors["hero"].Groups["primary"]);
        Assert.Equal(CanonicalJson.ComputeHash(original.Deck), CanonicalJson.ComputeHash(edited.Deck));
        Assert.Equal(original.Determinism.RandomState, edited.Determinism.RandomState);
        var resumed = Manager(); Assert.True(resumed.HydrateForReplay(edited).IsSuccess);
        Assert.Equal(CanonicalJson.ComputeHash(edited), CanonicalJson.ComputeHash(resumed.GetRun(edited.RunId).Value));
        var seed = first.Value.Determinism.DrawUInt64();
        var combat = new Core.Combat.Models.CombatState { CombatId = Guid.NewGuid(), RunId = first.Value.RunId, RunNodeId = "opening",
            Determinism = DeterministicContext.Create(seed.Value, runtime.Manifest.Revision) };
        var attached = manager.AttachEncounter(first.Value.RunId, first.Value.Sequence, first.Value.Determinism.Step, combat);
        Assert.True(attached.IsSuccess, attached.IsFailure ? attached.Error : null);
        var capture = attached.Value.GetActiveEncounter()!.DeckComposition;
        Assert.NotNull(capture);
        Assert.Equal(3, capture.ActiveCardIds.Length);
        Assert.Equal(first.Value.Sequence, capture.SourceSequence);
        Assert.Equal(first.Value.Determinism.Step, capture.SourceStep);
        Assert.Null(first.Value.ActiveEncounterId);
        var invalid = Manager().StartRun(options with { StartingCards = [new() { DefinitionId = "card" }] });
        Assert.True(invalid.IsFailure);
        Assert.Contains("Missing owned", invalid.Error);
    }

    private static CardZoneSystemDefinition Graph(CardZoneOwnerScope scope, string set = "deck.active") => new()
    {
        CardZoneSystemId = "arbitrary", Zones = [new() { ZoneId = "neutral", OwnerScope = scope, Ordering = CardZoneOrdering.Ordered }, new() { ZoneId = "ready", OwnerScope = scope, Ordering = CardZoneOrdering.Ordered }],
        Flows = [new() { FlowId = "assemble", AllowedInvocations = [CardZoneFlowInvocation.Boundary], Steps = [new()
        {
            StepId = "filter", Operation = CardZoneOperation.Move, SourceZoneId = "neutral", TargetZoneId = "ready",
            Selection = new() { Strategy = CardZoneSelectionStrategy.All, InstanceSetId = set }
        }] }]
    };

    [Theory]
    [InlineData(CardZoneOwnerScope.Actor, "hero")]
    [InlineData(CardZoneOwnerScope.RunOwner, "$run")]
    [InlineData(CardZoneOwnerScope.Global, "$global")]
    public void SelectedGroupsUseExistingCirculationWithoutDrawingReserve(CardZoneOwnerScope scope, string owner)
    {
        var source = DeckLoadoutPlannerTests.Initialized;
        var originalHash = CanonicalJson.ComputeHash(source);
        var graph = Graph(scope);
        var compiled = CardZoneSystemCompiler.Compile(graph);
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        var from = new CardZoneAddress { ZoneId = "neutral", OwnerId = owner };
        var to = new CardZoneAddress { ZoneId = "ready", OwnerId = owner };
        var zones = CardZoneTransitions.CreateEmpty([from, to]).Value;
        zones = zones with { Instances = source.Deck.Topology.Instances, Zones = zones.ZoneItems.SetItem(from.Key,
            zones.GetZone(from)! with { InstanceIds = source.Deck.Topology.Instances.Keys.ToArray() }) };
        var context = new CardZoneFlowContext { Invocation = CardZoneFlowInvocation.Boundary, FlowOwnerId = owner, RunOwnerId = owner, ActiveActorId = "hero",
            InstanceSets = DeckLoadoutZoneSets.Resolve(source) };
        var executor = new CardZoneFlowExecutor();
        var first = executor.Execute(compiled.Value, zones, source.Determinism, "assemble", context);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(3, first.Value.State.GetZone(to)!.InstanceIds.Count);
        Assert.Equal(3, first.Value.State.GetZone(from)!.InstanceIds.Count);
        Assert.Equal(6, first.Value.State.Instances.Count);
        Assert.Equal(source.Deck.GetCard(DeckLoadoutPlannerTests.Id(4))!.Upgrades,
            first.Value.State.Instances[DeckLoadoutPlannerTests.Id(4)].Upgrades);
        Assert.Equal(first.Value.Fingerprint, executor.Execute(compiled.Value, zones, source.Determinism, "assemble", context).Value.Fingerprint);
        Assert.Equal(originalHash, CanonicalJson.ComputeHash(source));
        Assert.Equal(source.Determinism.RandomState.DrawCount, first.Value.Context.RandomState.DrawCount);
        var again = executor.Execute(compiled.Value, first.Value.State, first.Value.Context, "assemble", context);
        Assert.True(again.IsSuccess);
        Assert.Equal(first.Value.State.GetZone(to)!.InstanceIds, again.Value.State.GetZone(to)!.InstanceIds);
    }

    [Fact]
    public void StartupAndEncounterDispatcherShareSetsWithoutProvisioningOpponentDeck()
    {
        var source = DeckLoadoutPlannerTests.Initialized;
        var baseGraph = Graph(CardZoneOwnerScope.Actor);
        var graph = baseGraph with { FlowOwnerBinding = CardZoneOwnerBinding.ActiveActor,
            Flows = [baseGraph.Flows[0] with { Triggers = ["run.started", "encounter.started"] }] };
        var compiled = CardZoneSystemCompiler.Compile(graph).Value;
        var ownTarget = new CardZoneAddress { ZoneId = "ready", OwnerId = "hero" };
        var topology = source.Deck.Topology;
        topology = topology with { Zones = topology.ZoneItems.Add(ownTarget.Key, new() { Address = ownTarget }) };
        source = source with { Deck = source.Deck with { Topology = topology },
            ResolvedMode = new() { Definition = new() { DeckCompositionPolicyId = "build" }, CardZoneSystem = graph } };
        var executor = new CardZoneFlowExecutor();
        var startup = RunCreationFlows.Execute(source, new() { InitialCardOwner = CardZoneOwnerBinding.ActiveActor }, 0, compiled, executor);
        Assert.True(startup.IsSuccess, startup.IsFailure ? startup.Error : null);
        Assert.Equal(3, startup.Value.Deck.Topology.GetZone(ownTarget)!.InstanceIds.Count);
        Assert.Equal(3, startup.Value.Deck.Topology.GetZone(new() { ZoneId = "neutral", OwnerId = "hero" })!.InstanceIds.Count);
        var provisioned = CardZoneRunFlowDispatcher.EnsureParticipants(startup.Value, ["hero", "opponent"]);
        Assert.True(provisioned.IsSuccess);
        var prepared = startup.Value with { Deck = startup.Value.Deck with { Topology = provisioned.Value.State } };
        var other = CardZoneRunFlowDispatcher.Execute(executor, prepared, prepared.Deck, prepared.Determinism,
            "encounter.started", activeActorId: "opponent");
        Assert.True(other.IsSuccess, other.IsFailure ? other.Error : null);
        Assert.Empty(other.Value.State.GetZone(new() { ZoneId = "ready", OwnerId = "opponent" })!.InstanceIds);
        Assert.Equal(3, other.Value.State.GetZone(ownTarget)!.InstanceIds.Count);
        Assert.Empty(source.Deck.Topology.GetZone(ownTarget)!.InstanceIds);
    }

    [Fact]
    public void CaptureRemainsFrozenAfterPermanentRemovalWhileNextBuildRequiresRepair()
    {
        var source = DeckLoadoutPlannerTests.Initialized;
        var captured = DeckLoadoutPlanner.Capture(source, DeckLoadoutPlannerTests.Runtime, "hero").Value;
        var encounter = new RunEncounterState { Combat = new() { CombatId = Guid.NewGuid() }, DeckComposition = captured };
        var removed = DeckLoadoutPlannerTests.Id(1);
        var topology = source.Deck.Topology;
        var changed = source with { ActiveEncounterId = encounter.Combat.CombatId, Encounters = [encounter],
            Deck = source.Deck with { Topology = topology with { Instances = topology.InstanceItems.Remove(removed),
                Zones = topology.ZoneItems.ToImmutableDictionary(p => p.Key, p => p.Value with { InstanceIds = p.Value.Items.Where(id => id != removed).ToArray() }) } } };
        changed = DeckLoadoutPlanner.ReconcileDestroyedCards(changed);
        Assert.Contains(removed, DeckLoadoutZoneSets.Resolve(changed)["deck.active"]);
        Assert.DoesNotContain(removed, changed.DeckLoadout!.Actors["hero"].Groups["primary"]);
        Assert.True(DeckLoadoutPlanner.Capture(changed, DeckLoadoutPlannerTests.Runtime, "hero").IsFailure);
    }

    [Fact]
    public void UnknownSetRejectsAtomicallyAndUnsupportedOperationsCannotIgnoreFilter()
    {
        var run = DeckLoadoutPlannerTests.Initialized;
        var graph = Graph(CardZoneOwnerScope.Actor);
        var flow = graph.Flows[0];
        var topology = CardZoneTransitions.CreateEmpty([new CardZoneAddress { ZoneId = "neutral", OwnerId = "hero" },
            new CardZoneAddress { ZoneId = "ready", OwnerId = "hero" }]).Value;
        var result = new CardZoneFlowExecutor().Execute(CardZoneSystemCompiler.Compile(graph).Value,
            topology, run.Determinism, "assemble", new() { Invocation = CardZoneFlowInvocation.Boundary, FlowOwnerId = "hero" });
        Assert.True(result.IsFailure); Assert.Contains("unavailable", result.Error);
        Assert.True(CardZoneSystemCompiler.Compile(graph with { Flows = [flow with { Steps = [flow.Steps[0] with { Operation = CardZoneOperation.Shuffle }] }] }).IsFailure);
    }

    [Fact]
    public void ExistingSettingsDoNotAcquireDerivedBuildState()
    {
        Assert.Empty(DeckLoadoutZoneSets.Resolve(new RunState()));
        Assert.DoesNotContain("DeckComposition", System.Text.Json.JsonSerializer.Serialize(new RunEncounterState()));
        Assert.DoesNotContain("DeckCompositionPolicyId", System.Text.Json.JsonSerializer.Serialize(new GameModeDefinition()));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void RevisionActivationRequiresOptInCompatibilityAndNoPendingEncounter(bool allow, bool pending, bool expected)
    {
        var mode = new GameModeDefinition { ModeId = "mode", DeckCompositionPolicyId = "build" };
        ContentRuntime Runtime(string revision) {
            var bundle = BuildContractTests.Bundle(("deck-composition-policies", "build", DeckLoadoutPlannerTests.Policy with
                { RevisionMigration = allow ? LoadoutRevisionMigration.RebindCompatible : null }),
                ("cards", "other", new CardContentDefinition { CardId = "other", Components = [new CardConditionComponentDefinition { ComponentId = "valid", Expression = "1" }] }),
                ("deck-modules", "different", new DeckModuleDefinition { ModuleId = "different", CardIds = ["other"], StartingCardIds = ["other"] }),
                ("modes", "mode", mode));
            return ContentRuntime.Create(bundle with { Manifest = bundle.Manifest with { Revision = revision } }).Value;
        }
        var source = DeckLoadoutPlannerTests.Initialized with { ResolvedMode = new() { Definition = mode }, ActiveEncounterId = pending ? Guid.NewGuid() : null };
        var hash = CanonicalJson.ComputeHash(source);
        var result = RunContentCompatibilityValidator.ValidateForActivation(source, Runtime(new string('b', 64)));
        Assert.True(expected == result.IsSuccess, result.IsFailure ? result.Error : "Unexpected compatibility");
        Assert.Equal(hash, CanonicalJson.ComputeHash(source));
    }
}
