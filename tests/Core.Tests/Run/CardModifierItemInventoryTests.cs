using System.Text.Json;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Core.Tests.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardModifierItemInventoryTests
{
    [Fact]
    public async Task CatalogRequiresExistingTransformationAndExplicitPolicy()
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var id = runtime.GetDefinitions("card-upgrades").Keys.First();
        var item = new CardModifierItemDefinition { ItemDefinitionId = "modifier", UpgradeId = id, Use = CardModifierItemUse.Consume };
        Assert.True(item.Validate(runtime).IsSuccess);
        Assert.True((item with { Use = null }).Validate(runtime).IsFailure);
        Assert.True((item with { UpgradeId = "missing" }).Validate(runtime).IsFailure);
        Assert.True((item with { Use = (CardModifierItemUse)99 }).Validate(runtime).IsFailure);
    }
    [Fact]
    public void OwnershipRoundTripsWithoutChangingCardsAndRejectsDuplicateUnits()
    {
        var run = new RunState { Deck = TestCardZones.FromDefinitions(("collection", new[] { "strike" })) };
        var deckHash = CanonicalJson.ComputeHash(run.Deck);
        var item = new CardModifierItemInstance { ItemInstanceId = Guid.NewGuid(), ItemDefinitionId = "fire", AcquisitionRevision = "revision",
            AcquisitionSource = "shop:1:offer:1", AcquisitionSequence = 3 };
        var empty = new CardModifierItemInventory();
        var inventory = empty.Add(item);
        Assert.True(inventory.IsSuccess);
        Assert.Empty(empty.Items);
        Assert.True(inventory.Value.Add(item).IsFailure);
        var owned = run with { CardModifierItems = inventory.Value };
        Assert.Equal(deckHash, CanonicalJson.ComputeHash(owned.Deck));
        var json = JsonSerializer.Serialize(owned);
        var restored = JsonSerializer.Deserialize<RunState>(json)!;
        Assert.Equal(CanonicalJson.ComputeHash(owned), CanonicalJson.ComputeHash(restored));
        Assert.Equal(CardModifierItemState.Available, restored.CardModifierItems!.Items[0].State);
        Assert.DoesNotContain("CardModifierItems", JsonSerializer.Serialize(run));
        Assert.True(empty.Add(item with { ItemInstanceId = Guid.Empty }).IsFailure);
        Assert.True(empty.Add(item with { State = CardModifierItemState.Bound }).IsFailure);
        Assert.True(empty.Add(item with { CardInstanceId = Guid.NewGuid() }).IsFailure);
    }
}
