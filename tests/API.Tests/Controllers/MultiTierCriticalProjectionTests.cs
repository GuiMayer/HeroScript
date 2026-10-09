using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Controllers;

public sealed class MultiTierCriticalProjectionTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task CriticalInspectionPublishesLocalAlternativesWithoutChangingCanonicalState()
    {
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync("ascendant", "ascendant_showcase_run", "critical-preview", 150,
            "ascendant_showcase");
        var commands = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        var start = commands.GetProperty("commands").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "START_ENCOUNTER");
        using var enter = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(), type = "START_ENCOUNTER", payload = start.GetProperty("validPayload"),
            expectedSequence = start.GetProperty("expectedSequence").GetInt32(), expectedStep = start.GetProperty("expectedStep").GetUInt64()
        });
        Assert.True(enter.IsSuccessStatusCode, await enter.Content.ReadAsStringAsync());
        // Setup is a canonical command, not a mutation of the test's in-memory snapshot.
        await game.DrawCardsAsync(runId, 5);
        var cardId = await game.GetPlayableCardInstanceIdAsync(runId, "ascendant_critical_lance");
        var before = await game.GetRunStateAsync(runId);
        var combatId = before.GetProperty("activeEncounterId").GetGuid();
        var combatBefore = await game.GetCombatStateAsync(combatId);
        string? baseline = null;
        for (var repetition = 0; repetition < 10; repetition++)
        {
            using var response = await client.GetAsync($"/api/v1/combats/{combatId}/cards/{cardId}/evaluation?actorId=critical-preview&targetIds=drone");
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            if (baseline == null) baseline = body;
            else Assert.Equal(baseline, body);
            using var json = JsonDocument.Parse(body);
            var preview = json.RootElement;
            Assert.True(preview.GetProperty("previewScope").GetProperty("dependsOnRandomInputs").GetBoolean());
            Assert.Equal("SampledPathNotGuaranteedOutcome", preview.GetProperty("previewScope").GetProperty("validity").GetString());
            Assert.False(preview.GetProperty("randomOutcomes").GetProperty("isGlobalOutcomeRange").GetBoolean());
            var impact = Assert.Single(preview.GetProperty("randomOutcomes").GetProperty("impacts").EnumerateArray());
            Assert.Equal(.5f, impact.GetProperty("probability").GetSingle());
            Assert.Equal(150f, impact.GetProperty("captures").GetProperty("chance").GetProperty("value").GetSingle());
            Assert.Equal(new[] { 21f, 31f }, impact.GetProperty("alternatives").EnumerateArray()
                .Select(item => item.GetProperty("calculation").GetProperty("value").GetSingle()));
        }
        Assert.Equal(before.GetRawText(), (await game.GetRunStateAsync(runId)).GetRawText());
        Assert.Equal(combatBefore.GetRawText(), (await game.GetCombatStateAsync(combatId)).GetRawText());
    }
}
