using System.Collections.Immutable;
using System.Text.Json;
using Core.CardZones;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckLoadouts;
using Core.Run.Loadouts;
using Xunit;

namespace Core.Tests.DeckLoadouts;

public sealed class DeckLoadoutPlannerTests
{
    internal static Guid Id(int index) => Guid.Parse($"10000000-0000-4000-8000-{index:000000000000}");
    internal static DeckCompositionPolicyDefinition Policy => BuildContractTests.Policy with
    { Groups = [BuildContractTests.Policy.Groups[0], BuildContractTests.Policy.Groups[1] with { ModuleIds = ["alternate", "different"] }] };
    internal static ContentRuntime Runtime => ContentRuntime.Create(BuildContractTests.Bundle(
        ("cards", "other", new CardContentDefinition { CardId = "other", Components = [new CardConditionComponentDefinition { ComponentId = "valid", Expression = "1" }] }),
        ("deck-modules", "different", new DeckModuleDefinition { ModuleId = "different", CardIds = ["other"], StartingCardIds = ["other"] }),
        ("deck-composition-policies", "build", Policy))).Value;
    internal static RunState Source
    {
        get
        {
            var instances = Enumerable.Range(1, 6).ToDictionary(i => Id(i), i => new CardInstanceState
            { CardInstanceId = Id(i), OwnerId = "hero", DefinitionId = i == 6 ? "other" : "card", CreationOrdinal = (ulong)i,
                Upgrades = i == 4 ? [new CardUpgradeState { UpgradeId = "preserved", TransformationId = 1 }] : [] });
            var zone = new CardZoneState { Address = new() { ZoneId = "neutral", OwnerId = "hero" }, InstanceIds = instances.Keys.ToArray() };
            return new() { ConfigName = "test", PlayerEntityId = "hero", Determinism = DeterministicContext.Create(23, Runtime.Manifest.Revision),
                Deck = new() { Topology = new() { Instances = instances, Zones = new Dictionary<string, CardZoneState> { [zone.Address.Key] = zone } } } };
        }
    }
    internal static RunState Initialized => DeckLoadoutPlanner.Initialize(Source, Runtime, "hero", "build").Value;
    internal static DeckLoadoutSelection Selection(RunState run) => new(run.DeckLoadout!.Actors["hero"].Groups, run.DeckLoadout.Actors["hero"].Modules);

