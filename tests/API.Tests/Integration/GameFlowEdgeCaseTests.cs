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
    public async Task DrawCards_EmptyDeck_HandlesGracefully()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync();

        // Try to draw more cards than exist in deck
        var response = await Client.PostRawAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            expectedSequence = GetJsonInt(runState, "sequence"),
            expectedStep = runState.GetProperty("step").GetUInt64(),
            type = "DRAW_CARDS",
            payload = new { count = 1000 }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAction_InvalidTarget_ReturnsError()
    {
        var (runId, runState) = await SetupRunAsync();
        var playerEntityId = GetJsonString(runState, "playerEntityId");
        var combatId = await Client.StartCombatAsync(playerEntityId, new[] { "enemy_1" }, runId: runId);
        var combatState = await Client.GetCombatStateAsync(combatId);

        // The run-owned command boundary must reject a target outside the combat.
        var response = await Client.PostRawAsync($"/api/v1/combats/{combatId}/commands", new
        {
            commandId = Guid.NewGuid(),
            expectedSequence = GetJsonInt((await Client.GetRunStateAsync(runId)), "sequence"),
            expectedStep = combatState.GetProperty("step").GetUInt64(),
            type = "EXECUTE_ACTION",
            payload = new
            {
                actorId = playerEntityId,
                targetId = "non_existent_enemy",
                cardId = "basic_attack",
                actionId = "basic_attack"
            }
        });

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("RULE_VIOLATION", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BuyShopItem_InsufficientGold_ReturnsError()
    {
        var (runId, _) = await SetupRunAsync();

        // Buy a published offer and a paid preparation option, leaving less
        // gold than every item in the next basic-shop offer.
        var shopResponse = await Client.OpenShopAsync(runId, "basic_shop");
        var shopInstanceId = GetJsonGuid(shopResponse, "shopInstanceId");
        var items = shopResponse.GetProperty("items");
        var firstItem = items.EnumerateArray().First();
        await Client.BuyShopItemAsync(runId, shopInstanceId, GetJsonString(firstItem, "itemId"));

        while (GetJsonInt(await Client.GetRunStateAsync(runId), "gold") >= 10)
        {
            var preparation = await Client.StartPreparationAsync(runId, "basic_preparation");
            var paidOption = preparation.GetProperty("options").EnumerateArray()
                .Single(option => GetJsonInt(option, "goldCost") == 10);
            await Client.ApplyPreparationOptionAsync(
                runId,
                GetJsonGuid(preparation, "preparationInstanceId"),
                GetJsonString(paidOption, "optionId"));
        }

        var remainingGold = GetJsonInt(await Client.GetRunStateAsync(runId), "gold");
        Assert.True(remainingGold < 10);

        var secondShop = await Client.OpenShopAsync(runId, "basic_shop");
        var expensiveItem = secondShop.GetProperty("items").EnumerateArray()
            .First(item => GetJsonInt(item, "goldCost") > remainingGold);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => Client.BuyShopItemAsync(
                runId,
                GetJsonGuid(secondShop, "shopInstanceId"),
                GetJsonString(expensiveItem, "itemId")));
        Assert.Contains("422", exception.Message);
    }

    [Fact]
    public async Task StartCombat_RuntimeHeroId_IsSupportedByTheStandaloneCombatPrimitive()
    {
        // Standalone combat is a simulation primitive and accepts runtime IDs.
        var response = await Client.PostRawAsync("/api/v1/combats/start", new
        {
            heroId = "non_existent_hero_xyz",
            enemies = new[] { "enemy_1" },
            initialEnergy = 3
        });

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("non_existent_hero_xyz", created.GetProperty("hero").GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task EvaluateFormula_InvalidFormulaName_ReturnsError()
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

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
