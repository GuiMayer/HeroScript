using System.Collections.Immutable;
using System.Text.Json;
using Core.CardZones;
using Core.Combat.Models;
using Core.Determinism;
using Core.Run;
using Core.Run.DeckCompartments;
using Xunit;

namespace Core.Tests.DeckCompartments;

public sealed class CompartmentPlannerTests
{
    internal static readonly Guid First = Guid.Parse("10000000-0000-4000-8000-000000000001");
    internal static readonly Guid Second = Guid.Parse("10000000-0000-4000-8000-000000000002");
    internal static RunState Run => new() { ConfigName = "test", PlayerEntityId = "hero",
        PlayerEntity = new() { InstanceId = "hero", Components = new Dictionary<string, EntityComponentState> {
            ["build"] = new DeckCompartmentProfileComponentState { ComponentId = "build", ProfileId = "layout" } } },
        Determinism = DeterministicContext.Create(7, CompartmentFixture.Runtime.Manifest.Revision),
        ResolvedMode = new() { Definition = new() { DeckCompartmentPolicyId = "outside" } },
        Deck = new() { Topology = new() { Instances = new Dictionary<Guid, CardInstanceState> {
            [First] = new() { CardInstanceId = First, DefinitionId = "card" },
            [Second] = new() { CardInstanceId = Second, DefinitionId = "card" } } } } };

    [Fact]
    public void AtomicSwapAndNeutralizeDoNotTouchZonesLedgerOrRng()
    {
        var source = Run;
        var before = CanonicalJson.ComputeHash(source);
        var plan = DeckCompartmentPlanner.Plan(source, CompartmentFixture.Runtime, "hero", [new(First, "utility"), new(Second, "other")]);
        Assert.True(plan.IsSuccess, plan.IsFailure ? plan.Error : null);
        var swap = DeckCompartmentPlanner.Plan(plan.Value.Candidate, CompartmentFixture.Runtime, "hero", [new(First, "other"), new(Second, "utility")]);
        Assert.True(swap.IsSuccess);
        Assert.Same(source.Deck, swap.Value.Candidate.Deck);
        Assert.Same(source.Determinism, swap.Value.Candidate.Determinism);
        Assert.Equal(before, CanonicalJson.ComputeHash(source));
        Assert.Empty(DeckCompartmentPlanner.Plan(swap.Value.Candidate, CompartmentFixture.Runtime, "hero", []).Value.After.Allocations);
    }

    [Fact]
    public void DisabledCombatForeignDuplicateAndCapacityFailuresPreserveSource()
    {
        var source = Run;
        var hash = CanonicalJson.ComputeHash(source);
        Assert.True(DeckCompartmentPlanner.Plan(source with { ResolvedMode = null }, CompartmentFixture.Runtime, "hero", []).IsFailure);
        Assert.True(DeckCompartmentPlanner.Plan(source with { ActiveEncounterId = First }, CompartmentFixture.Runtime, "hero", []).IsFailure);
        Assert.True(DeckCompartmentPlanner.Plan(source, CompartmentFixture.Runtime, "other", []).IsFailure);
        Assert.True(DeckCompartmentPlanner.Plan(source, CompartmentFixture.Runtime, "hero", [new(First, "utility"), new(First, "other")]).IsFailure);
        Assert.True(DeckCompartmentPlanner.Plan(source, CompartmentFixture.Runtime, "hero", [new(First, "other"), new(Second, "other")]).IsFailure);
        Assert.True(DeckCompartmentPlanner.Plan(source, CompartmentFixture.Runtime, "hero", [new(Guid.Empty, "utility")]).IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(source));
    }

    [Fact]
    public void OrderingAndSerializationAreStableAndAbsenceIsOmitted()
    {
        var a = DeckCompartmentPlanner.Plan(Run, CompartmentFixture.Runtime, "hero", [new(First, "utility"), new(Second, "other")]).Value.Candidate;
        var b = DeckCompartmentPlanner.Plan(Run, CompartmentFixture.Runtime, "hero", [new(Second, "other"), new(First, "utility")]).Value.Candidate;
        Assert.Equal(CanonicalJson.ComputeHash(a), CanonicalJson.ComputeHash(b));
        Assert.DoesNotContain("deckCompartments", CanonicalJson.Serialize(Run));
        var restored = JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(a))!;
        Assert.Equal(CanonicalJson.ComputeHash(a), CanonicalJson.ComputeHash(restored));
    }
}
