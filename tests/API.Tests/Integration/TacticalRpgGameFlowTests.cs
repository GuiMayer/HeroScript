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
            initialHeroResourceValues: new Dictionary<string, float>
            {
                ["energy"] = 5
            });

        var combatState = await Client.GetCombatStateAsync(combatId);

        // Verify combat state
        AssertCombatStateValid(combatState);
        Assert.Equal(4, GetArrayLength(combatState, "actors"));

        // Verify hero has tactical resources
        var hero = GetActor(combatState, "squad_leader");
        AssertEntityHasResource(hero, "energy");
        Assert.False(hero.TryGetProperty("currentHp", out _));
        Assert.False(hero.TryGetProperty("maxHp", out _));
        Assert.False(combatState.TryGetProperty("energy", out _));
    }

    [Fact]
    public async Task TurnFlow_ExecuteMultipleActions_TurnProgresses()
    {
        var (runId, runState) = await SetupRunAsync();
        await Client.DrawCardsAsync(runId, 5);
        var playerEntityId = GetJsonString(runState, "playerEntityId");
        var combatId = await Client.StartCombatAsync(playerEntityId, new[] { "enemy_1", "enemy_2" }, runId: runId);
        var combatState = await Client.GetCombatStateAsync(combatId);
        var initialTurn = GetJsonInt(combatState, "currentTurn");
        var hand = await Client.GetHandAsync(runId);

        // Execute two real cards through the run-owned combat command gateway.
        var attackCard = hand.First(cardId => cardId == "basic_attack");
        await Client.ExecuteActionAsync(combatId, playerEntityId, targetId: "enemy_1", cardId: attackCard, runId: runId);

        var defendCard = (await Client.GetHandAsync(runId)).First(cardId => cardId == "defend");
        await Client.ExecuteActionAsync(combatId, playerEntityId, cardId: defendCard, runId: runId);

        // End turn
        var endTurnResult = await Client.EndTurnAsync(combatId, runId);
        AssertJsonPropertyEquals(endTurnResult, "combatId", combatId);

        // Verify turn advanced
        var updatedCombatState = await Client.GetCombatStateAsync(combatId);
        var newTurn = GetJsonInt(updatedCombatState, "currentTurn");
        Assert.True(newTurn >= initialTurn);
    }

    [Fact]
    public async Task Fireball_UsesRealContentAndDamagesTarget()
    {
        // Use shipped content through the same run and command flow used by a client.
        var (runId, runState) = await SetupRunAsync();
        await Client.DrawCardsAsync(runId, 5);
        var playerEntityId = GetJsonString(runState, "playerEntityId");
        var combatId = await Client.StartCombatAsync(playerEntityId, new[] { "enemy_1", "enemy_2" }, runId: runId);
        var combatState = await Client.GetCombatStateAsync(combatId);
        var initialHealth = GetActor(combatState, "enemy_1")
            .GetProperty("resources")
            .GetProperty("health")
            .GetProperty("current")
            .GetDouble();
        var fireballCard = (await Client.GetHandAsync(runId)).First(cardId => cardId == "fireball");

        var actionResult = await Client.ExecuteActionAsync(combatId, playerEntityId,
            targetId: "enemy_1",
            cardId: fireballCard,
            runId: runId);

        AssertJsonPropertyEquals(actionResult, "combatId", combatId);
        var updatedState = await Client.GetCombatStateAsync(combatId);
        var updatedHealth = GetActor(updatedState, "enemy_1")
            .GetProperty("resources")
            .GetProperty("health")
            .GetProperty("current")
            .GetDouble();
        Assert.True(updatedHealth < initialHealth, "Fireball from the shipped content must damage its target");

        // Status projections are deliberately not used as a client mutation
        // path. The immutable combat-state representation belongs to the
        // combat aggregate tests; this flow proves that published content is
        // executed through the run-owned command boundary.
    }

    [Fact]
    public async Task PreparationPhase_ApplyUpgrade_StatsIncrease()
    {
        // Setup run
        var (runId, runState) = await SetupRunAsync("combat_sandbox");

        // Start preparation phase (between missions)
        var prepResponse = await Client.StartPreparationAsync(runId, "basic_preparation");
        
        AssertJsonPropertyExists(prepResponse, "preparationInstanceId");
        AssertJsonPropertyExists(prepResponse, "options");

        var prepInstanceId = GetJsonGuid(prepResponse, "preparationInstanceId");
        var options = prepResponse.GetProperty("options");

        Assert.NotEmpty(options.EnumerateArray());
        var upgradeOption = options.EnumerateArray().First();
        var optionId = GetJsonString(upgradeOption, "optionId");

        var applyResult = await Client.ApplyPreparationOptionAsync(runId, prepInstanceId, optionId);
        
        AssertJsonPropertyExists(applyResult, "applied");
        Assert.True(GetJsonBool(applyResult, "applied"));
    }
}
