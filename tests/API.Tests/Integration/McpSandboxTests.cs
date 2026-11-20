using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeroScript.Mcp;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace API.Tests.Integration;

public sealed class McpSandboxTests
{
    [Fact]
    public async Task DedicatedSandboxUsesActualEngineAndDoesNotChangeSourceHead()
    {
        using var root = new TestWebApplicationFactory();
        using var ordinary = root.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await ordinary.GetAsync("/api/v1/diagnostics/mcp-sandbox")).StatusCode);
        using var isolatedRoot = new TestWebApplicationFactory();
        using var isolated = isolatedRoot.WithWebHostBuilder(b => b.UseEnvironment("McpSandbox").UseSetting("McpSandbox:PersistenceRoot", isolatedRoot.PersistenceRoot));
        using var client = isolated.CreateClient();
        var revisions = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/revisions?configName=default");
        var revision = revisions.GetProperty("currentRevision").GetString()!;
        using var session = new SandboxSession(new HeroScriptApiClient(new Uri("http://localhost"), isolated.Server.CreateHandler()));
        var tools = new SimulationTools(session);
        var scenario = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2, modeId = "combat_sandbox",
            participants = new object[] { new { instanceId = "hero", entityDefinitionId = "player_warrior", sideId = "player", controllerBinding = new { kind = "Player" } },
                new { instanceId = "goblin_a", entityDefinitionId = "enemy_goblin", sideId = "opposition", controllerBinding = new { kind = "AI", policyId = "gambit" } } },
            startingCards = new[] { new { definitionId = "basic_attack" }, new { definitionId = "defend" }, new { definitionId = "fireball" }, new { definitionId = "heal" } }
        });
        var encounter = await tools.Encounter("default", revision, 983744, scenario, default, maxCommands: 2);
        Assert.NotEmpty(encounter.Actions);
        Assert.Null(encounter.Error);
        var run = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{encounter.RunId}");
        var branch = await tools.Branch(encounter.RunId, run.GetProperty("sequence").GetInt32(), [], default);
        Assert.Contains("sourceUnchanged = True", branch.ToString());
    }
}
