using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Run;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Vertical slices for the smallest playable headless prototype. These tests
/// deliberately use the same persisted command paths a Godot client will use.
/// </summary>
public sealed class DeterministicPrototypeFlowTests : GameEngineIntegrationTestBase
{
    public DeterministicPrototypeFlowTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task FixedSeed_RunCombatAndJournal_RemainVerifiableAfterReconnect()
    {
        const ulong seed = 20260816;
        const string playerEntityId = "prototype-player";

        var runId = await Client.StartRunAsync(
            configName: "default",
            runDefinitionId: "default_run",
            playerEntityId: playerEntityId,
            seed: seed);
        var started = await Client.GetRunStateAsync(runId);

        Assert.Equal(seed, started.GetProperty("seed").GetUInt64());
        Assert.Equal("default", GetJsonString(started, "configName"));
        Assert.Equal(playerEntityId, GetJsonString(started, "playerEntityId"));
        Assert.Equal(64, started.GetProperty("contentRevision").GetString()!.Length);
        Assert.Equal("spire_zones", started.GetProperty("resolvedMode")
            .GetProperty("cardZoneSystem")
            .GetProperty("cardZoneSystemId")
            .GetString());

        // The initial playable zone contains the first five cards. This draw consumes
        // the other five published cards, including Fireball, without using
        // an out-of-band setup hook.
        var drawn = await Client.DrawCardsAsync(runId, 5);
        Assert.Contains("fireball", drawn);

        var combatId = await Client.StartCombatAsync(
            playerEntityId,
            new[] { "prototype-enemy" },
            runId: runId);
        var beforeAction = await Client.GetCombatStateAsync(combatId);
        var initialEnemyHealth = GetActor(beforeAction, "prototype-enemy")
            .GetProperty("resources")
            .GetProperty("health")
            .GetProperty("current")
            .GetDouble();
        var fireball = (await Client.GetHandAsync(runId)).Single(cardId => cardId == "fireball");

        var afterAction = await Client.ExecuteActionAsync(
            combatId,
            playerEntityId,
            targetId: "prototype-enemy",
            cardId: fireball,
            runId: runId);

        AssertJsonPropertyEquals(afterAction, "combatId", combatId);
        var reconnectedCombat = await Client.GetCombatStateAsync(combatId);
        Assert.True(
            GetActor(reconnectedCombat, "prototype-enemy")
                .GetProperty("resources")
                .GetProperty("health")
                .GetProperty("current")
                .GetDouble() < initialEnemyHealth,
            "The persisted Fireball command must survive a client reconnect.");

        using var historyResponse = await RawClient.GetAsync($"/api/v1/combats/{combatId}/history");
        var history = await historyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var application = history.GetProperty("actions")[0]
            .GetProperty("applications")
            .EnumerateArray()
            .Single(item => item.GetProperty("resourceId").GetString() == "health");
        Assert.Equal("Current", application.GetProperty("resourceField").GetString());
        Assert.True(application.GetProperty("signedAmount").GetDouble() < 0);
        Assert.False(history.GetProperty("actions")[0].TryGetProperty("damageDealt", out _));
        Assert.False(history.GetProperty("actions")[0].TryGetProperty("energyChange", out _));

        using var journalResponse = await RawClient.GetAsync($"/api/v1/runs/{runId}/journal?limit=10");
        var journal = await journalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, journalResponse.StatusCode);
        var commandTypes = journal.GetProperty("entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("commandType").GetString())
            .ToList();
        Assert.Equal(new[] { RunCommandTypes.StartRun, RunCommandTypes.InvokeCardZoneGameplayFlow,
            "START_ENCOUNTER", "PLAY_CARD" }, commandTypes);

        using var verifyResponse = await RawClient.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var verification = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        Assert.True(verification.GetProperty("isValid").GetBoolean(), verification.GetRawText());
    }

    [Fact]
    public async Task EntityCatalog_ExposesImmutableComponentDefinition()
    {
        var entity = await Client.GetEntityDefinitionAsync("player_warrior");

        AssertJsonPropertyEquals(entity, "definitionId", "player_warrior");
        AssertJsonPropertyEquals(entity, "displayName", "Warrior");
        var resources = entity.GetProperty("components").EnumerateArray()
            .Single(component => component.GetProperty("type").GetString() == "resources")
            .GetProperty("pools");
        Assert.Equal(150, GetJsonInt(resources.GetProperty("health"), "max"));
        Assert.Equal(10, GetJsonInt(resources.GetProperty("energy"), "max"));
    }
}
