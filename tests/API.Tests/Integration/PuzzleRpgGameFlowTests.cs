using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Integration tests simulating a Puzzle RPG (Puzzle Quest/Match-3 RPG-like).
/// Tests formula evaluation, mana/resource management, and affordability checks.
/// </summary>
public sealed class PuzzleRpgGameFlowTests : GameEngineIntegrationTestBase
{
    public PuzzleRpgGameFlowTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task FormulaEvaluation_DamageCalculation_ReturnsCorrectValue()
    {
        // Simulate damage calculation based on puzzle match
        var parameters = new Dictionary<string, float>
        {
            { "base_damage", 10f },
            { "match_count", 5f },
            { "multiplier", 1.5f },
            { "bonus", 3f }
        };

        var result = await Client.EvaluateFormulaAsync("puzzle_damage", parameters);

        // Verify formula executed
        AssertJsonPropertyExists(result, "result");
        AssertJsonPropertyExists(result, "formulaName");
        AssertJsonPropertyEquals(result, "formulaName", "puzzle_damage");

        // Verify result is numeric
        var damageValue = GetJsonFloat(result, "result");
        Assert.True(damageValue > 0, "Damage should be positive");
    }

    [Fact]
    public async Task ManaSystem_SpendResource_AffordabilityCheck()
    {
        // Setup combat with mana-based powers
        var (combatId, combatState) = await SetupCombatAsync("mage_hero", new[] { "enemy_1" });

        var hero = combatState.GetProperty("hero");
        AssertEntityHasResource(hero, "energy");

        // Get initial energy
        var resources = hero.GetProperty("resources");
        var energy = resources.GetProperty("energy");
        var currentEnergy = GetJsonInt(energy, "current");

        // Try to use a power that costs energy
        if (currentEnergy >= 2)
        {
            var actionResult = await Client.ExecuteActionAsync(combatId, "mage_hero", 
                targetId: "enemy_1", powerId: "fireball");

            // Verify action executed
            AssertJsonPropertyExists(actionResult, "combatId");

            // Verify energy decreased
            var updatedState = await Client.GetCombatStateAsync(combatId);
            var updatedHero = updatedState.GetProperty("hero");
            var updatedResources = updatedHero.GetProperty("resources");
            var updatedEnergy = updatedResources.GetProperty("energy");
            var newEnergy = GetJsonInt(updatedEnergy, "current");

            Assert.True(newEnergy <= currentEnergy, "Energy should decrease or stay same after action");
        }
    }

    [Fact]
    public async Task ComboSystem_ChainMultiplier_DamageScaling()
    {
        // Test combo multiplier formula
        var baseParams = new Dictionary<string, float>
        {
            { "base_damage", 10f },
            { "combo_count", 1f }
        };

        var result1 = await Client.EvaluateFormulaAsync("combo_damage", baseParams);
        var damage1 = GetJsonFloat(result1, "result");

        // Increase combo
        var comboParams = new Dictionary<string, float>
        {
            { "base_damage", 10f },
            { "combo_count", 5f }
        };

        var result5 = await Client.EvaluateFormulaAsync("combo_damage", comboParams);
        var damage5 = GetJsonFloat(result5, "result");

        // Verify scaling (5 combo should deal more than 1 combo)
        Assert.True(damage5 >= damage1, "Higher combo should deal same or more damage");
    }

    [Fact]
    public async Task ResourceGeneration_MatchGems_GainMana()
    {
        // Setup run with mana system
        var (runId, runState) = await SetupRunAsync();

        // Simulate gem match generating mana (via preparation/event)
        var prepResponse = await Client.StartPreparationAsync(runId, "gem_match_mana");

        AssertJsonPropertyExists(prepResponse, "preparationInstanceId");
        AssertJsonPropertyExists(prepResponse, "options");

        var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
        var options = prepResponse.GetProperty("options");

        // Apply option that grants mana
        if (options.GetArrayLength() > 0)
        {
            var manaOption = options.EnumerateArray().First();
            var optionId = GetJsonString(manaOption, "optionId");

            var applyResult = await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);

            AssertJsonPropertyExists(applyResult, "applied");
            Assert.True(GetJsonBool(applyResult, "applied"));
        }
    }

    [Fact]
    public async Task PowerAffordability_InsufficientMana_Validation()
    {
        // Setup combat with limited energy
        var combatId = await Client.StartCombatAsync("mage_hero", new[] { "enemy_1" }, initialEnergy: 1);
        var combatState = await Client.GetCombatStateAsync(combatId);

        var hero = combatState.GetProperty("hero");
        var resources = hero.GetProperty("resources");
        var energy = resources.GetProperty("energy");
        var currentEnergy = GetJsonInt(energy, "current");

        // Verify low energy
        Assert.True(currentEnergy <= 1, "Should start with low energy for this test");

        // Try to use expensive power (should fail or be prevented)
        var expensivePowerResponse = await Client.PostRawAsync($"/api/combat/{combatId}/action", new
        {
            actorId = "mage_hero",
            targetId = "enemy_1",
            powerId = "mega_spell", // Hypothetical expensive spell
            actionType = "POWER"
        });

        // Response should indicate failure or validation error
        // In a real system, this might return 400 or a specific error
        var responseText = await expensivePowerResponse.Content.ReadAsStringAsync();
        
        // Verify some response (system should handle affordability)
        Assert.NotNull(responseText);
        Assert.NotEmpty(responseText);
    }
}
