using Core.CardZones;
using Core.Combat.Flow;
using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class GameplayQueriesTests
{
    [Fact]
    public void ZoneQueriesReadRealTopologyDeduplicateAndRejectMissingAddresses()
    {
        var address = new CardZoneAddress { ZoneId = "bank", OwnerId = "$run" };
        var topology = CardZoneTransitions.CreateEmpty([address]).Value;
        var run = new RunState { Deck = new() { Topology = topology } };
        var before = CanonicalJson.ComputeHash(run);
        QueryZone zone = new() { ZoneId = "bank", OwnerScope = CardZoneOwnerScope.RunOwner };
        var counted = GameplayQueries.Evaluate([new() { Variable = "available", Kind = GameplayQueryKind.SumZoneCounts, Zones = [zone, zone] }], run, null, "hero");
        Assert.Equal(0, counted.Value["available"]);
        Assert.True(GameplayQueries.Evaluate([new() { Variable = "bad", Kind = GameplayQueryKind.ZoneCount,
            Zones = [zone with { ZoneId = "absent" }] }], run, null, "hero").IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
    }
    [Fact]
    public void ActivationFactsAreDistinctFromRoundCounters()
    {
        var combat = ActivationSequenceReducerTests.State();
        combat = combat with { ActivationState = combat.ActivationState! with
            { Facts = combat.ActivationState.Facts.Add("command:draw-action", 1).Add("moved:draw-flow", 2) } };
        var values = GameplayQueries.Evaluate([
            new() { Variable = "drawn", Kind = GameplayQueryKind.ActivationMovedCount, ActionId = "draw-flow" },
            new() { Variable = "attempts", Kind = GameplayQueryKind.ActivationCommandCount, ActionId = "draw-action" }
        ], new RunState(), combat, "B").Value;
        Assert.Equal(2, values["drawn"]); Assert.Equal(1, values["attempts"]);
    }
}
