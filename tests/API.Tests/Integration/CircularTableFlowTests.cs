using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration;

public sealed class CircularTableFlowTests
{
    [Fact]
    public async Task PublishedTableStartsWithFourPrivateHandsAndARealSharedPool()
    {
        var first = await RunJourney(8, false);
        Assert.Equal(first.Take(8), await RunJourney(8, false));
    }

    [Fact]
    [Trait("Category", "ExtendedIntegration")]
    public async Task WholeCircularJourneyFinishesAndReexecutesItsHistory() => await RunJourney(150, true);

    private static async Task<List<string>> RunJourney(int commandLimit, bool requireFinished)
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(15);
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
        var store = factory.Services.GetRequiredService<IRunCommitStore>();
        var tape = new List<string>(); var actions = 0;
        for (; actions < commandLimit; actions++)
        {
            combat = await Read(client, $"/api/v1/combats/{combatId}");
            if (combat.GetProperty("status").GetString() != "ACTIVE") break;
            legal = await Read(client, $"/api/v1/combats/{combatId}/legal-actions?actorId=seat_0");
            var candidates = legal.GetProperty("candidates").EnumerateArray().ToArray();
            Assert.NotEmpty(candidates);
            var chosen = candidates.OrderBy(candidate => {
                var cmd = candidate.GetProperty("command"); var power = cmd.GetProperty("powerId").GetString() ?? "";
                return power.StartsWith("t_choose_", StringComparison.Ordinal) ? 0 : power == "t_accept" ? 1 : power == "t_call" ? 2 :
                    cmd.GetProperty("actionType").GetString() == "PLAY_CARD" ? 3 : power == "t_draw" ? 4 : cmd.GetProperty("actionType").GetString() == "END_TURN" ? 5 : 6;
            }).First().GetProperty("command");
            currentRun = await Read(client, $"/api/v1/runs/{id}");
            var actionType = chosen.GetProperty("actionType").GetString()!;
            var envelope = new { commandId = Guid.Parse($"84000000-0000-4000-8000-{actions + 2:000000000000}"),
                type = actionType is "PLAY_CARD" or "END_TURN" ? actionType : "EXECUTE_ACTION",
                expectedSequence = currentRun.GetProperty("sequence").GetInt32(), expectedStep = combat.GetProperty("step").GetUInt64(),
                payload = new { actorId = "seat_0", actionType, powerId = chosen.GetProperty("powerId").GetString(),
                    cardInstanceId = chosen.GetProperty("cardInstanceId").ValueKind == JsonValueKind.String ? chosen.GetProperty("cardInstanceId").GetGuid() : (Guid?)null,
                    targetIds = chosen.GetProperty("targetIds").EnumerateArray().Select(target => target.GetString()).ToArray() } };
            using var actionResponse = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", envelope);
            var receipt = await actionResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(actionResponse.IsSuccessStatusCode, receipt.GetRawText());
            using var retryResponse = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", envelope);
            var retry = await retryResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(retry.GetProperty("duplicate").GetBoolean());
            Assert.Equal(receipt.GetProperty("stateHash").GetString(), retry.GetProperty("stateHash").GetString());
            tape.Add(CanonicalJson.ComputeHash((await store.LoadLatestStateAsync(id))!));
        }
        combat = await Read(client, $"/api/v1/combats/{combatId}");
        if (requireFinished) {
            Assert.True(actions < commandLimit, "The scripted reference game must finish");
            Assert.Single(combat.GetProperty("participantVictory").GetProperty("actorIds").EnumerateArray());
        }
        var finalHash = CanonicalJson.ComputeHash((await store.LoadLatestStateAsync(id))!);
        using (var reloaded = new TestWebApplicationFactory(factory.PersistenceRoot))
        using (var reconnected = reloaded.CreateClient())
        {
            Assert.Equal(combat.GetProperty("stateHash").GetString(), (await Read(reconnected, $"/api/v1/combats/{combatId}")).GetProperty("stateHash").GetString());
        }
        using var branchResponse = await client.PostAsJsonAsync($"/api/v1/runs/{id}/branches", new { sourceSequence = 2, branchKey = "table-opening" });
        var branch = await branchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(branchResponse.IsSuccessStatusCode, branch.GetRawText());
        Assert.Equal(finalHash, CanonicalJson.ComputeHash((await store.LoadLatestStateAsync(id))!));
        using var verifyResponse = await client.PostAsync($"/api/v1/runs/{id}/verify", null);
        var verify = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verify.GetProperty("isValid").GetBoolean(), verify.GetRawText());
        Assert.True(verify.GetProperty("reexecuted").GetBoolean());
        return tape;
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path); var value = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, value.GetRawText()); return value;
    }
}
