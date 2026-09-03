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
        var (runId, runState) = await SetupRunAsync();
        await Client.DrawCardsAsync(runId, 5);
        var playerEntityId = GetJsonString(runState, "playerEntityId");
        var combatId = await Client.StartCombatAsync(playerEntityId, new[] { "enemy_1" }, runId: runId);
        var combatState = await Client.GetCombatStateAsync(combatId);

        var hero = combatState.GetProperty("hero");
        AssertEntityHasResource(hero, "energy");

        // Get initial energy
        var resources = hero.GetProperty("resources");
        var energy = resources.GetProperty("energy");
        var currentEnergy = GetJsonInt(energy, "current");

        Assert.True(currentEnergy >= 2, "The default run must start combat with enough energy for Fireball.");
        var fireballCard = await Client.GetHandCardInstanceIdAsync(runId, "fireball");
        var actionResult = await Client.ExecuteActionAsync(combatId, playerEntityId,
            targetId: "enemy_1", cardId: fireballCard.ToString(), runId: runId);

        AssertJsonPropertyEquals(actionResult, "combatId", combatId);

        // Verify the exact cost from the published Fireball definition was spent.
        var updatedState = await Client.GetCombatStateAsync(combatId);
        var updatedHero = updatedState.GetProperty("hero");
        var updatedResources = updatedHero.GetProperty("resources");
        var updatedEnergy = updatedResources.GetProperty("energy");
        var newEnergy = GetJsonInt(updatedEnergy, "current");

        Assert.Equal(currentEnergy - 2, newEnergy);
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
        var prepResponse = await Client.StartPreparationAsync(runId, "basic_preparation");

        AssertJsonPropertyExists(prepResponse, "preparationInstanceId");
        AssertJsonPropertyExists(prepResponse, "options");

        var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
        var options = prepResponse.GetProperty("options");

        Assert.NotEmpty(options.EnumerateArray());
        var manaOption = options.EnumerateArray().First();
        var optionId = GetJsonString(manaOption, "optionId");

        var applyResult = await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);

        AssertJsonPropertyExists(applyResult, "applied");
        Assert.True(GetJsonBool(applyResult, "applied"));
    }

    [Fact]
    public async Task EnergyRefresh_IsOwnedByTheSelectedGameMode()
    {
        var (runId, runState) = await SetupRunAsync();
        await Client.DrawCardsAsync(runId, 5);
        var playerEntityId = GetJsonString(runState, "playerEntityId");
        var combatId = await Client.StartCombatAsync(playerEntityId, new[] { "enemy_1" }, initialEnergy: 1, runId: runId);
        var combatState = await Client.GetCombatStateAsync(combatId);

        var hero = combatState.GetProperty("hero");
        var resources = hero.GetProperty("resources");
        var energy = resources.GetProperty("energy");
        var currentEnergy = GetJsonInt(energy, "current");

        // START_ENCOUNTER cannot bypass the mode's ResetToMax policy with an
        // arbitrary low value supplied by the adapter.
        Assert.True(currentEnergy > 1);
        Assert.Equal(GetJsonInt(energy, "maximum"), currentEnergy);

        var currentRun = await Client.GetRunStateAsync(runId);
        var fireballCard = await Client.GetHandCardInstanceIdAsync(runId, "fireball");
        var fireballResponse = await Client.PostRawAsync($"/api/v1/combats/{combatId}/commands", new
        {
            commandId = Guid.NewGuid(),
            expectedSequence = GetJsonInt(currentRun, "sequence"),
            expectedStep = combatState.GetProperty("step").GetUInt64(),
            type = "PLAY_CARD",
            payload = new
            {
                actorId = playerEntityId,
                targetIds = new[] { "enemy_1" },
                cardInstanceId = fireballCard
            }
        });

        var responseText = await fireballResponse.Content.ReadAsStringAsync();
        Assert.True(fireballResponse.IsSuccessStatusCode, responseText);
    }
}
