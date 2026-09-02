using System;
using System.Net.Http;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Base class for integration tests that simulate a game engine client (Unity/Godot).
/// Provides common setup, helpers, and assertions for testing full game workflows.
/// </summary>
[Trait("Category", "Integration")]
public abstract class GameEngineIntegrationTestBase : IClassFixture<TestWebApplicationFactory>
{
    protected readonly HttpClient RawClient;
    protected readonly GameEngineClientSimulator Client;

    protected GameEngineIntegrationTestBase(TestWebApplicationFactory factory)
    {
        RawClient = factory.CreateClient();
        Client = new GameEngineClientSimulator(RawClient);
    }

    // ==================== ASSERTION HELPERS ====================

    protected static void AssertJsonProperty(JsonElement element, string propertyName, Action<JsonElement> assertion)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), 
            $"Expected property '{propertyName}' not found in JSON");
        assertion(property);
    }

    protected static void AssertJsonPropertyExists(JsonElement element, string propertyName)
    {
        Assert.True(element.TryGetProperty(propertyName, out _), 
            $"Expected property '{propertyName}' not found in JSON");
    }

    protected static void AssertJsonPropertyEquals<T>(JsonElement element, string propertyName, T expectedValue)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), 
            $"Expected property '{propertyName}' not found in JSON");
        
        if (expectedValue is int intValue)
            Assert.Equal(intValue, property.GetInt32());
        else if (expectedValue is string stringValue)
            Assert.Equal(stringValue, property.GetString());
        else if (expectedValue is bool boolValue)
            Assert.Equal(boolValue, property.GetBoolean());
        else if (expectedValue is Guid guidValue)
            Assert.Equal(guidValue, property.GetGuid());
        else
            throw new NotSupportedException($"Type {typeof(T)} not supported for assertion");
    }

    protected static void AssertCombatStateValid(JsonElement combatState)
    {
        AssertJsonPropertyExists(combatState, "combatId");
        AssertJsonPropertyExists(combatState, "hero");
        AssertJsonPropertyExists(combatState, "enemies");
        AssertJsonPropertyExists(combatState, "currentTurn");
    }

    protected static void AssertRunStateValid(JsonElement runState)
    {
        AssertJsonPropertyExists(runState, "runId");
        AssertJsonPropertyExists(runState, "configName");
        AssertJsonPropertyExists(runState, "playerEntityId");
        AssertJsonPropertyExists(runState, "gold");
        AssertJsonPropertyExists(runState, "deck");
    }

    protected static void AssertDeckStateValid(JsonElement deckState)
    {
        AssertJsonPropertyExists(deckState, "drawPile");
        AssertJsonPropertyExists(deckState, "hand");
        AssertJsonPropertyExists(deckState, "discardPile");
    }

    protected static void AssertEntityHasResource(JsonElement entity, string resourceId)
    {
        Assert.True(entity.TryGetProperty("resources", out var resources), 
            "Entity does not have 'resources' property");
        Assert.True(resources.TryGetProperty(resourceId, out _), 
            $"Entity does not have resource '{resourceId}'");
    }

    protected static int GetJsonInt(JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetInt32();
    }

    protected static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetString()!;
    }

    protected static Guid GetJsonGuid(JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetGuid();
    }

    protected static bool GetJsonBool(JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetBoolean();
    }

    protected static float GetJsonFloat(JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetSingle();
    }

    protected static int GetArrayLength(JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetArrayLength();
    }

    // ==================== WORKFLOW HELPERS ====================

    protected async Task<(Guid runId, JsonElement runState)> SetupRunAsync()
    {
        var runId = await Client.StartRunAsync("default", "default_run", "player");
        var runState = await Client.GetRunStateAsync(runId);
        return (runId, runState);
    }

    protected async Task<(Guid combatId, JsonElement combatState)> SetupCombatAsync(
        string heroId = "hero", 
        string[] enemies = null!, 
        int initialEnergy = 3)
    {
        enemies ??= new[] { "enemy_1" };
        var combatId = await Client.StartCombatAsync(heroId, enemies, initialEnergy);
        var combatState = await Client.GetCombatStateAsync(combatId);
        return (combatId, combatState);
    }

    protected async Task<(Guid runId, Guid combatId, JsonElement runState, JsonElement combatState)> SetupRunWithCombatAsync()
    {
        var (runId, runState) = await SetupRunAsync();
        var playerEntityId = GetJsonString(runState, "playerEntityId");
        var combatId = await Client.StartCombatAsync(
            playerEntityId,
            new[] { "enemy_1", "enemy_2" },
            runId: runId);
        var combatState = await Client.GetCombatStateAsync(combatId);
        return (runId, combatId, runState, combatState);
    }
}
