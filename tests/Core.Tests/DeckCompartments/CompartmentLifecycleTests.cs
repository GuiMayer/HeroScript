using System.Text.Json;
using Core.Determinism;
using Core.Run;
using Core.Run.DeckCompartments;
using Xunit;

namespace Core.Tests.DeckCompartments;

public sealed class CompartmentLifecycleTests
{
    [Fact]
    public void CommandUsesCanonicalCodecAndRejectsArbitraryComponents()
    {
        var codec = new GameplayCommandCodec(GameplayCommandDescriptors.All);
        var identity = new RunCommandIdentity(CompartmentPlannerTests.First, RunCommandTypes.ApplyDeckCompartments, 1, 0);
        var valid = codec.Decode(new(identity, JsonSerializer.SerializeToElement(new { actorId = "hero", allocations = new[] {
            new { cardInstanceId = CompartmentPlannerTests.First, compartmentId = "utility" } } })));
        Assert.True(valid.IsSuccess);
        Assert.IsType<ApplyDeckCompartmentsCommand>(valid.Value.Payload);
        Assert.True(codec.Decode(new(identity, JsonSerializer.SerializeToElement(new { actorId = "hero", allocations = new object[0], components = new object[0] }))).IsFailure);
    }

    [Fact]
    public void DestructionCleansOnlyMissingInstancesAndBranchEvolutionIsIndependent()
    {
        var run = DeckCompartmentPlanner.Plan(CompartmentPlannerTests.Run, CompartmentFixture.Runtime, "hero",
            [new(CompartmentPlannerTests.First, "utility"), new(CompartmentPlannerTests.Second, "other")]).Value.Candidate;
        var original = CanonicalJson.ComputeHash(run);
        var branch = run with { Deck = run.Deck with { Topology = run.Deck.Topology with {
            Instances = run.Deck.Topology.Instances.Where(p => p.Key != CompartmentPlannerTests.First).ToDictionary() } } };
        var cleaned = DeckCompartmentPlanner.ReconcileDestroyedCards(branch);
        Assert.Single(cleaned.DeckCompartments!.Actors["hero"].Allocations);
        Assert.Equal(2, run.DeckCompartments!.Actors["hero"].Allocations.Count);
        Assert.Equal(original, CanonicalJson.ComputeHash(run));
        Assert.Same(run.Determinism, cleaned.Determinism);
        Assert.Same(run, DeckCompartmentPlanner.ReconcileDestroyedCards(run));
    }
}
