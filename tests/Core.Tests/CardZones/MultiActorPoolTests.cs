using System.Text.Json;
using Core.CardZones;
using Core.Determinism;
using Core.Math;
using Core.Combat.Models;
using Core.Content;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class MultiActorPoolTests
{
    internal static CardZoneSystemDefinition Graph()
    {
        var assembly = typeof(MultiActorPoolTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("participant-pool.json", StringComparison.Ordinal)))!;
        return JsonSerializer.Deserialize<CardZoneSystemDefinition>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    internal static RunState Seed()
    {
        var graph = Graph();
        var seed = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(graph).Value,
            new() { RunOwnerId = "$run", ActorIds = ["hero"], Batches = [new()
                { ZoneId = "pool", OwnerId = "$run", DefinitionIds = ["one", "two", "three"] }] },
            DeterministicContext.Create(74, "pinned-revision")).Value;
        var run = new RunState { PlayerEntityId = "hero", Deck = new() { Topology = seed.State },
            Determinism = seed.Context, ResolvedMode = new() { CardZoneSystem = graph } };
        return run with { Deck = run.Deck with { Topology = CardZoneRunFlowDispatcher.EnsureParticipants(run, ["hero", "dealer"]).Value.State } };
    }

    internal static RunState Boundary(RunState run, string actor, string trigger)
    {
        var executor = new CardZoneFlowExecutor(new CardZoneRuntimeRuleEvaluator(
            Mock.Of<IRuntimeFormulaEvaluator>(), Mock.Of<ICardZoneCardMetadataResolver>()));
        var result = CardZoneRunFlowDispatcher.Execute(executor, run, run.Deck, run.Determinism, trigger, actor);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return run with { Deck = run.Deck with { Topology = result.Value.State }, Determinism = result.Value.Context };
    }

    [Fact]
    public void JsonFlowsDealFromOnePhysicalPoolOnlyInTheInvokedActorContext()
    {
        var original = Seed(); var before = CanonicalJson.ComputeHash(original);
        var hero = Boundary(original, "hero", "activation.started");
        Assert.Empty(hero.Deck.Topology.GetZone("pocket", "dealer")!.InstanceIds);
        var both = Boundary(hero, "dealer", "activation.started");
        var heroCard = Assert.Single(both.Deck.Topology.GetZone("pocket", "hero")!.InstanceIds);
        var dealerCard = Assert.Single(both.Deck.Topology.GetZone("pocket", "dealer")!.InstanceIds);
        Assert.Single(both.Deck.Topology.GetZone("pool", "$run")!.InstanceIds);
        Assert.Equal(3, both.Deck.Topology.Zones.Values.SelectMany(zone => zone.InstanceIds).Distinct().Count());
        Assert.True(CardZonePlaySource.Contains(both, "hero", heroCard));
        Assert.True(CardZonePlaySource.Contains(both, "dealer", dealerCard));
        Assert.False(CardZonePlaySource.Contains(both, "hero", dealerCard));
        Assert.False(CardZonePlaySource.Contains(both, "dealer", heroCard));
        var executor = new CardPlayExecutor(Mock.Of<IContentRuntimeResolver>(), Mock.Of<ICardContentCompiler>(),
            Mock.Of<IEffectiveCardResolver>(), Mock.Of<ICardPlayEvaluator>(), Mock.Of<IEffectTriggerExecutor>());
        var denied = executor.Execute(new() { Run = both, Combat = new CombatState(), ActorId = "hero", CardInstanceId = dealerCard });
        Assert.True(denied.IsFailure); Assert.Contains("not in a playable zone", denied.Error);
        Assert.Equal(before, CanonicalJson.ComputeHash(original));
    }

    [Theory]
    [InlineData(CardZoneVisibility.Owner, false)]
    [InlineData(CardZoneVisibility.All, true)]
    public void VisibilityDoesNotGrantThePlayerOwnershipOfAdversaryCards(CardZoneVisibility visibility, bool visible)
    {
        var run = Boundary(Boundary(Seed(), "hero", "activation.started"), "dealer", "activation.started");
        var graph = run.ResolvedMode!.CardZoneSystem!;
        run = run with { ResolvedMode = run.ResolvedMode with { CardZoneSystem = graph with
            { Zones = graph.Zones.Select(zone => zone.ZoneId == "pocket" ? zone with
                { Visibility = new() { Contents = visibility, Order = CardZoneOrderVisibility.Visible } } : zone).ToArray() } } };
        var projection = CardZoneReadModel.Project(run);
        var dealer = projection.Zones.Single(zone => zone.ZoneId == "pocket" && zone.OwnerId == "dealer");
        Assert.Equal(1, dealer.Count); Assert.Equal(visible, dealer.ContentsVisible);
        Assert.Equal(visible ? 1 : 0, dealer.Cards.Count); Assert.False(dealer.PlayerCanPlayFrom);
        Assert.True(projection.Zones.Single(zone => zone.ZoneId == "pocket" && zone.OwnerId == "hero").PlayerCanPlayFrom);
        Assert.True(projection.Zones.Single(zone => zone.ZoneId == "table").PlayerCanPlayFrom);
        Assert.False(projection.Zones.Single(zone => zone.ZoneId == "pool").PlayerCanPlayFrom);
    }

    [Fact]
    public void ReusedActorKeepsCardsUntilAnExplicitJsonCleanupAndRoundTripsWithoutMutation()
    {
        var dealt = Boundary(Seed(), "dealer", "activation.started");
        var card = Assert.Single(dealt.Deck.Topology.GetZone("pocket", "dealer")!.InstanceIds);
        var provisioned = CardZoneRunFlowDispatcher.EnsureParticipants(dealt, ["hero", "dealer", "later"]).Value;
        Assert.Equal(card, Assert.Single(provisioned.State.GetZone("pocket", "dealer")!.InstanceIds));
        var next = dealt with { Deck = dealt.Deck with { Topology = provisioned.State } };
        var hash = CanonicalJson.ComputeHash(next);
        var serialized = JsonSerializer.Serialize(next);
        var restored = JsonSerializer.Deserialize<RunState>(serialized)!;
        Assert.Equal(hash, CanonicalJson.ComputeHash(restored));
        Assert.Empty(CardZoneRunFlowDispatcher.EnsureParticipants(restored, ["later", "dealer", "hero"]).Value.CreatedAddresses);
        var cleaned = Boundary(restored, "dealer", "test.cleanup");
        Assert.Empty(cleaned.Deck.Topology.GetZone("pocket", "dealer")!.InstanceIds);
        Assert.Equal(3, cleaned.Deck.Topology.GetZone("pool", "$run")!.InstanceIds.Count);
        Assert.Equal(hash, CanonicalJson.ComputeHash(next));
        Assert.Equal(card, Assert.Single(dealt.Deck.Topology.GetZone("pocket", "dealer")!.InstanceIds));
    }

    [Fact]
    public void SameRevisionSeedAndBoundariesReproduceTheWholeImmutableRun()
    {
        string? baseline = null;
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var run = Seed(); var before = CanonicalJson.ComputeHash(run);
            var provisioned = CardZoneRunFlowDispatcher.EnsureParticipants(run, ["later", "hero", "dealer"]).Value;
            Assert.Equal(before, CanonicalJson.ComputeHash(run));
            run = run with { Deck = run.Deck with { Topology = provisioned.State } };
            foreach (var actor in new[] { "hero", "dealer", "later" }) run = Boundary(run, actor, "activation.started");
            var hash = CanonicalJson.ComputeHash(run);
            baseline ??= hash;
            Assert.Equal(baseline, hash);
        }
    }

    [Fact]
    public void ProvisioningUsesOnlyTheGraphPinnedInTheRun()
    {
        var pinned = Seed();
        var latestGraph = Graph() with { Zones = Graph().Zones.Concat([new CardZoneDefinition
            { ZoneId = "new-revision-only", OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered }]).ToArray() };
        var pinnedResult = CardZoneRunFlowDispatcher.EnsureParticipants(pinned, ["hero", "dealer", "later"]).Value;
        Assert.Null(pinnedResult.State.GetZone("new-revision-only", "later"));
        var explicitlyRebound = pinned with { ResolvedMode = pinned.ResolvedMode! with { CardZoneSystem = latestGraph } };
        Assert.NotNull(CardZoneRunFlowDispatcher.EnsureParticipants(explicitlyRebound, ["later"]).Value.State.GetZone("new-revision-only", "later"));
        Assert.Equal("pinned-revision", pinned.Determinism.ContentRevision);
        Assert.Null(pinned.Deck.Topology.GetZone("pocket", "later"));
    }
}
