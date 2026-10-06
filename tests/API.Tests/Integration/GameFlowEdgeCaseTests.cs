using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Integration tests for edge cases, error handling, and validation scenarios.
/// Tests system robustness and proper error responses.
/// </summary>
public sealed class GameFlowEdgeCaseTests : GameEngineIntegrationTestBase
{
    public GameFlowEdgeCaseTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetRunState_NonExistentRun_Returns404()
    {
        // Try to get state for non-existent run
        var nonExistentRunId = Guid.NewGuid();
        var response = await Client.GetRawResponseAsync($"/api/v1/runs/{nonExistentRunId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCombatState_NonExistentCombat_Returns404()
    {
        // Try to get state for non-existent combat
        var nonExistentCombatId = Guid.NewGuid();
        var response = await Client.GetRawResponseAsync($"/api/v1/combats/{nonExistentCombatId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CardZoneGameplayFlow_ExcessiveDraw_UsesAuthoredPartialPolicy()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync();

        // Try to draw more cards than exist in deck
        var response = await Client.PostRawAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            expectedSequence = GetJsonInt(runState, "sequence"),
            expectedStep = runState.GetProperty("step").GetUInt64(),
            type = "INVOKE_CARD_ZONE_GAMEPLAY_FLOW",
            payload = new { flowId = "run.draw", requestedCount = 1000 }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var zonesResponse = await RawClient.GetAsync($"/api/v1/runs/{runId}/card-zones");
        var zones = await zonesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var hand = zones.GetProperty("zones").EnumerateArray()
            .Single(zone => zone.GetProperty("zoneId").GetString() == "hand");
        Assert.InRange(hand.GetProperty("count").GetInt32(), 1, 10);
    }

    [Fact]
    public async Task ExecuteAction_InvalidTarget_ReturnsError()
    {
        var (runId, runState) = await SetupRunAsync();
        var playerEntityId = GetJsonString(runState, "playerEntityId");
        var combatId = await Client.StartCombatAsync(playerEntityId, new[] { "enemy_1" }, runId: runId);
        var combatState = await Client.GetCombatStateAsync(combatId);
        var cardInstanceId = await Client.GetPlayableCardInstanceIdAsync(runId, "basic_attack");

        // The run-owned command boundary must reject a target outside the combat.
        var response = await Client.PostRawAsync($"/api/v1/combats/{combatId}/commands", new
        {
            commandId = Guid.NewGuid(),
            expectedSequence = GetJsonInt((await Client.GetRunStateAsync(runId)), "sequence"),
            expectedStep = combatState.GetProperty("step").GetUInt64(),
            type = "PLAY_CARD",
            payload = new
            {
                actorId = playerEntityId,
                targetIds = new[] { "non_existent_enemy" },
                cardInstanceId
            }
        });

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("RULE_VIOLATION", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BuyShopItem_InsufficientGold_ReturnsError()
    {
        var (runId, _) = await SetupRunAsync("combat_sandbox");

        // Buy a published offer and a paid preparation option, leaving less
        // gold than every item in the next basic-shop offer.
        var shopResponse = await Client.OpenShopAsync(runId, "basic_shop");
        var shopInstanceId = GetJsonGuid(shopResponse, "shopInstanceId");
        var items = shopResponse.GetProperty("items");
        var availableGold = GetRunResource(await Client.GetRunStateAsync(runId), "gold");
        var firstItem = items.EnumerateArray().First(item =>
            GetResourceAmount(item, "costs", "gold") <= availableGold);
        await Client.BuyShopItemAsync(runId, shopInstanceId, GetJsonString(firstItem, "itemId"));

        while (GetRunResource(await Client.GetRunStateAsync(runId), "gold") >= 10)
        {
            var preparation = await Client.StartPreparationAsync(runId, "basic_preparation");
            var paidOption = preparation.GetProperty("options").EnumerateArray()
                .Single(option => GetResourceAmount(option, "costs", "gold") == 10);
            await Client.ApplyPreparationOptionAsync(
                runId,
                GetJsonGuid(preparation, "preparationInstanceId"),
                GetJsonString(paidOption, "optionId"));
        }

        var remainingGold = GetRunResource(await Client.GetRunStateAsync(runId), "gold");
        Assert.True(remainingGold < 10);

        var secondShop = await Client.OpenShopAsync(runId, "basic_shop");
        var expensiveItem = secondShop.GetProperty("items").EnumerateArray()
            .First(item => GetResourceAmount(item, "costs", "gold") > remainingGold);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => Client.BuyShopItemAsync(
                runId,
                GetJsonGuid(secondShop, "shopInstanceId"),
                GetJsonString(expensiveItem, "itemId")));
        Assert.Contains("422", exception.Message);
    }

    [Theory]
    [InlineData("/api/v1/combats/start")]
    [InlineData("/api/v1/combats/00000000-0000-0000-0000-000000000001/action")]
    [InlineData("/api/v1/combats/00000000-0000-0000-0000-000000000001/end-turn")]
    [InlineData("/api/v1/combats/00000000-0000-0000-0000-000000000001/process-ai-turns")]
    [InlineData("/api/v1/combats/00000000-0000-0000-0000-000000000001/end")]
    [InlineData("/api/v1/combats/00000000-0000-0000-0000-000000000001/auto-play")]
    public async Task DirectCombatMutationRoutes_AreNotExposed(string path)
    {
        var response = await Client.PostRawAsync(path);

        Assert.Contains(response.StatusCode, new[]
        {
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed
        });
    }

    [Fact]
    public async Task EvaluateFormula_MissingContentRevision_ReturnsBadRequest()
    {
        // Try to evaluate non-existent formula
        var parameters = new Dictionary<string, float>
        {
            { "value", 10f }
        };

        var response = await Client.PostRawAsync("/api/v1/simulations/formulas/evaluate", new
        {
            formulaName = "non_existent_formula_xyz",
            inputValue = 0f,
            paramOverrides = parameters
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
