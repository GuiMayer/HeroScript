using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
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
    public async Task NormalRun_CanInvokeAuthoredGameplayZoneFlowButNotToolFlow()
    {
        var runId = await Client.StartRunAsync();
        var before = await Client.GetRunStateAsync(runId);
        using var zonesBeforeResponse = await RawClient.GetAsync($"/api/v1/runs/{runId}/card-zones");
        var zonesBefore = await zonesBeforeResponse.Content.ReadFromJsonAsync<JsonElement>();
        var handBefore = zonesBefore.GetProperty("zones").EnumerateArray()
            .Single(zone => zone.GetProperty("zoneId").GetString() == "hand")
            .GetProperty("count").GetInt32();

        using var drawn = await RawClient.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "INVOKE_CARD_ZONE_GAMEPLAY_FLOW",
            expectedSequence = before.GetProperty("sequence").GetInt32(),
            expectedStep = before.GetProperty("step").GetUInt64(),
            payload = new { flowId = "run.draw", requestedCount = 1 }
        });
        Assert.True(drawn.IsSuccessStatusCode, await drawn.Content.ReadAsStringAsync());

        using var zonesAfterResponse = await RawClient.GetAsync($"/api/v1/runs/{runId}/card-zones");
        var zonesAfter = await zonesAfterResponse.Content.ReadFromJsonAsync<JsonElement>();
        var handAfter = zonesAfter.GetProperty("zones").EnumerateArray()
            .Single(zone => zone.GetProperty("zoneId").GetString() == "hand")
            .GetProperty("count").GetInt32();
        Assert.Equal(handBefore + 1, handAfter);

        var selectedCard = zonesAfter.GetProperty("zones").EnumerateArray()
            .Single(zone => zone.GetProperty("zoneId").GetString() == "hand")
            .GetProperty("cards")[0].GetProperty("cardInstanceId").GetGuid();
        var afterDraw = await Client.GetRunStateAsync(runId);
        using var discarded = await RawClient.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "INVOKE_CARD_ZONE_GAMEPLAY_FLOW",
            expectedSequence = afterDraw.GetProperty("sequence").GetInt32(),
            expectedStep = afterDraw.GetProperty("step").GetUInt64(),
            payload = new { flowId = "run.discard", cardInstanceIds = new[] { selectedCard } }
        });
        Assert.True(discarded.IsSuccessStatusCode, await discarded.Content.ReadAsStringAsync());
        using var zonesDiscardResponse = await RawClient.GetAsync($"/api/v1/runs/{runId}/card-zones");
        var zonesDiscard = await zonesDiscardResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(handBefore, zonesDiscard.GetProperty("zones").EnumerateArray()
            .Single(zone => zone.GetProperty("zoneId").GetString() == "hand")
            .GetProperty("count").GetInt32());

        var current = await Client.GetRunStateAsync(runId);
        using var toolOnly = await RawClient.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "INVOKE_CARD_ZONE_GAMEPLAY_FLOW",
            expectedSequence = current.GetProperty("sequence").GetInt32(),
            expectedStep = current.GetProperty("step").GetUInt64(),
            payload = new { flowId = "tool.create-in-hand", requestedCount = 1 }
        });
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, toolOnly.StatusCode);
        Assert.Contains("does not allow invocation",
            await toolOnly.Content.ReadAsStringAsync());
        Assert.Equal(current.GetProperty("sequence").GetInt32(),
            (await Client.GetRunStateAsync(runId)).GetProperty("sequence").GetInt32());

        using var internalGrant = await RawClient.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "INVOKE_CARD_ZONE_GAMEPLAY_FLOW",
            expectedSequence = current.GetProperty("sequence").GetInt32(),
            expectedStep = current.GetProperty("step").GetUInt64(),
            payload = new { flowId = "grant.create-in-discard" }
        });
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, internalGrant.StatusCode);
        Assert.Contains("not player-invokable",
            await internalGrant.Content.ReadAsStringAsync());

        using var purposeBound = await RawClient.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "DRAW_CARDS",
            expectedSequence = current.GetProperty("sequence").GetInt32(),
            expectedStep = current.GetProperty("step").GetUInt64(),
            payload = new { count = 1 }
        });
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, purposeBound.StatusCode);
        Assert.Contains("INVOKE_CARD_ZONE_GAMEPLAY_FLOW",
            await purposeBound.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CardZones_ExposeGraphPresentationAndCurrentTopology()
    {
        var runId = await Client.StartRunAsync();
        using var response = await RawClient.GetAsync($"/api/v1/runs/{runId}/card-zones");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var snapshot = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(runId, snapshot.GetProperty("runId").GetGuid());
        Assert.Equal(1, snapshot.GetProperty("sequence").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(snapshot.GetProperty("topologyHash").GetString()));
        var zones = snapshot.GetProperty("zones").EnumerateArray().ToArray();
        Assert.Equal(["discard", "draw", "exhaust", "hand"],
            zones.Select(zone => zone.GetProperty("zoneId").GetString()!).ToArray());
        var hand = zones.Single(zone => zone.GetProperty("zoneId").GetString() == "hand");
        Assert.Equal(5, hand.GetProperty("count").GetInt32());
        Assert.True(hand.GetProperty("contentsVisible").GetBoolean());
        Assert.True(hand.GetProperty("allowsCardPlay").GetBoolean());
        Assert.True(hand.GetProperty("orderVisible").GetBoolean());
        Assert.Equal(5, hand.GetProperty("cards").GetArrayLength());
        Assert.Equal("playable_cards", hand.GetProperty("presentation")
            .GetProperty("slot").GetString());
        var draw = zones.Single(zone => zone.GetProperty("zoneId").GetString() == "draw");
        Assert.False(draw.GetProperty("allowsCardPlay").GetBoolean());
        Assert.False(draw.GetProperty("orderVisible").GetBoolean());
    }

    [Fact]
    public async Task Sandbox_CanInvokeOnlyAuthorizedZoneToolFlows()
    {
        var runId = await Client.StartRunAsync(modeId: "combat_sandbox");
        var before = await Client.GetRunStateAsync(runId);
        using var created = await RawClient.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "INVOKE_CARD_ZONE_FLOW",
            expectedSequence = before.GetProperty("sequence").GetInt32(),
            expectedStep = before.GetProperty("step").GetUInt64(),
            payload = new
            {
                flowId = "tool.create-in-hand",
                cardDefinitionIds = new[] { "basic_attack", "defend" }
            }
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        using var zonesResponse = await RawClient.GetAsync($"/api/v1/runs/{runId}/card-zones");
        var zones = await zonesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var hand = zones.GetProperty("zones").EnumerateArray()
            .Single(zone => zone.GetProperty("zoneId").GetString() == "hand");
        Assert.Equal(7, hand.GetProperty("count").GetInt32());
        Assert.Contains(hand.GetProperty("cards").EnumerateArray(),
            card => card.GetProperty("definitionId").GetString() == "defend");

        var after = await Client.GetRunStateAsync(runId);
        var movedId = hand.GetProperty("cards").EnumerateArray()
            .Single(card => card.GetProperty("definitionId").GetString() == "defend")
            .GetProperty("cardInstanceId").GetGuid();
        using var moved = await RawClient.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "INVOKE_CARD_ZONE_FLOW",
            expectedSequence = after.GetProperty("sequence").GetInt32(),
            expectedStep = after.GetProperty("step").GetUInt64(),
            payload = new
            {
                flowId = "tool.move-hand-to-discard",
                cardInstanceIds = new[] { movedId }
            }
        });
        Assert.True(moved.IsSuccessStatusCode, await moved.Content.ReadAsStringAsync());
        after = await Client.GetRunStateAsync(runId);
        using var forbidden = await RawClient.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "INVOKE_CARD_ZONE_FLOW",
            expectedSequence = after.GetProperty("sequence").GetInt32(),
            expectedStep = after.GetProperty("step").GetUInt64(),
            payload = new { flowId = "run.initial-draw" }
        });
        Assert.False(forbidden.IsSuccessStatusCode);
        Assert.Equal(after.GetProperty("sequence").GetInt32(),
            (await Client.GetRunStateAsync(runId)).GetProperty("sequence").GetInt32());

        using var unknownCard = await RawClient.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "INVOKE_CARD_ZONE_FLOW",
            expectedSequence = after.GetProperty("sequence").GetInt32(),
            expectedStep = after.GetProperty("step").GetUInt64(),
            payload = new { flowId = "tool.create-in-draw", cardDefinitionIds = new[] { "not-published" } }
        });
        Assert.False(unknownCard.IsSuccessStatusCode);
        using var verified = await RawClient.PostAsync($"/api/v1/runs/{runId}/verify", null);
        Assert.True(verified.IsSuccessStatusCode, await verified.Content.ReadAsStringAsync());
        var proof = await verified.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(proof.GetProperty("isValid").GetBoolean());

        var standardId = await Client.StartRunAsync();
        var standard = await Client.GetRunStateAsync(standardId);
        using var disabled = await RawClient.PostAsJsonAsync($"/api/v1/runs/{standardId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "INVOKE_CARD_ZONE_FLOW",
            expectedSequence = standard.GetProperty("sequence").GetInt32(),
            expectedStep = standard.GetProperty("step").GetUInt64(),
            payload = new { flowId = "tool.create-in-hand", cardDefinitionIds = new[] { "basic_attack" } }
        });
        Assert.False(disabled.IsSuccessStatusCode);
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
        var combatId = await Client.StartCombatAsync(
            playerEntityId,
            new[] { "enemy_1", "enemy_2" },
            initialHeroResourceValues: new Dictionary<string, float> { ["energy"] = 3 },
            runId: runId);
        var combatState = await Client.GetCombatStateAsync(combatId);

        // Verify combat started
        AssertCombatStateValid(combatState);
        Assert.Equal(3, GetArrayLength(combatState, "actors"));

        // Verify hero has energy
        var hero = GetPlayerActor(combatState);
        AssertEntityHasResource(hero, "energy");

        // Play a card (simulate Strike)
        var hand = await Client.GetHandAsync(runId);
        Assert.NotEmpty(hand);
        var cardToPlay = hand.First();
        var result = await Client.ExecuteActionAsync(combatId, playerEntityId,
            targetId: "enemy_1", cardId: cardToPlay, runId: runId);

        // Verify action executed through the run-owned combat command gateway.
        AssertJsonPropertyEquals(result, "combatId", combatId);

        // End turn
        var endTurnResult = await Client.EndTurnAsync(combatId, runId);
        AssertJsonPropertyExists(endTurnResult, "combatId");
    }

    [Fact]
    public async Task CardSelection_PickReward_AddsToDeck()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync("combat_sandbox");

        // Open card selection (simulate post-combat reward)
        var selectionResponse = await Client.StartCardSelectionAsync(runId, "basic_reward");

        AssertJsonPropertyExists(selectionResponse, "selectionInstanceId");
        AssertJsonPropertyExists(selectionResponse, "options");

        var selectionInstanceId = GetJsonGuid(selectionResponse, "selectionInstanceId");
        var options = selectionResponse.GetProperty("options");

        Assert.NotEmpty(options.EnumerateArray());
        var firstOption = options.EnumerateArray().First();
        var cardId = GetJsonString(firstOption, "cardId");

        var pickResult = await Client.PickCardsAsync(runId, selectionInstanceId, new[] { cardId });

        AssertJsonPropertyExists(pickResult, "completed");
        Assert.True(GetJsonBool(pickResult, "completed"));
    }

    [Fact]
    public async Task Shop_BuyItem_GoldDecreases()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync("combat_sandbox");
        var initialGold = GetRunResource(runState, "gold");

        // Open shop
        var shopResponse = await Client.OpenShopAsync(runId, "basic_shop");

        AssertJsonPropertyExists(shopResponse, "shopInstanceId");
        AssertJsonPropertyExists(shopResponse, "items");

        var shopInstanceId = GetJsonGuid(shopResponse, "shopInstanceId");
        var items = shopResponse.GetProperty("items");

        Assert.NotEmpty(items.EnumerateArray());
        var firstItem = items.EnumerateArray().First();
        var itemId = GetJsonString(firstItem, "itemId");
        var price = GetResourceAmount(firstItem, "costs", "gold");
        Assert.True(initialGold >= price, "The shipped starting run must afford the first basic-shop item.");

        var buyResult = await Client.BuyShopItemAsync(runId, shopInstanceId, itemId);

        Assert.True(GetJsonBool(buyResult, "purchased"));

        // Verify the exact persisted economy transition.
        var updatedRunState = await Client.GetRunStateAsync(runId);
        var newGold = GetRunResource(updatedRunState, "gold");
        Assert.Equal(initialGold - price, newGold);
    }

    [Fact]
    public async Task Preparation_ApplyOption_StateUpdates()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync("combat_sandbox");

        // Start preparation (rest, upgrade, etc.)
        var prepResponse = await Client.StartPreparationAsync(runId, "basic_preparation");
        
        AssertJsonPropertyExists(prepResponse, "preparationInstanceId");
        AssertJsonPropertyExists(prepResponse, "options");

        var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
        var options = prepResponse.GetProperty("options");

        Assert.NotEmpty(options.EnumerateArray());
        var firstOption = options.EnumerateArray().First();
        var optionId = GetJsonString(firstOption, "optionId");

        var applyResult = await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);
        
        AssertJsonPropertyExists(applyResult, "applied");
        Assert.True(GetJsonBool(applyResult, "applied"));
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

        // Definition IDs can legitimately repeat; one deterministic instance
        // must move zones without requiring every copy to disappear.
        var updatedHand = await Client.GetHandAsync(runId);
        Assert.Equal(hand.Count - 1, updatedHand.Count);
        Assert.Equal(
            hand.Count(cardId => cardId == cardToDiscard) - 1,
            updatedHand.Count(cardId => cardId == cardToDiscard));
    }

    [Fact]
    public async Task RerollCardSelection_ChangesOptions_CostsGold()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync("combat_sandbox");
        var initialGold = GetRunResource(runState, "gold");

        // Open card selection
        var selectionResponse = await Client.StartCardSelectionAsync(runId, "basic_reward");
        var selectionInstanceId = GetJsonGuid(selectionResponse, "selectionInstanceId");
        var initialOptions = selectionResponse.GetProperty("options");
        Assert.NotEmpty(initialOptions.EnumerateArray());

        var rerollCostGold = GetResourceAmount(selectionResponse, "rerollCosts", "gold");
        var effectiveCost = selectionResponse.GetProperty("freeRerollsRemaining").GetInt32() > 0
            ? 0
            : rerollCostGold;
        Assert.True(initialGold >= effectiveCost);

        var rerollResult = await Client.RerollCardSelectionAsync(runId, selectionInstanceId);
        
        AssertJsonPropertyExists(rerollResult, "options");
        
        var newOptions = rerollResult.GetProperty("options");
        var newCardIds = newOptions.EnumerateArray()
            .Select(o => GetJsonString(o, "cardId"))
            .ToList();

        Assert.NotEmpty(newCardIds);
        var updatedRunState = await Client.GetRunStateAsync(runId);
        Assert.Equal(initialGold - effectiveCost, GetRunResource(updatedRunState, "gold"));
    }
}
