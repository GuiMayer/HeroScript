using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        var response = await Client.GetRawResponseAsync($"/api/run/{nonExistentRunId}/state");

        // Should return 404 or similar error
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCombatState_NonExistentCombat_Returns404()
    {
        // Try to get state for non-existent combat
        var nonExistentCombatId = Guid.NewGuid();
        var response = await Client.GetRawResponseAsync($"/api/combat/{nonExistentCombatId}/state");

        // Should return 404 or similar error
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DrawCards_EmptyDeck_HandlesGracefully()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync();

        // Try to draw more cards than exist in deck
        var response = await Client.GetRawResponseAsync($"/api/run/{runId}/draw?count=1000");

        // Should handle gracefully (return available cards or error)
        // Not crash or return 500
        Assert.True(response.IsSuccessStatusCode || 
                   response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExecuteAction_InvalidTarget_ReturnsError()
    {
        // Setup combat
        var (combatId, combatState) = await SetupCombatAsync("hero", new[] { "enemy_1" });

        // Try to attack non-existent target
        var response = await Client.PostRawAsync($"/api/combat/{combatId}/action", new
        {
            actorId = "hero",
            targetId = "non_existent_enemy",
            powerId = "basic_attack",
            actionType = "POWER"
        });

        // Should return validation error
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BuyShopItem_InsufficientGold_ReturnsError()
    {
        // Setup run with 0 gold (if possible) or track initial gold
        var (runId, runState) = await SetupRunAsync();
        var initialGold = GetJsonInt(runState, "gold");

        // Open shop
        var shopResponse = await Client.OpenShopAsync(runId, "basic_shop");
        var shopInstanceId = GetJsonGuid(shopResponse, "shopInstanceId");
        var items = shopResponse.GetProperty("items");

        // Find item more expensive than current gold
        var expensiveItem = items.EnumerateArray()
            .FirstOrDefault(item => GetJsonInt(item, "price") > initialGold);

        if (expensiveItem.ValueKind != JsonValueKind.Undefined)
        {
            var itemId = GetJsonString(expensiveItem, "itemId");
            
            var response = await Client.GetRawResponseAsync($"/api/run/{runId}/shop/{shopInstanceId}/buy/{itemId}");

            // Should return error for insufficient funds
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                       !response.IsSuccessStatusCode);
        }
    }

    [Fact]
    public async Task StartCombat_InvalidHeroId_ReturnsError()
    {
        // Try to start combat with non-existent hero
        var response = await Client.PostRawAsync("/api/combat/start", new
        {
            heroId = "non_existent_hero_xyz",
            enemies = new[] { "enemy_1" },
            initialEnergy = 3
        });

        // Should return validation error
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApplyStatusEffect_InvalidStatusId_HandlesGracefully()
    {
        // Setup entity
        var (runId, runState) = await SetupRunAsync();
        var playerEntityId = GetJsonString(runState, "playerEntityId");

        // Try to apply non-existent status effect
        var response = await Client.PostRawAsync("/api/status/apply", new
        {
            targetId = playerEntityId,
            statusId = "totally_fake_status_xyz",
            stacks = 1
        });

        // Should handle gracefully (error or ignore)
        // Should not crash with 500
        Assert.True(response.IsSuccessStatusCode || 
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EvaluateFormula_InvalidFormulaName_ReturnsError()
    {
        // Try to evaluate non-existent formula
        var parameters = new Dictionary<string, float>
        {
            { "value", 10f }
        };

        var response = await Client.PostRawAsync("/api/formula/evaluate", new
        {
            formulaName = "non_existent_formula_xyz",
            parameters
        });

        // Should return error
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                   response.StatusCode == HttpStatusCode.NotFound);
    }
}
