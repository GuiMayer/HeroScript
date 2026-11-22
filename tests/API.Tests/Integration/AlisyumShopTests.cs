using API.Tests.Helpers;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class AlisyumShopTests
{
    [Fact]
    public async Task PublishedClassShopsGenerateAndRerollEligibleCardsAndRunes()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var manager = Assert.IsType<RunManager>(factory.Services.GetRequiredService<IRunManager>());

        foreach (var (runDefinition, mode, shopId, hero, cardPrefix) in new[]
        {
            ("alisyum_chapter_1", "alisyum_campaign", "alisyum_bazaar_pyro", "pyromancer", "pyro_"),
            ("alisyum_servo_run", "alisyum_servo_campaign", "alisyum_bazaar_servo", "baron_servant", "baron_"),
            ("alisyum_gambler_run", "alisyum_gambler_campaign", "alisyum_bazaar_gambler", "gambler", "gambler_"),
            ("alisyum_renegado_run", "alisyum_renegado_campaign", "alisyum_bazaar_renegado", "vitreous_renegade", "vitreous_")
        })
        {
            // Use the real published content and a run per class. Invoke the shop
            // transitions directly to isolate the offer policy from map progression.
            // The actor instance ID differs from its definition ID, as in client runs.
            var runId = await game.StartRunAsync("alisyum", runDefinition, "player", 7123, mode);
            var before = manager.GetRun(runId).Value;
            Assert.Equal(hero, before.PlayerEntity!.DefinitionId);
            var gold = before.ResourceState.Current("gold");

            var created = manager.CreateShop(runId, shopId);
            Assert.True(created.IsSuccess, created.IsFailure ? $"{hero}: {created.Error}" : null);
            AssertOffers(created.Value);
            Assert.Equal(gold, manager.GetRun(runId).Value.ResourceState.Current("gold"));

            var reroll = manager.RerollShop(runId, created.Value.ShopInstanceId);
            Assert.True(reroll.IsSuccess, reroll.IsFailure ? $"{hero}: {reroll.Error}" : null);
            AssertOffers(reroll.Value);
            Assert.Equal(1, reroll.Value.RerollsUsed);
            Assert.Equal(gold - 15, manager.GetRun(runId).Value.ResourceState.Current("gold"));

            void AssertOffers(ShopState shop)
            {
                var cards = shop.Items.Where(item => item.CardId != null).ToArray();
                Assert.Equal(5, cards.Length);
                Assert.All(cards, item => Assert.StartsWith(cardPrefix, item.CardId!));
                Assert.Equal(5, cards.Select(item => item.CardId).Distinct().Count());
                Assert.Contains(shop.Items, item => item.ModifierItemDefinitionId == "obor_overwrite_ember_item");
                Assert.Contains(shop.Items, item => item.ModifierItemDefinitionId == "obor_overwrite_fragment_item");
                Assert.All(shop.Items, item => Assert.False(item.Purchased));
            }
        }
    }
}
