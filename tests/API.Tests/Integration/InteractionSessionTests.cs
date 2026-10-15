using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class InteractionSessionTests
{
    [Fact]
    public async Task SessionUsesOneRevisionPreservesStateAndRejectsStaleVersions()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var player = "session-" + Guid.NewGuid().ToString("N");
        var id = await game.StartRunAsync("ascendant", "ascendant_world_one_run", player, 250, "ascendant_world_one");
        var runs = factory.Services.GetRequiredService<IRunQueryService>();
        var before = runs.GetRun(id).Value;
        var hash = CanonicalJson.ComputeHash(before);
        using var response = await client.GetAsync($"/api/v1/runs/{id}/session?expectedSequence={before.Sequence}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(before.Sequence, session.GetProperty("version").GetProperty("runSequence").GetInt32());
        Assert.Equal(before.Sequence, session.GetProperty("cardPresentations").GetProperty("runSequence").GetInt32());
        Assert.False(session.GetProperty("run").TryGetProperty("encounters", out _));
        Assert.Equal(hash, CanonicalJson.ComputeHash(runs.GetRun(id).Value));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/runs/{id}/session");
        request.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var cached = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        using var stale = await client.GetAsync($"/api/v1/runs/{id}/session?expectedSequence={before.Sequence + 1}");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var start = session.GetProperty("commands").GetProperty("commands").EnumerateArray()
            .Single(value => value.GetProperty("type").GetString() == "START_ENCOUNTER");
        using var command = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
        {
            commandId = Guid.NewGuid(), type = "START_ENCOUNTER", payload = start.GetProperty("validPayload"),
            expectedSequence = start.GetProperty("expectedSequence").GetInt32(), expectedStep = start.GetProperty("expectedStep").GetUInt64()
        });
        Assert.True(command.IsSuccessStatusCode, await command.Content.ReadAsStringAsync());
        var active = runs.GetRun(id).Value;
        var activeHash = CanonicalJson.ComputeHash(active);
        using var combatResponse = await client.GetAsync($"/api/v1/runs/{id}/session?expectedSequence={active.Sequence}");
        Assert.True(combatResponse.IsSuccessStatusCode, await combatResponse.Content.ReadAsStringAsync());
        var combat = await combatResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(active.ActiveEncounterId, combat.GetProperty("combat").GetProperty("combatId").GetGuid());
        Assert.NotEmpty(combat.GetProperty("actions").GetProperty("candidates").EnumerateArray());
        Assert.Empty(combat.GetProperty("evaluations").GetProperty("cards").EnumerateArray());
        Assert.All(combat.GetProperty("cardPresentations").GetProperty("cards").EnumerateArray(), card =>
        {
            Assert.Equal("Source", card.GetProperty("faceValueMode").GetString());
            Assert.Empty(card.GetProperty("impacts").EnumerateArray());
            Assert.Equal(active.Sequence, card.GetProperty("version").GetProperty("runSequence").GetInt32());
        });
        Assert.Equal(activeHash, CanonicalJson.ComputeHash(runs.GetRun(id).Value));
        using var old = await client.GetAsync($"/api/v1/runs/{id}/session?expectedSequence={before.Sequence}");
        Assert.Equal(HttpStatusCode.Conflict, old.StatusCode);
    }
}
