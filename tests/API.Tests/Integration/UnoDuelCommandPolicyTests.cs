using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Integration;

public sealed class UnoDuelCommandPolicyTests
{
    [Fact]
    public async Task ExistingDuelKeepsPerRoundAndEnforcesItsFormerClientRuleOnTheServer()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(3);
        var game = new GameEngineClientSimulator(client);
        var found = false;
        foreach (var seed in new ulong[] { 7, 8, 13, 70, 123, 1, 2, 3, 4, 5, 6, 9, 10, 11, 12 })
        {
            var id = await game.StartRunAsync("uno-clash", "uno_clash_run", "player", seed, "uno_classic");
            var run = await Read(client, $"/api/v1/runs/{id}");
            var start = (await Read(client, $"/api/v1/runs/{id}/available-commands")).GetProperty("commands").EnumerateArray()
                .Single(command => command.GetProperty("type").GetString() == "START_ENCOUNTER");
            await Post(client, $"/api/v1/runs/{id}/commands", new { commandId = Guid.NewGuid(), type = "START_ENCOUNTER",
                expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = run.GetProperty("step").GetUInt64(), payload = start.GetProperty("validPayload") });
            run = await Read(client, $"/api/v1/runs/{id}");
            var combatId = run.GetProperty("activeEncounterId").GetGuid();
            var combat = await Read(client, $"/api/v1/combats/{combatId}");
            Assert.Equal("PerRound", combat.GetProperty("activationSequenceStrategy").GetString());
            Assert.Equal(2, combat.GetProperty("actors").GetArrayLength());
            await Post(client, $"/api/v1/combats/{combatId}/commands", new { commandId = Guid.NewGuid(), type = "EXECUTE_ACTION",
                expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = combat.GetProperty("step").GetUInt64(),
                payload = new { actorId = "player", actionType = "POWER", powerId = "uno_setup_table", targetIds = Array.Empty<string>() } });
            var legal = await Read(client, $"/api/v1/combats/{combatId}/legal-actions?actorId=player");
            if (legal.GetProperty("candidates").EnumerateArray().Any(candidate => candidate.GetProperty("command").GetProperty("actionType").GetString() == "PLAY_CARD")) continue;
            found = true;
            Assert.DoesNotContain(legal.GetProperty("candidates").EnumerateArray(), candidate => candidate.GetProperty("command").GetProperty("actionType").GetString() == "END_TURN");
            Assert.Contains(legal.GetProperty("blockedCommands").EnumerateArray(), item => item.GetProperty("code").GetString() == "DRAW_REQUIRED");
            run = await Read(client, $"/api/v1/runs/{id}"); combat = await Read(client, $"/api/v1/combats/{combatId}");
            using var denied = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new { commandId = Guid.NewGuid(), type = "END_TURN",
                expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = combat.GetProperty("step").GetUInt64(), payload = new { actorId = "player" } });
            Assert.False(denied.IsSuccessStatusCode);
            Assert.Equal(run.GetProperty("stateHash").GetString(), (await Read(client, $"/api/v1/runs/{id}")).GetProperty("stateHash").GetString());
            break;
        }
        Assert.True(found, "The fixed seed set must include an initially unplayable hand");
    }
    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path); var value = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, value.GetRawText()); return value;
    }
    private static async Task Post(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body); var value = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, value.GetRawText());
    }
}
