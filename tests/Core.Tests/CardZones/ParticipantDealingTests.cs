using Core.CardZones;
using Core.Combat.Models;
using Core.Determinism;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class ParticipantDealingTests
{
    [Theory]
    [InlineData(ParticipantDistribution.BatchPerActor)]
    [InlineData(ParticipantDistribution.RoundRobin)]
    public void ExplicitIterationDealsFourHandsAndPreservesTheSinglePhysicalPool(ParticipantDistribution distribution)
    {
        var graph = Graph(distribution); var system = CardZoneSystemCompiler.Compile(graph).Value;
        var seed = Seed(system, 108); var before = CanonicalJson.ComputeHash(seed.State);
        var result = new CardZoneFlowExecutor().Execute(system, seed.State, seed.Context, "setup", Context());
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        foreach (var id in new[] { "A", "B", "C", "D" }) Assert.Equal(7, result.Value.State.GetZone("pocket", id)!.InstanceIds.Count);
        Assert.Single(result.Value.State.GetZone("table", "$run")!.InstanceIds);
        Assert.Equal(79, result.Value.State.GetZone("pool", "$run")!.InstanceIds.Count);
        Assert.Equal(108, result.Value.State.Zones.Values.SelectMany(zone => zone.InstanceIds).Distinct().Count());
        Assert.Equal(seed.CreatedInstanceIds[distribution == ParticipantDistribution.RoundRobin ? 4 : 1], result.Value.State.GetZone("pocket", "A")!.InstanceIds[1]);
        Assert.Equal(before, CanonicalJson.ComputeHash(seed.State));
        var repeated = new CardZoneFlowExecutor().Execute(system, seed.State, seed.Context, "setup", Context()).Value;
        Assert.Equal(result.Value.Fingerprint, repeated.Fingerprint);
    }
    [Fact]
    public void FailureOnTheLastParticipantDoesNotPublishPartialDealing()
    {
        var system = CardZoneSystemCompiler.Compile(Graph(ParticipantDistribution.BatchPerActor)).Value;
        var seed = Seed(system, 27); var before = CanonicalJson.ComputeHash(seed.State);
        Assert.True(new CardZoneFlowExecutor().Execute(system, seed.State, seed.Context, "setup", Context()).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(seed.State));
        Assert.All(new[] { "A", "B", "C", "D" }, id => Assert.Empty(seed.State.GetZone("pocket", id)!.InstanceIds));
    }
    private static CardZoneFlowContext Context() => new() { Invocation = CardZoneFlowInvocation.Boundary, RunOwnerId = "$run", FlowOwnerId = "$run", ActiveActorId = "A",
        CombatSnapshot = new() { Actors = new[] { "D", "B", "A", "C" }.ToDictionary(id => id, id => new CombatActorState { InstanceId = id }) } };
    private static CardZoneBootstrapResult Seed(CompiledCardZoneSystem graph, int count) => CardZoneBootstrapper.Create(graph,
        new() { RunOwnerId = "$run", ActorIds = ["A", "B", "C", "D"], Batches = [new()
            { ZoneId = "pool", OwnerId = "$run", DefinitionIds = Enumerable.Repeat("card", count).ToArray() }] }, DeterministicContext.Create(73, "pinned")).Value;
    private static CardZoneSystemDefinition Graph(ParticipantDistribution distribution) => new() {
        CardZoneSystemId = "four-hands", Zones = [
            new() { ZoneId = "pool", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered },
            new() { ZoneId = "pocket", OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered, AllowsCardPlay = true },
            new() { ZoneId = "table", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered, Capacity = 1 }],
        Flows = [new() { FlowId = "setup", AllowedInvocations = [CardZoneFlowInvocation.Boundary], Steps = [
            new() { StepId = "deal-all", Operation = CardZoneOperation.ForEachParticipant, NestedFlowId = "take-one",
                Participants = new() { Order = ParticipantIterationOrder.Ordinal, Distribution = distribution, Repetitions = 7 } },
            new() { StepId = "open-table", Operation = CardZoneOperation.Move, SourceZoneId = "pool", TargetZoneId = "table",
                Selection = new() { Strategy = CardZoneSelectionStrategy.First, Count = 1 } }] },
            new() { FlowId = "take-one", AllowedInvocations = [CardZoneFlowInvocation.Boundary], Steps = [new() {
                StepId = "take", Operation = CardZoneOperation.Move, SourceZoneId = "pool", TargetZoneId = "pocket", TargetOwner = CardZoneOwnerBinding.IterationActor,
                Selection = new() { Strategy = CardZoneSelectionStrategy.First, Count = 1 } }] }]
    };
}
