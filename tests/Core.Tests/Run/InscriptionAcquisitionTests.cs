using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Determinism;
using Core.Meta;
using Core.Resources;
using Core.Run;
using Core.Run.Loadouts;
using Xunit;

namespace Core.Tests.Run;

public sealed class InscriptionAcquisitionTests
{
    [Theory]
    [InlineData(InscriptionInventoryScope.Actor)]
    [InlineData(InscriptionInventoryScope.Run)]
    public void OffersAreDeterministicPricedAtomicAndBoundToTheirCapturedRecipient(InscriptionInventoryScope scope)
    {
        var (run, runtime) = Setup(scope);
        var before = CanonicalJson.ComputeHash(run);
        var attempts = Enumerable.Range(0, 5).Select(_ => InscriptionStationTransitions.Open(run, runtime, new("alice", "reward"))).ToArray();
        Assert.All(attempts, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(attempts.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct());
        var opened = attempts[0].Value;
        var offer = Assert.Single(opened.Loadouts!.Offers!).Value;
        Assert.Equal("alice", offer.ActorId);
        Assert.Equal("focus", Assert.Single(offer.Options));
        Assert.True(InscriptionStationTransitions.Open(opened, runtime, new("bob", "reward")).IsFailure);
        var chosen = InscriptionStationTransitions.Choose(opened, runtime, new(offer.OfferId, "focus"));
        Assert.True(chosen.IsSuccess, chosen.IsFailure ? chosen.Error : null);
        Assert.Equal(7, chosen.Value.ResourceState.Current("gold"));
        Assert.Single(chosen.Value.Loadouts!.Inventories[chosen.Value.Loadouts.InventoryId(run.RunId, "alice")].Items);
        if (scope == InscriptionInventoryScope.Actor) Assert.Empty(chosen.Value.Loadouts.Inventories[chosen.Value.Loadouts.InventoryId(run.RunId, "bob")].Items);
        Assert.True(InscriptionStationTransitions.Choose(chosen.Value, runtime, new(offer.OfferId, "focus")).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        var recovered = JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(chosen.Value))!;
        Assert.Equal(CanonicalJson.ComputeHash(chosen.Value), CanonicalJson.ComputeHash(recovered));
    }

    [Fact]
    public void DecliningClosesWithoutCostOrGrantAndCannotReopenAtSameStation()
    {
        var (run, runtime) = Setup();
        var opened = InscriptionStationTransitions.Open(run, runtime, new("alice", "reward")).Value;
        var offer = opened.Loadouts!.Offers!.Values.Single();
        var declined = InscriptionStationTransitions.Choose(opened, runtime, new(offer.OfferId, null)).Value;
        Assert.Equal(10, declined.ResourceState.Current("gold"));
        Assert.Empty(declined.Loadouts!.Inventories.Values.SelectMany(inventory => inventory.Items));
        Assert.True(InscriptionStationTransitions.Open(declined, runtime, new("alice", "reward")).IsFailure);
        Assert.True(new RunProgressionService(RunActivityRegistry.CreateDefault()).CanResolve(declined, declined.Map.Nodes[0]).IsSuccess);
    }

    [Fact]
    public void NoMoneyWrongNodeOrForeignChoiceRejectWithoutPartialState()
    {
        var (run, runtime) = Setup();
        Assert.True(InscriptionStationTransitions.Open(run with { CurrentNodeId = "other" }, runtime, new("alice", "reward")).IsFailure);
        var opened = InscriptionStationTransitions.Open(run, runtime, new("alice", "reward")).Value;
        var offer = opened.Loadouts!.Offers!.Values.Single();
        var noMoney = opened with { ResourceState = opened.ResourceState with { Resources = new Dictionary<string, ResourcePool>
            { ["gold"] = opened.ResourceState.Get("gold")!.Set(0) } } };
        var before = CanonicalJson.ComputeHash(noMoney);
        Assert.True(InscriptionStationTransitions.Choose(noMoney, runtime, new(offer.OfferId, "focus")).IsFailure);
        Assert.True(InscriptionStationTransitions.Choose(opened, runtime, new(offer.OfferId, "foreign")).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(noMoney));
        Assert.Empty(opened.Loadouts!.Inventories.Values.SelectMany(inventory => inventory.Items));
    }

    [Fact]
    public void FrozenEligibilityFiltersOffersAndRejectsDirectAcquisition()
    {
        var (run, runtime) = Setup();
        var gated = new UnlockTargetDefinition { Kind = UnlockTargetKind.Inscription, DefinitionId = "focus" };
        run = run with { Eligibility = new() { GatedTargets = [gated], AvailableTargets = [], EmptyPools = EmptyEligiblePoolPolicy.AllowEmpty } };
        var offered = InscriptionStationTransitions.Open(run, runtime, new("alice", "reward")).Value;
        Assert.Empty(offered.Loadouts!.Offers!.Values.Single().Options);
        Assert.True(InscriptionAcquisition.Grant(run, runtime, "alice", "focus", "bypass").IsFailure);
        Assert.Equal("inscriptions", gated.ContentKind);
    }

    [Fact]
    public void DirectedCraftingDoesNotDrawEntropyAndStartingItemsHaveExplicitProvenance()
    {
        var (run, runtime) = Setup(selection: InscriptionOfferSelection.Directed, starting: true);
        Assert.All(run.Loadouts!.Inventories.Values.SelectMany(inventory => inventory.Items), item => Assert.Equal("run-start", item.AcquisitionSource));
        var opened = InscriptionStationTransitions.Open(run, runtime, new("alice", "reward")).Value;
        Assert.Equal(run.Determinism.RandomState, opened.Determinism.RandomState);
        Assert.True(InscriptionStationTransitions.Validate([Station() with { Pool = Station().Pool.SetItem("foreign", 1) }], ["focus"]).IsFailure);
    }

    private static InscriptionStationDefinition Station() => new() { StationId = "reward", NodeIds = ["reward"],
        Pool = ImmutableSortedDictionary<string, int>.Empty.Add("focus", 1), OfferCount = 3, Costs = [new() { ResourceId = "gold", Amount = 3 }] };
    private static (RunState Run, ContentRuntime Runtime) Setup(InscriptionInventoryScope scope = InscriptionInventoryScope.Actor,
        InscriptionOfferSelection selection = InscriptionOfferSelection.Random, bool starting = false)
    {
        var policy = LoadoutTestContent.Policy(scope) with { Stations = [Station() with { Selection = selection }], StartingInscriptionIds = starting ? ["focus"] : null };
        var (run, runtime, _) = InscriptionLoadoutTests.Setup(policy);
        return (run with { CurrentNodeId = "reward", Map = new() { Nodes = [new() { NodeId = "reward", Activity = new()
            { Type = RunActivityType.InscriptionStation, DefinitionId = "reward" } }] }, ResourceState = new()
            { OwnerId = run.RunId.ToString(), Resources = new Dictionary<string, ResourcePool> { ["gold"] = ResourcePool.Materialize(new()
                { ResourceId = "gold", DisplayName = "Gold", DefaultMax = 100 }, 10) } } }, runtime);
    }
}
