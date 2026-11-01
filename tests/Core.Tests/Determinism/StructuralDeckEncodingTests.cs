using System.Text;
using Core.CardZones;
using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.Determinism;

public sealed class StructuralDeckEncodingTests
{
    [Fact]
    public void MovingCardsReusesTheRegistryWithoutChangingReferenceBytesOrArrayOrder()
    {
        var first = Guid.Parse("9a4a1269-a313-438a-a3c1-59994468e001");
        var second = Guid.Parse("9a4a1269-a313-438a-a3c1-59994468e002");
        var deck = new DeckState { Topology = new()
        {
            Instances = new Dictionary<Guid, CardInstanceState>
            {
                [first] = new() { CardInstanceId = first, DefinitionId = "a", CreationOrdinal = 1 },
                [second] = new() { CardInstanceId = second, DefinitionId = "b", CreationOrdinal = 2 }
            }
        } };
        using var metrics = Core.Diagnostics.OperationalTiming.Begin();
        using var scope = CanonicalJson.BeginHashScope();
        long referenceBytes = 0;
        for (var index = 0; index < 100; index++)
        {
            var address = new CardZoneAddress { ZoneId = "arbitrary", OwnerId = "$run" };
            deck = deck with { Topology = deck.Topology with
            {
                Zones = new Dictionary<string, CardZoneState>
                {
                    [address.Key] = new() { Address = address, InstanceIds = index % 2 == 0 ? [first, second] : [second, first] }
                }
            } };
            var reference = CanonicalJson.Serialize(deck);
            referenceBytes += Encoding.UTF8.GetByteCount(reference);
            Assert.Equal(reference, Encoding.UTF8.GetString(CanonicalJson.Property("deck", deck).Bytes.Span));
            Assert.Equal(CanonicalJson.ComputeHash(new { deck, index }), CanonicalJson.ComputeObjectHash(
                CanonicalJson.Property("deck", deck), CanonicalJson.Property("index", index)));
        }
        Assert.True(metrics.Counters()["immutable_leaf_bytes"] < referenceBytes / 2,
            "Repeated movement must not re-encode the entire unchanged card registry.");
    }
}
