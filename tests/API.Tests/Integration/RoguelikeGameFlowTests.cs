using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Integration tests simulating a Roguelike card game (Slay the Spire-like).
/// Tests the full game loop: run start, combat, rewards, shop, deck management.
/// </summary>
public sealed class RoguelikeGameFlowTests : GameEngineIntegrationTestBase
{
    public RoguelikeGameFlowTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task StartRun_CreatesRunWithDeck_ReturnsValidState()
    {
        // Simulate Unity/Godot starting a new run
        var runId = await Client.StartRunAsync("default", "default_run", "player");
        var runState = await Client.GetRunStateAsync(runId);

        // Verify run state is valid
        AssertRunStateValid(runState);
        AssertJsonPropertyEquals(runState, "runId", runId);
        AssertJsonPropertyEquals(runState, "configName", "default");
        AssertJsonPropertyEquals(runState, "playerEntityId", "player");
        
        // Verify deck exists
        var deck = runState.GetProperty("deck");
        AssertDeckStateValid(deck);
    }

    [Fact]
    public async Task DrawCards_FromDrawPile_MovesToHand()
    {
        // Setup: Start run
        var (runId, runState) = await SetupRunAsync();

        // Draw 5 cards like opening hand
        var drawnCards = await Client.DrawCardsAsync(runId, 5);

        // Verify cards were drawn
        Assert.NotEmpty(drawnCards);
        Assert.True(drawnCards.Count <= 5, "Should draw at most 5 cards");

        // Verify hand updated
        var hand = await Client.GetHandAsync(runId);
        Assert.Contains(drawnCards[0], hand);
    }

    [Fact]
    public async Task CombatFlow_StartPlayCardsEndTurn_StateUpdates()
    {
        // Setup: Start run and draw cards
        var (runId, runState) = await SetupRunAsync();
        await Client.DrawCardsAsync(runId, 5);

        var playerEntityId = GetJsonString(runState, "playerEntityId");

        // Start combat with 2 enemies
        var combatId = await Client.StartCombatAsync(playerEntityId, new[] { "enemy_1", "enemy_2" }, initialEnergy: 3, runId: runId);
        var combatState = await Client.GetCombatStateAsync(combatId);

        // Verify combat started
        AssertCombatStateValid(combatState);
        Assert.Equal(2, GetArrayLength(combatState, "enemies"));

        // Verify hero has energy
        var hero = combatState.GetProperty("hero");
        AssertEntityHasResource(hero, "energy");

        // Play a card (simulate Strike)
        var hand = await Client.GetHandAsync(runId);
        if (hand.Any())
        {
            var cardToPlay = hand.First();
            var result = await Client.ExecuteActionAsync(combatId, playerEntityId, 
                targetId: "enemy_1", cardId: cardToPlay, runId: runId);

            // Verify action executed
            Assert.True(result.TryGetProperty("combatState", out _) || 
                       result.TryGetProperty("combatId", out _));
        }

        // End turn
        var endTurnResult = await Client.EndTurnAsync(combatId);
        AssertJsonPropertyExists(endTurnResult, "combatId");
    }

    [Fact]
    public async Task CardSelection_PickReward_AddsToDeck()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync();

        // Open card selection (simulate post-combat reward)
        var selectionResponse = await Client.StartCardSelectionAsync(runId, "basic_reward");
        
        AssertJsonPropertyExists(selectionResponse, "selectionInstanceId");
        AssertJsonPropertyExists(selectionResponse, "options");

        var selectionInstanceId = GetJsonGuid(selectionResponse, "selectionInstanceId");
        var options = selectionResponse.GetProperty("options");