    [Fact] public void InitializeSelectsOwnedInstancesWithoutDuplicatingOrMovingThem()
    {
        var run = Source; var hash = CanonicalJson.ComputeHash(run);
        var initialized = DeckLoadoutPlanner.Initialize(run, Runtime, "hero", "build");
        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        Assert.Same(run.Deck, initialized.Value.Deck); Assert.Same(run.Determinism, initialized.Value.Determinism);
        Assert.Equal(new[] { Id(1), Id(2), Id(3) }, DeckLoadoutPlanner.Capture(initialized.Value, Runtime, "hero").Value.ActiveCardIds);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
        Assert.DoesNotContain("deckLoadout", CanonicalJson.Serialize(run));
        Assert.Same(initialized.Value, DeckLoadoutPlanner.Initialize(initialized.Value, Runtime, "hero", "build").Value);
    }
    [Fact] public void ExchangeAndModuleChangeAreAtomicAndKeepPhysicalIdentityAndUpgrades()
    {
        var run = Initialized; var before = CanonicalJson.ComputeHash(run); var selection = Selection(run);
        var changed = selection with { Groups = selection.Groups.SetItem("primary", [Id(1), Id(4)]).SetItem("secondary", [Id(6)]), Modules = selection.Modules.SetItem("secondary", "different") };
        var plan = DeckLoadoutPlanner.Plan(run, Runtime, "hero", changed);
        Assert.True(plan.IsSuccess, plan.IsFailure ? plan.Error : null);
        Assert.Same(run.Deck, plan.Value.Candidate.Deck);
        Assert.Same(run.Deck.GetCard(Id(4)), plan.Value.Candidate.Deck.GetCard(Id(4)));
        Assert.Equal("preserved", plan.Value.Candidate.Deck.GetCard(Id(4))!.Upgrades[0].UpgradeId);
        Assert.Equal(new[] { Id(1), Id(4), Id(6) }, DeckLoadoutPlanner.Capture(plan.Value.Candidate, Runtime, "hero").Value.ActiveCardIds);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
    }
    [Fact] public void InvalidWholeSelectionsNeverPublishPartialModuleOrGroupChanges()
    {
        var run = Initialized; var hash = CanonicalJson.ComputeHash(run); var selection = Selection(run);
        foreach (var invalid in new[] {
            selection with { Groups = selection.Groups.SetItem("primary", [Id(1)]) },
            selection with { Groups = selection.Groups.SetItem("primary", [Id(1), Id(1)]) },
            selection with { Groups = selection.Groups.SetItem("secondary", [Id(1)]) },
            selection with { Modules = selection.Modules.SetItem("secondary", "different") },
            selection with { Groups = selection.Groups.Add("unknown", []) },
            selection with { Groups = selection.Groups.SetItem("primary", [Id(1), Id(99)]) },
            selection with { Modules = selection.Modules.SetItem("secondary", "not-owned-by-policy") } })
        {
            Assert.True(DeckLoadoutPlanner.Plan(run, Runtime, "hero", invalid).IsFailure);
            Assert.Equal(hash, CanonicalJson.ComputeHash(run));
        }
    }
    [Fact] public void ActorRevisionAndEncounterBoundariesAreEnforced()
    {
        var run = Initialized; var selection = Selection(run);
        Assert.True(DeckLoadoutPlanner.Plan(run, Runtime, "other", selection).IsFailure);
        Assert.True(DeckLoadoutPlanner.Plan(run with { ConfigName = "other" }, Runtime, "hero", selection).IsFailure);
        Assert.True(DeckLoadoutPlanner.Plan(run with { Determinism = DeterministicContext.Create(23, "other") }, Runtime, "hero", selection).IsFailure);
        Assert.True(DeckLoadoutPlanner.Plan(run with { ActiveEncounterId = Id(100) }, Runtime, "hero", selection).IsFailure);
        Assert.True(DeckLoadoutPlanner.Plan(run with { Lifecycle = RunLifecycleState.Completed }, Runtime, "hero", selection).IsFailure);
    }
    [Fact] public void ClientOrderingAndRoundTripDoNotChangeTheSelectedBuild()
    {
        var run = Initialized; var original = Selection(run);
        var reversed = original with { Groups = original.Groups.SetItem("primary", [Id(2), Id(1)]) };
        var plan = DeckLoadoutPlanner.Plan(run, Runtime, "hero", reversed).Value;
        Assert.Equal(CanonicalJson.ComputeHash(run), CanonicalJson.ComputeHash(plan.Candidate));
        var restored = JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(plan.Candidate))!;
        Assert.Equal(CanonicalJson.ComputeHash(plan.Candidate), CanonicalJson.ComputeHash(restored));
        Assert.True(DeckLoadoutPlanner.Capture(restored, Runtime, "hero").IsSuccess);
    }
    [Fact] public void PermanentRemovalReconcilesReferencesButDoesNotFabricateReplacementCards()
    {
        var run = Initialized;
        var instances = run.Deck.Topology.Instances.Where(p => p.Key != Id(2)).ToDictionary();
        var zones = run.Deck.Topology.Zones.ToDictionary(pair => pair.Key, pair => pair.Value with { InstanceIds = pair.Value.InstanceIds.Where(id => id != Id(2)).ToArray() });
        var removed = run with { Deck = run.Deck with { Topology = run.Deck.Topology with { Instances = instances, Zones = zones } } };
        var next = DeckLoadoutPlanner.ReconcileDestroyedCards(removed);
        Assert.Equal(new[] { Id(1) }, next.DeckLoadout!.Actors["hero"].Groups["primary"]);
        Assert.True(DeckLoadoutPlanner.Capture(next, Runtime, "hero").IsFailure);
        Assert.Equal(2, run.DeckLoadout!.Actors["hero"].Groups["primary"].Length);
    }
    [Fact] public void MissingStartingOwnershipFailsWithoutAllocatingIdsOrRng()
    {
        var run = Source with { Deck = new() }; var hash = CanonicalJson.ComputeHash(run);
        Assert.True(DeckLoadoutPlanner.Initialize(run, Runtime, "hero", "build").IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
    }
    [Fact] public void ActorIdentityInsidePersistedSelectionMustMatchItsAddress()
    {
        var run = Initialized;
        var state = run.DeckLoadout!;
        var invalid = run with { DeckLoadout = state with { Actors = state.Actors.SetItem("hero", state.Actors["hero"] with { ActorId = "other" }) } };
        Assert.True(DeckLoadoutPlanner.Plan(invalid, Runtime, "hero", Selection(run)).IsFailure);
        Assert.True(DeckLoadoutPlanner.Capture(invalid, Runtime, "hero").IsFailure);
        Assert.True(DeckLoadoutPlanner.Initialize(invalid, Runtime, "hero", "build").IsFailure);
    }
    [Fact] public void PreparationOnlyPolicyUsesActualUnresolvedActivity()
    {
        var policy = Policy with { EditWindows = [LoadoutEquipmentWindow.Preparation] };
        var run = Initialized;
        Assert.True(DeckLoadoutPlanner.CanChange(run, policy).IsFailure);
        run = run with { CurrentNodeId = "prepare", Map = new() { Nodes = [new() { NodeId = "prepare", Activity = new() { Type = RunActivityType.Preparation } }] } };
        Assert.True(DeckLoadoutPlanner.CanChange(run, policy).IsSuccess);
        Assert.True(DeckLoadoutPlanner.CanChange(run with { CompletedActivityNodeIds = ["prepare"] }, policy).IsFailure);
    }
}
