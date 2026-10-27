using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Integration;

public sealed class CircularTableFlowTests
{
    [Fact]
    public async Task PublishedTableStartsWithFourPrivateHandsAndARealSharedPool()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        using var revisionResponse = await client.GetAsync("/api/v1/content/revisions?configName=circular-table");
        var revision = await revisionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(revisionResponse.IsSuccessStatusCode, revision.GetRawText());
        using var startedResponse = await client.PostAsJsonAsync("/api/v1/runs", new {
            settingId = "circular-table", runDefinitionId = "table_run", playerEntityId = "seat_0", seed = 70UL,
            contentRevision = revision.GetProperty("currentRevision").GetString(), modeId = "table_mode" });
        var started = await startedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(startedResponse.IsSuccessStatusCode, started.GetRawText());
        var id = started.GetProperty("runId").GetGuid();
        var run = await Read(client, $"/api/v1/runs/{id}");
        var command = (await Read(client, $"/api/v1/runs/{id}/available-commands")).GetProperty("commands").EnumerateArray()
            .Single(value => value.GetProperty("type").GetString() == "START_ENCOUNTER");
        using var encounterResponse = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new {
            commandId = Guid.Parse("84000000-0000-4000-8000-000000000001"), type = "START_ENCOUNTER",
            expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = run.GetProperty("step").GetUInt64(), payload = command.GetProperty("validPayload") });
        var encounter = await encounterResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(encounterResponse.IsSuccessStatusCode, encounter.GetRawText());
        var zones = (await Read(client, $"/api/v1/runs/{id}/card-zones")).GetProperty("zones").EnumerateArray().ToArray();
        Assert.Equal(4, zones.Count(zone => zone.GetProperty("zoneId").GetString() == "hand"));
        Assert.All(zones.Where(zone => zone.GetProperty("zoneId").GetString() == "hand"), zone => Assert.Equal(7, zone.GetProperty("count").GetInt32()));
        Assert.Equal(79, zones.Single(zone => zone.GetProperty("zoneId").GetString() == "draw").GetProperty("count").GetInt32());
        Assert.Equal(1, zones.Single(zone => zone.GetProperty("zoneId").GetString() == "table").GetProperty("count").GetInt32());
        var currentRun = await Read(client, $"/api/v1/runs/{id}");
        var combatId = currentRun.GetProperty("activeEncounterId").GetGuid();
        var combat = await Read(client, $"/api/v1/combats/{combatId}");
        Assert.Equal("Circular", combat.GetProperty("activationSequenceStrategy").GetString());
        Assert.Equal("seat_1", combat.GetProperty("nextScheduledActorId").GetString());
        var legal = await Read(client, $"/api/v1/combats/{combatId}/legal-actions?actorId=seat_0");
        Assert.True(legal.TryGetProperty("blockedCommands", out _));
        Assert.All(legal.GetProperty("candidates").EnumerateArray().Where(value => value.GetProperty("command").GetProperty("actionType").GetString() == "PLAY_CARD"),
            value => Assert.Contains(value.GetProperty("command").GetProperty("cardInstanceId").GetGuid(),
                zones.Single(zone => zone.GetProperty("zoneId").GetString() == "hand" && zone.GetProperty("ownerId").GetString() == "seat_0").GetProperty("cards").EnumerateArray().Select(card => card.GetProperty("cardInstanceId").GetGuid())));
        using var verifyResponse = await client.PostAsync($"/api/v1/runs/{id}/verify", null);
        var verify = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verify.GetProperty("isValid").GetBoolean(), verify.GetRawText());
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path); var value = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, value.GetRawText()); return value;
    }
}