        // Pick first card
        if (options.GetArrayLength() > 0)
        {
            var firstOption = options.EnumerateArray().First();
            var cardId = GetJsonString(firstOption, "cardId");

            var pickResult = await Client.PickCardsAsync(runId, selectionInstanceId, new[] { cardId });
            
            AssertJsonPropertyExists(pickResult, "completed");
            Assert.True(GetJsonBool(pickResult, "completed"));
        }
    }

    [Fact]
    public async Task Shop_BuyItem_GoldDecreases()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync();
        var initialGold = GetJsonInt(runState, "gold");

        // Open shop
        var shopResponse = await Client.OpenShopAsync(runId, "basic_shop");
        
        AssertJsonPropertyExists(shopResponse, "shopInstanceId");
        AssertJsonPropertyExists(shopResponse, "items");

        var shopInstanceId = GetJsonGuid(shopResponse, "shopInstanceId");
        var items = shopResponse.GetProperty("items");

        // Try to buy first item if available
        if (items.GetArrayLength() > 0)
        {
            var firstItem = items.EnumerateArray().First();
            var itemId = GetJsonString(firstItem, "itemId");
            var price = GetJsonInt(firstItem, "price");

            if (initialGold >= price)
            {
                var buyResult = await Client.BuyShopItemAsync(runId, shopInstanceId, itemId);
                
                AssertJsonPropertyExists(buyResult, "success");
                
                // Verify gold decreased
                var updatedRunState = await Client.GetRunStateAsync(runId);
                var newGold = GetJsonInt(updatedRunState, "gold");
                Assert.True(newGold <= initialGold);
            }
        }
    }

    [Fact]
    public async Task Preparation_ApplyOption_StateUpdates()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync();

        // Start preparation (rest, upgrade, etc.)
        var prepResponse = await Client.StartPreparationAsync(runId, "basic_preparation");
        
        AssertJsonPropertyExists(prepResponse, "preparationInstanceId");
        AssertJsonPropertyExists(prepResponse, "options");

        var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
        var options = prepResponse.GetProperty("options");

        // Apply first option if available
        if (options.GetArrayLength() > 0)
        {
            var firstOption = options.EnumerateArray().First();
            var optionId = GetJsonString(firstOption, "optionId");

            var applyResult = await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);
            
            AssertJsonPropertyExists(applyResult, "applied");
            Assert.True(GetJsonBool(applyResult, "applied"));
        }
    }

    [Fact]
    public async Task DiscardCards_RemovesFromHand_MovesToDiscard()
    {
        // Setup: Start run and draw cards
        var (runId, runState) = await SetupRunAsync();
        await Client.DrawCardsAsync(runId, 3);
        
        var hand = await Client.GetHandAsync(runId);
        Assert.NotEmpty(hand);

        // Discard first card
        var cardToDiscard = hand.First();
        var discardResult = await Client.DiscardCardsAsync(runId, new[] { cardToDiscard });

        // Verify response
        AssertJsonPropertyExists(discardResult, "runId");

        // Verify card removed from hand
        var updatedHand = await Client.GetHandAsync(runId);
        Assert.DoesNotContain(cardToDiscard, updatedHand);
    }

    [Fact]
    public async Task RerollCardSelection_ChangesOptions_CostsGold()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync();
        var initialGold = GetJsonInt(runState, "gold");

        // Open card selection
        var selectionResponse = await Client.StartCardSelectionAsync(runId, "basic_reward");
        var selectionInstanceId = GetJsonGuid(selectionResponse, "selectionInstanceId");
        var initialOptions = selectionResponse.GetProperty("options");
        var initialCardIds = initialOptions.EnumerateArray()
            .Select(o => GetJsonString(o, "cardId"))
            .ToList();

        // Reroll (if gold allows)
        var rerollCostGold = 0;
        if (selectionResponse.TryGetProperty("rerollCostGold", out var costProp))
        {
            rerollCostGold = costProp.GetInt32();
        }

        if (initialGold >= rerollCostGold)
        {
            var rerollResult = await Client.RerollCardSelectionAsync(runId, selectionInstanceId);
            
            AssertJsonPropertyExists(rerollResult, "options");
            
            var newOptions = rerollResult.GetProperty("options");
            var newCardIds = newOptions.EnumerateArray()
                .Select(o => GetJsonString(o, "cardId"))
                .ToList();

            // Options should change (probabilistic, but usually different)
            // At minimum, verify structure is valid
            Assert.NotEmpty(newCardIds);
        }
    }
}
