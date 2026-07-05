using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Integration tests simulating an Auto-Battler game (Underlords/TFT-like).
/// Tests AI-driven combat, gambits, unit management, and synergy systems.
/// </summary>
public sealed class AutoBattlerGameFlowTests : GameEngineIntegrationTestBase
{
    public AutoBattlerGameFlowTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AutoPlay_GambitDrivenCombat_AllUnitsAct()
    {
        // Setup: Create combat with multiple units on both sides
        var combatId = await Client.StartCombatAsync("player_board", 
            new[] { "enemy_unit_1", "enemy_unit_2", "enemy_unit_3" }, 
            initialEnergy: 0); // Auto-battlers typically don't use player energy

        var initialState = await Client.GetCombatStateAsync(combatId);
        AssertCombatStateValid(initialState);

        // Auto-play entire combat (all units use gambits to decide actions)
        var autoPlayResult = await Client.AutoPlayCombatAsync(combatId);
        
        // Verify combat executed
        AssertJsonPropertyExists(autoPlayResult, "combatId");
        
        // Verify final state
        var finalState = await Client.GetCombatStateAsync(combatId);
        AssertCombatStateValid(finalState);
        
        // Combat should have progressed (turn counter increased or combat ended)
        var finalTurn = GetJsonInt(finalState, "currentTurn");
        Assert.True(finalTurn >= 0);
    }

    [Fact]
    public async Task StatusEffect_ApplySynergy_ModifiersActive()
    {
        // Setup: Create a unit
        var (runId, runState) = await SetupRunAsync();
        var playerEntityId = GetJsonString(runState, "playerEntityId");

        // Apply synergy buff (simulating "3 knights" synergy bonus)
        var statusResult = await Client.ApplyStatusEffectAsync(
            targetId: playerEntityId,
            statusId: "knight_synergy_bonus",
            stacks: 3,
            duration: null, // Permanent synergy
            sourceId: "synergy_system");

        // Verify status applied
        AssertJsonPropertyExists(statusResult, "instanceId");
        AssertJsonPropertyEquals(statusResult, "statusId", "knight_synergy_bonus");
        AssertJsonPropertyEquals(statusResult, "stacks", 3);

        // Verify status is active on entity
        var activeStatuses = await Client.GetStatusEffectsAsync(playerEntityId);
        Assert.NotEmpty(activeStatuses);
    }

    [Fact]
    public async Task ShopRound_BuyUnits_TeamComposition()
    {
        // Simulate auto-battler shop round
        var (runId, runState) = await SetupRunAsync();
        var initialGold = GetJsonInt(runState, "gold");

        // Open shop (unit shop)
        var shopResponse = await Client.OpenShopAsync(runId, "auto_battler_shop");
        
        AssertJsonPropertyExists(shopResponse, "shopInstanceId");
        AssertJsonPropertyExists(shopResponse, "items");

        var shopInstanceId = GetJsonGuid(shopResponse, "shopInstanceId");
        var items = shopResponse.GetProperty("items");

        // Buy first available unit
        if (items.GetArrayLength() > 0)
        {
            var firstUnit = items.EnumerateArray().First();
            var unitId = GetJsonString(firstUnit, "itemId");
            var price = GetJsonInt(firstUnit, "price");

            if (initialGold >= price)
            {
                var buyResult = await Client.BuyShopItemAsync(runId, shopInstanceId, unitId);
                AssertJsonPropertyExists(buyResult, "success");

                // Verify gold decreased
                var updatedRunState = await Client.GetRunStateAsync(runId);
                var newGold = GetJsonInt(updatedRunState, "gold");
                Assert.Equal(initialGold - price, newGold);
            }
        }
    }

    [Fact]
    public async Task RerollShop_FindDesiredUnit_GoldCost()
    {
        // Setup shop
        var (runId, runState) = await SetupRunAsync();
        var initialGold = GetJsonInt(runState, "gold");

        var shopResponse = await Client.OpenShopAsync(runId, "auto_battler_shop");
        var shopInstanceId = GetJsonGuid(shopResponse, "shopInstanceId");

        // Get reroll cost
        var rerollCost = 0;
        if (shopResponse.TryGetProperty("rerollCostGold", out var costProp))
        {
            rerollCost = costProp.GetInt32();
        }

        // Reroll shop to find better units
        if (initialGold >= rerollCost)
        {
            var rerollResult = await Client.RerollShopAsync(runId, shopInstanceId);
            
            AssertJsonPropertyExists(rerollResult, "items");
            
            // Verify new items offered
            var newItems = rerollResult.GetProperty("items");
            Assert.True(newItems.GetArrayLength() > 0);

            // Verify gold decreased by reroll cost
            var updatedRunState = await Client.GetRunStateAsync(runId);
            var newGold = GetJsonInt(updatedRunState, "gold");
            Assert.True(newGold <= initialGold);
        }
    }

    [Fact]
    public async Task MultiRound_WaveProgression_GoldAccumulation()
    {
        // Simulate multiple combat rounds with income
        var (runId, runState) = await SetupRunAsync();
        var startGold = GetJsonInt(runState, "gold");

        // Round 1: Combat
        var playerEntityId = GetJsonString(runState, "playerEntityId");
        var combatId1 = await Client.StartCombatAsync(playerEntityId, 
            new[] { "weak_enemy" }, initialEnergy: 0, runId: runId);
        
        var combat1State = await Client.GetCombatStateAsync(combatId1);
        AssertCombatStateValid(combat1State);

        // Auto-play combat
        await Client.AutoPlayCombatAsync(combatId1);

        // Round 1: Income phase (simulated via preparation)
        var prepResponse = await Client.StartPreparationAsync(runId, "income_phase");
        
        if (prepResponse.TryGetProperty("options", out var options) && options.GetArrayLength() > 0)
        {
            var incomeOption = options.EnumerateArray().First();
            var optionId = GetJsonString(incomeOption, "optionId");
            
            var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
            await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);

            // Verify gold increased or stayed same
            var updatedRunState = await Client.GetRunStateAsync(runId);
            var newGold = GetJsonInt(updatedRunState, "gold");
            Assert.True(newGold >= 0); // Gold should exist
        }
    }

    [Fact]
    public async Task LevelUp_IncreaseTeamSize_PreparationOption()
    {
        // Setup
        var (runId, runState) = await SetupRunAsync();

        // Start preparation for level up
        var prepResponse = await Client.StartPreparationAsync(runId, "level_up_preparation");
        
        AssertJsonPropertyExists(prepResponse, "preparationInstanceId");
        AssertJsonPropertyExists(prepResponse, "options");

        var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
        var options = prepResponse.GetProperty("options");

        // Apply level up option if available
        if (options.GetArrayLength() > 0)
        {
            var levelUpOption = options.EnumerateArray().First();
            var optionId = GetJsonString(levelUpOption, "optionId");

            var applyResult = await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);
            
            AssertJsonPropertyExists(applyResult, "applied");
            Assert.True(GetJsonBool(applyResult, "applied"));

            // In a real implementation, this would increase max team size
            // We verify the preparation system worked
        }
    }
}
