using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Run;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Integration")]
public sealed class ActorResourceFoundationTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task AscendantExposesPinnedPersistentPlayerAndBindsEncounterToCustomPlayerIdentity()
    {
        using var client = factory.CreateClient();
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/settings");
        var setting = catalog.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("settingId").GetString() == "ascendant");
        var playerId = "persistent-" + Guid.NewGuid().ToString("N");
        using var start = await client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId = "ascendant", runDefinitionId = "ascendant_showcase_run", modeId = "ascendant_showcase",
            contentRevision = setting.GetProperty("currentRevision").GetString(), playerEntityId = playerId, seed = 456UL
        });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var run = await start.Content.ReadFromJsonAsync<JsonElement>();
        var runId = run.GetProperty("runId").GetGuid();
        var player = run.GetProperty("playerEntity");
        Assert.Equal(playerId, player.GetProperty("instanceId").GetString());
        var resources = player.GetProperty("components").EnumerateObject().Select(item => item.Value)
            .Single(item => item.GetProperty("type").GetString() == "resources").GetProperty("state");
        Assert.Equal(playerId, resources.GetProperty("ownerId").GetString());
        Assert.Equal(100, resources.GetProperty("resources").GetProperty("health").GetProperty("current").GetSingle());
        Assert.False(resources.GetProperty("resources").TryGetProperty("gold", out _));
        var commands = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        var command = commands.GetProperty("commands").EnumerateArray().Single(item => item.GetProperty("type").GetString() == RunCommandTypes.StartEncounter);
        using var enter = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(), type = RunCommandTypes.StartEncounter,
            expectedSequence = command.GetProperty("expectedSequence").GetInt32(), expectedStep = command.GetProperty("expectedStep").GetUInt64(),
            payload = command.GetProperty("validPayload")
        });
        Assert.True(enter.IsSuccessStatusCode, await enter.Content.ReadAsStringAsync());
        var active = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}");
        Assert.NotEqual(Guid.Empty, active.GetProperty("activeEncounterId").GetGuid());
        Assert.Equal(player.GetRawText(), active.GetProperty("playerEntity").GetRawText());
    }
}
