using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Integration;

public sealed class ExileCardPresentationFactsTests
{
    [Fact]
    public async Task ShippedStarterPublishesAnExileFlowAndLeavesThePlayableZone()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        const string player = "exile-starter";
        var runId = await game.StartRunAsync("ascendant", "ascendant_world_one_run", player, 154, "ascendant_world_one");
        var commands = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        var start = commands.GetProperty("commands").EnumerateArray().Single(c => c.GetProperty("type").GetString() == "START_ENCOUNTER");
        using var started = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(), type = "START_ENCOUNTER", payload = start.GetProperty("validPayload"),
            expectedSequence = start.GetProperty("expectedSequence").GetInt32(), expectedStep = start.GetProperty("expectedStep").GetUInt64()
        });
        Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
        var run = await game.GetRunStateAsync(runId);
        var combatId = run.GetProperty("activeEncounterId").GetGuid();
        var cardId = await game.GetPlayableCardInstanceIdAsync(runId, "w1_last_guard");
        var combat = await game.GetCombatStateAsync(combatId);
        using var played = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
        {
            commandId = Guid.NewGuid(), type = "PLAY_CARD", payload = new { actorId = player, cardInstanceId = cardId },
            expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = combat.GetProperty("step").GetUInt64()
        });
        Assert.True(played.IsSuccessStatusCode, await played.Content.ReadAsStringAsync());
        var receipt = await played.Content.ReadFromJsonAsync<JsonElement>();
        var steps = receipt.GetProperty("state").GetProperty("resolution").GetProperty("frames").EnumerateArray()
            .Where(frame => frame.TryGetProperty("cardZoneSteps", out _))
            .SelectMany(frame => frame.GetProperty("cardZoneSteps").EnumerateArray());
        Assert.Contains(steps, step => step.GetProperty("flowId").GetString() == "card.played.to-exhaust"
            && step.GetProperty("instanceIds").EnumerateArray().Any(id => id.GetGuid() == cardId));
        var zones = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/card-zones");
        Assert.Contains(zones.GetProperty("zones").EnumerateArray(), zone => zone.GetProperty("zoneId").GetString() == "exhaust"
            && zone.GetProperty("cards").EnumerateArray().Any(card => card.GetProperty("cardInstanceId").GetGuid() == cardId));
        Assert.DoesNotContain(zones.GetProperty("zones").EnumerateArray().Where(zone => zone.GetProperty("allowsCardPlay").GetBoolean())
            .SelectMany(zone => zone.GetProperty("cards").EnumerateArray()), card => card.GetProperty("cardInstanceId").GetGuid() == cardId);
    }
}
