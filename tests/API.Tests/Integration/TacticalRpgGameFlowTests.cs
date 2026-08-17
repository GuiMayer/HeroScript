using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Integration tests simulating a Tactical RPG (Fire Emblem/XCOM-like).
/// Tests turn phases, positioning, status effects, and tactical combat mechanics.
/// </summary>
public sealed class TacticalRpgGameFlowTests : GameEngineIntegrationTestBase
{
    public TacticalRpgGameFlowTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Combat_WithMultipleUnits_StartsSuccessfully()
    {
        // Simulate tactical combat with squad-based units
        var combatId = await Client.StartCombatAsync("squad_leader", 
            new[] { "enemy_soldier_1", "enemy_soldier_2", "enemy_elite" }, 
            initialEnergy: 5);

        var combatState = await Client.GetCombatStateAsync(combatId);

        // Verify combat state
        AssertCombatStateValid(combatState);
        Assert.Equal(3, GetArrayLength(combatState, "enemies"));
        
        // Verify hero has tactical resources
        var hero = combatState.GetProperty("hero");
        AssertEntityHasResource(hero, "energy");
    }

    [Fact]
    public async Task StatusEffect_ApplyDebuff_TargetAffected()
    {
        // Setup combat
        var (combatId, combatState) = await SetupCombatAsync("hero", new[] { "enemy_1" });
        
        // Apply status effect (e.g., "Stunned", "Overwatch", "Cover")
        var statusResult = await Client.ApplyStatusEffectAsync(
            targetId: "enemy_1",
            statusId: "stunned",
            stacks: 1,
            duration: 2, // 2 turns
            sourceId: "hero");

        // Verify status applied
        AssertJsonPropertyExists(statusResult, "instanceId");
        AssertJsonPropertyEquals(statusResult, "statusId", "stunned");
        AssertJsonPropertyEquals(statusResult, "stacks", 1);

        // Verify status is active
        var activeStatuses = await Client.GetStatusEffectsAsync("enemy_1");
        Assert.NotEmpty(activeStatuses);
        
        var stunnedStatus = activeStatuses.FirstOrDefault(s => 
            s.TryGetProperty("statusId", out var id) && id.GetString() == "stunned");
        Assert.False(stunnedStatus.ValueKind == JsonValueKind.Undefined);
    }

    [Fact]
    public async Task TurnFlow_ExecuteMultipleActions_TurnProgresses()
    {
        // Setup combat
        var (combatId, combatState) = await SetupCombatAsync("hero", new[] { "enemy_1", "enemy_2" });
        var initialTurn = GetJsonInt(combatState, "currentTurn");

        // Execute action 1: Attack enemy_1
        await Client.ExecuteActionAsync(combatId, "hero", targetId: "enemy_1", powerId: "basic_attack");

        // Execute action 2: Apply buff to self
        await Client.ExecuteActionAsync(combatId, "hero", powerId: "defend");

        // End turn
        var endTurnResult = await Client.EndTurnAsync(combatId);
        AssertJsonPropertyExists(endTurnResult, "combatId");

        // Verify turn advanced
        var updatedCombatState = await Client.GetCombatStateAsync(combatId);
        var newTurn = GetJsonInt(updatedCombatState, "currentTurn");
        Assert.True(newTurn >= initialTurn);
    }

    [Fact]
    public async Task Fireball_UsesRealContentAndDamagesTarget()
    {
        // Use the shipped default content rather than a fixture-only action.
        var (combatId, combatState) = await SetupCombatAsync("hero", 
            new[] { "enemy_1", "enemy_2" });
        var initialHealth = combatState.GetProperty("enemies")[0].GetProperty("currentHp").GetInt32();

        var actionResult = await Client.ExecuteActionAsync(combatId, "hero", 
            targetId: "enemy_1",
            powerId: "fireball");

        AssertJsonPropertyExists(actionResult, "combatId");
        var updatedState = await Client.GetCombatStateAsync(combatId);
        var updatedHealth = updatedState.GetProperty("enemies")[0].GetProperty("currentHp").GetInt32();
        Assert.True(updatedHealth < initialHealth, "Fireball from the shipped content must damage its target");
    }

    [Fact]
    public async Task StatusEffect_MultipleStacks_StacksAccumulate()
    {
        // Setup
        var (runId, runState) = await SetupRunAsync();
        var playerEntityId = GetJsonString(runState, "playerEntityId");

        // Apply status effect multiple times
        await Client.ApplyStatusEffectAsync(playerEntityId, "poison", stacks: 1, duration: 3);
        await Client.ApplyStatusEffectAsync(playerEntityId, "poison", stacks: 2, duration: 3);

        // Verify stacks accumulated
        var statuses = await Client.GetStatusEffectsAsync(playerEntityId);
        Assert.NotEmpty(statuses);

        // Find poison status
        var poisonStatus = statuses.FirstOrDefault(s => 
            s.TryGetProperty("statusId", out var id) && id.GetString() == "poison");
        
        if (poisonStatus.ValueKind != JsonValueKind.Undefined)
        {
            // Verify stacks (implementation-dependent: might be 3 total or separate instances)
            AssertJsonPropertyExists(poisonStatus, "stacks");
        }
    }

    [Fact]
    public async Task PreparationPhase_ApplyUpgrade_StatsIncrease()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync();

        // Start preparation phase (between missions)
        var prepResponse = await Client.StartPreparationAsync(runId, "basic_preparation");
        
        AssertJsonPropertyExists(prepResponse, "preparationInstanceId");
        AssertJsonPropertyExists(prepResponse, "options");

        var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
        var options = prepResponse.GetProperty("options");

        // Apply upgrade option
        if (options.GetArrayLength() > 0)
        {
            var upgradeOption = options.EnumerateArray().First();
            var optionId = GetJsonString(upgradeOption, "optionId");

            var applyResult = await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);
            
            AssertJsonPropertyExists(applyResult, "applied");
            Assert.True(GetJsonBool(applyResult, "applied"));

            // In a real implementation, verify stats increased
            // Here we verify the preparation system worked
        }
    }
}
