using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Integration tests simulating an Auto-Battler game (Underlords/TFT-like).
/// Tests AI-driven combat plus run-owned shop and preparation operations.
/// </summary>
public sealed class AutoBattlerGameFlowTests : GameEngineIntegrationTestBase
{
    public AutoBattlerGameFlowTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ShopRound_BuyUnits_TeamComposition()
    {
        // Simulate auto-battler shop round
        var (runId, runState) = await SetupRunAsync();
        var initialGold = GetJsonInt(runState, "gold");

        // Open shop (unit shop)
        var shopResponse = await Client.OpenShopAsync(runId, "basic_shop");

        AssertJsonPropertyExists(shopResponse, "shopInstanceId");
        AssertJsonPropertyExists(shopResponse, "items");

        var shopInstanceId = GetJsonGuid(shopResponse, "shopInstanceId");
        var items = shopResponse.GetProperty("items");

        Assert.NotEmpty(items.EnumerateArray());
        var firstUnit = items.EnumerateArray().First();
        var unitId = GetJsonString(firstUnit, "itemId");
        var price = GetJsonInt(firstUnit, "goldCost");
        Assert.True(initialGold >= price);

        var buyResult = await Client.BuyShopItemAsync(runId, shopInstanceId, unitId);
        Assert.True(GetJsonBool(buyResult, "purchased"));

        // Verify gold decreased
        var updatedRunState = await Client.GetRunStateAsync(runId);
        var newGold = GetJsonInt(updatedRunState, "gold");
        Assert.Equal(initialGold - price, newGold);
    }

    [Fact]
    public async Task RerollShop_FindDesiredUnit_GoldCost()
    {
        // Setup shop
        var (runId, runState) = await SetupRunAsync();
        var initialGold = GetJsonInt(runState, "gold");

        var shopResponse = await Client.OpenShopAsync(runId, "basic_shop");
        var shopInstanceId = GetJsonGuid(shopResponse, "shopInstanceId");

        var rerollCost = shopResponse.GetProperty("rerollCostGold").GetInt32();
        Assert.True(initialGold >= rerollCost);

        var rerollResult = await Client.RerollShopAsync(runId, shopInstanceId);

        AssertJsonPropertyExists(rerollResult, "items");
        
        // Verify new items offered
        var newItems = rerollResult.GetProperty("items");
        Assert.NotEmpty(newItems.EnumerateArray());

        // Verify the exact persisted economy transition.
        var updatedRunState = await Client.GetRunStateAsync(runId);
        var newGold = GetJsonInt(updatedRunState, "gold");
        Assert.Equal(initialGold - rerollCost, newGold);
    }

    [Fact]
    public async Task PreparationRound_PreservesValidEconomyState()
    {
        // Simulate multiple combat rounds with income
        var (runId, runState) = await SetupRunAsync();
        var startGold = GetJsonInt(runState, "gold");

        // Income/preparation phase
        var prepResponse = await Client.StartPreparationAsync(runId, "basic_preparation");
        
        var options = prepResponse.GetProperty("options");
        Assert.NotEmpty(options.EnumerateArray());
        var incomeOption = options.EnumerateArray().First();
        var optionId = GetJsonString(incomeOption, "optionId");
        
        var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
        var applied = await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);
        Assert.True(GetJsonBool(applied, "applied"));
        
        // Verify the persisted run remains economically valid.
        var updatedRunState = await Client.GetRunStateAsync(runId);
        Assert.True(GetJsonInt(updatedRunState, "gold") >= 0);
    }

    [Fact]
    public async Task LevelUp_IncreaseTeamSize_PreparationOption()
    {
        // Setup
        var (runId, runState) = await SetupRunAsync();

        // Start preparation for level up
        var prepResponse = await Client.StartPreparationAsync(runId, "basic_preparation");
        
        AssertJsonPropertyExists(prepResponse, "preparationInstanceId");
        AssertJsonPropertyExists(prepResponse, "options");

        var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
        var options = prepResponse.GetProperty("options");

        Assert.NotEmpty(options.EnumerateArray());
        var levelUpOption = options.EnumerateArray().First();
        var optionId = GetJsonString(levelUpOption, "optionId");

        var applyResult = await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);
        
        AssertJsonPropertyExists(applyResult, "applied");
        Assert.True(GetJsonBool(applyResult, "applied"));
    }
}
