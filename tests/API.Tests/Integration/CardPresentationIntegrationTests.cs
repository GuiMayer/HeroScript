using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

public sealed class CardPresentationIntegrationTests(TestWebApplicationFactory factory, ITestOutputHelper output)
    : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task RevisionedFacesAndIllegalCardInspectionStayReadableWithoutMutatingRun()
    {
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var player = "card-facts-" + Guid.NewGuid().ToString("N");
        var runId = await game.StartRunAsync("ascendant", "ascendant_world_one_run", player, 51209, "ascendant_world_one");
        async Task<JsonElement> Read(string path)
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
        var before = await Read($"/api/v1/runs/{runId}");
        var batch = await Read($"/api/v1/runs/{runId}/cards/presentations");
        Assert.Equal(before.GetProperty("sequence").GetInt32(), batch.GetProperty("runSequence").GetInt32());
        Assert.Equal(before.GetProperty("contentRevision").GetString(), batch.GetProperty("contentRevision").GetString());
        var strike = batch.GetProperty("cards").EnumerateArray().First(card => card.GetProperty("identity").GetProperty("definitionId").GetString() == "w1_strike");
        Assert.Equal("ComposedBase", strike.GetProperty("faceValueMode").GetString());
        Assert.NotEmpty(strike.GetProperty("effects").EnumerateArray());
        var catalog = await Read($"/api/v1/runs/{runId}/card-modifier-items");
        Assert.Equal(11, catalog.GetProperty("catalog").GetArrayLength());
        Assert.Empty(catalog.GetProperty("items").EnumerateArray());
        var timer = Stopwatch.StartNew();
        var cached = await Read($"/api/v1/runs/{runId}/cards/presentations");
        timer.Stop();
        output.WriteLine($"Cached card facts read: {timer.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(batch.GetRawText(), cached.GetRawText());
        Assert.Equal(before.GetProperty("stateHash").GetString(), (await Read($"/api/v1/runs/{runId}")).GetProperty("stateHash").GetString());
        var commands = (await Read($"/api/v1/runs/{runId}/available-commands")).GetProperty("commands");
        var start = commands.EnumerateArray().Single(command => command.GetProperty("type").GetString() == "START_ENCOUNTER");
        using var started = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new {
            commandId = Guid.NewGuid(), type = "START_ENCOUNTER", payload = start.GetProperty("validPayload"),
            expectedSequence = start.GetProperty("expectedSequence").GetInt32(), expectedStep = start.GetProperty("expectedStep").GetUInt64() });
        Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
        var combatRun = await Read($"/api/v1/runs/{runId}");
        var combatId = combatRun.GetProperty("activeEncounterId").GetGuid();
        var cards = (await Read($"/api/v1/combats/{combatId}/cards/evaluations?actorId={player}")).GetProperty("cards");
        var inspected = cards.EnumerateArray().First(card => card.GetProperty("presentation").GetProperty("identity").GetProperty("definitionId").GetString() == "w1_strike");
        Assert.False(inspected.GetProperty("isPlayable").GetBoolean()); // No target selected.
        Assert.False(inspected.GetProperty("previewScope").GetProperty("hasExecutablePreview").GetBoolean());
        var facts = inspected.GetProperty("presentation");
        Assert.Equal("Source", facts.GetProperty("faceValueMode").GetString());
        var amount = facts.GetProperty("effects")[0].GetProperty("parameters")[0].GetProperty("values");
        Assert.Equal(JsonValueKind.Number, amount.GetProperty("source").ValueKind);
        Assert.Empty(facts.GetProperty("impacts").EnumerateArray());
        Assert.NotEmpty(facts.GetProperty("explanations").EnumerateArray());
        Assert.Equal(combatRun.GetProperty("stateHash").GetString(), (await Read($"/api/v1/runs/{runId}")).GetProperty("stateHash").GetString());
    }
}
