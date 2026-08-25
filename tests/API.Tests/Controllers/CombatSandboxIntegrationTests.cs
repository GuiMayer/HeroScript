using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Integration")]
public sealed class CombatSandboxIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CombatSandboxIntegrationTests(TestWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task SandboxScenario_IsValidatedLaunchedAndIdempotentlyReopened()
    {
        var attemptKey = $"api-sandbox-{Guid.NewGuid():N}";
        var scenario = CreateScenario(attemptKey);

        using var validation = await _client.PostAsJsonAsync("/api/v1/sandbox/scenarios/validate", scenario);
        var validated = await validation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        Assert.Equal(64, validated.GetProperty("scenarioHash").GetString()!.Length);
        Assert.Equal("hero", validated.GetProperty("hero").GetProperty("entityId").GetString());

        using var launch = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", scenario);
        var first = await launch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.False(first.GetProperty("duplicate").GetBoolean());
        var runId = first.GetProperty("run").GetProperty("runId").GetGuid();
        var combatId = first.GetProperty("combat").GetProperty("combatId").GetGuid();
        Assert.Equal("combat_sandbox", first.GetProperty("run").GetProperty("modeId").GetString());
        Assert.Equal("goblin_a", first.GetProperty("combat").GetProperty("enemies")[0].GetProperty("entityId").GetString());

        using var repeated = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", scenario);
        var second = await repeated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.True(second.GetProperty("duplicate").GetBoolean());
        Assert.Equal(runId, second.GetProperty("run").GetProperty("runId").GetGuid());
        Assert.Equal(combatId, second.GetProperty("combat").GetProperty("combatId").GetGuid());

        using var snapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        Assert.Equal(runId, snapshot.GetProperty("run").GetProperty("runId").GetGuid());
        Assert.Equal(combatId, snapshot.GetProperty("combat").GetProperty("combatId").GetGuid());
        Assert.Equal(5, snapshot.GetProperty("hand").GetArrayLength());
        Assert.Contains(
            snapshot.GetProperty("combat").GetProperty("actors").EnumerateArray(),
            actor => actor.GetProperty("entityId").GetString() == "goblin_a");

        using var verification = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var replay = await verification.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());

        using var stored = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/scenario");
        var persisted = await stored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        Assert.Equal(attemptKey, persisted.GetProperty("attemptKey").GetString());
    }

    private static object CreateScenario(string attemptKey) => new
    {
        schemaVersion = 1,
        modeId = "combat_sandbox",
        seed = 983744UL,
        attemptKey,
        hero = new { alias = "hero", entityDefinitionId = "player_warrior" },
        deck = new[]
        {
            new { definitionId = "basic_attack" },
            new { definitionId = "basic_attack" },
            new { definitionId = "defend" },
            new { definitionId = "fireball" },
            new { definitionId = "heal" }
        },
        enemies = new[]
        {
            new { alias = "goblin_a", entityDefinitionId = "enemy_goblin" }
        },
        initialState = new { heroResources = new { energy = 3 } }
    };
}
