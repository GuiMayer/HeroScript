using Core.CardZones;
using Core.Determinism;
using Core.Run;

namespace Core.Tests;

internal static class TestCardZones
{
    public const string RunOwner = "$run";

    public static CardZoneSystemDefinition Graph(
        IReadOnlyList<string> zoneIds,
        string? playableZoneId = null) => new()
    {
        CardZoneSystemId = "test-zones",
        Zones = zoneIds.Select(zoneId => new CardZoneDefinition
        {
            ZoneId = zoneId,
            OwnerScope = CardZoneOwnerScope.RunOwner,
            Ordering = CardZoneOrdering.Ordered,
            AllowsCardPlay = string.Equals(zoneId, playableZoneId, StringComparison.Ordinal)
        }).ToArray()
    };

    public static DeckState FromDefinitions(
        params (string ZoneId, IReadOnlyList<string> DefinitionIds)[] batches)
    {
        var graph = Graph(batches.Select(batch => batch.ZoneId).Distinct(StringComparer.Ordinal).ToArray());
        var result = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(graph).Value,
            new CardZoneBootstrapPlan
            {
                RunOwnerId = RunOwner,
                Batches = batches.Select(batch => new CardZoneInitialBatch
                {
                    ZoneId = batch.ZoneId,
                    OwnerId = RunOwner,
                    DefinitionIds = batch.DefinitionIds
                }).ToArray()
            }, DeterministicContext.Create(1, "test"));
        if (result.IsFailure) throw new InvalidOperationException(result.Error);
        return new DeckState { Topology = result.Value.State };
    }

    public static DeckState WithInstance(string zoneId, CardInstanceState instance)
    {
        var address = new CardZoneAddress { ZoneId = zoneId, OwnerId = RunOwner };
        return new DeckState
        {
            Topology = new CardZoneTopologyState
            {
                Instances = new Dictionary<Guid, CardInstanceState>
                {
                    [instance.CardInstanceId] = instance with { OwnerId = RunOwner }
                },
                Zones = new Dictionary<string, CardZoneState>
                {
                    [address.Key] = new() { Address = address, InstanceIds = [instance.CardInstanceId] }
                }
            }
        };
    }
}
