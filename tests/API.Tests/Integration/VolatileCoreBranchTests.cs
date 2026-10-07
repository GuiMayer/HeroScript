using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Integration;

public sealed class VolatileCoreBranchTests
{
    [Fact]
    public async Task TransformedCardsPersistAndBranchesDivergeWithoutEditingTheParent()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var revision = await game.GetCurrentContentRevisionAsync("volatile-core");
        using var started = await client.PostAsJsonAsync("/api/v1/runs", new
        { settingId = "volatile-core", runDefinitionId = "core_volatile_run", playerEntityId = "player", seed = 922709,
            modeId = "core_volatile_sandbox", contentRevision = revision });
        var startResult = await started.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(started.IsSuccessStatusCode, startResult.GetRawText());
        var id = startResult.GetProperty("runId").GetGuid();
        var run = await game.GetRunStateAsync(id);
        var zones = (await Read(client, $"/api/v1/runs/{id}/card-zones")).GetProperty("zones");
        var card = zones.EnumerateArray().SelectMany(zone => zone.GetProperty("cards").EnumerateArray())
            .First(card => card.GetProperty("definitionId").GetString() == "core_strike").GetProperty("cardInstanceId").GetGuid();
        foreach (var upgradeId in new[] { "core_ember", "core_multihit", "core_cascade", "core_sharpen" })
            await Send(client, id, "UPGRADE_CARD", new { cardInstanceId = card, upgradeId });
        var transformed = await game.GetRunStateAsync(id);
        using (var restarted = new TestWebApplicationFactory(factory.PersistenceRoot))
        using (var reconnected = restarted.CreateClient())
            Assert.Equal(transformed.GetProperty("stateHash").GetString(), (await Read(reconnected, $"/api/v1/runs/{id}")).GetProperty("stateHash").GetString());
        var advertised = (await Read(client, $"/api/v1/runs/{id}/available-commands")).GetProperty("commands");
        var start = advertised.EnumerateArray().First(command => command.GetProperty("type").GetString() == "START_ENCOUNTER");
        await Send(client, id, "START_ENCOUNTER", start.GetProperty("validPayload"));
        var ready = await game.GetRunStateAsync(id);
        var combatId = ready.GetProperty("activeEncounterId").GetGuid();
        var candidates = (await Read(client, $"/api/v1/combats/{combatId}/legal-actions?actorId=player")).GetProperty("candidates").EnumerateArray().ToArray();
        var charge = candidates.First(candidate => candidate.TryGetProperty("cardDefinitionId", out var definition) && definition.GetString() == "core_charge_card");
        await Play(client, game, id, combatId, charge);
        var accumulated = await game.GetRunStateAsync(id);
        using (var restarted = new TestWebApplicationFactory(factory.PersistenceRoot))
        using (var reconnected = restarted.CreateClient())
            Assert.Equal(accumulated.GetProperty("stateHash").GetString(), (await Read(reconnected, $"/api/v1/runs/{id}")).GetProperty("stateHash").GetString());
        using var forkResponse = await client.PostAsJsonAsync($"/api/v1/runs/{id}/branches", new
        { sourceSequence = accumulated.GetProperty("sequence").GetInt32(), branchKey = "consume-instead" });
        var fork = await forkResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(forkResponse.IsSuccessStatusCode, fork.GetRawText());
        var branchId = fork.GetProperty("runId").GetGuid();
        var branch = await game.GetRunStateAsync(branchId);
        var branchCombat = branch.GetProperty("activeEncounterId").GetGuid();
        var branchCandidates = (await Read(client, $"/api/v1/combats/{branchCombat}/legal-actions?actorId=player")).GetProperty("candidates").EnumerateArray().ToArray();
        var release = branchCandidates.First(candidate => candidate.TryGetProperty("cardDefinitionId", out var definition) && definition.GetString() == "core_release");
        await Play(client, game, branchId, branchCombat, release);
        var afterBranch = await game.GetRunStateAsync(branchId);
        Assert.NotEqual(accumulated.GetProperty("stateHash").GetString(), afterBranch.GetProperty("stateHash").GetString());
        Assert.Equal(accumulated.GetProperty("stateHash").GetString(), (await game.GetRunStateAsync(id)).GetProperty("stateHash").GetString());
        using (var restarted = new TestWebApplicationFactory(factory.PersistenceRoot))
        using (var reconnected = restarted.CreateClient())
            Assert.Equal(afterBranch.GetProperty("stateHash").GetString(), (await Read(reconnected, $"/api/v1/runs/{branchId}")).GetProperty("stateHash").GetString());
        foreach (var runId in new[] { id, branchId })
        {
            using var verify = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
            var result = await verify.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(verify.IsSuccessStatusCode && result.GetProperty("isValid").GetBoolean(), result.GetRawText());
        }
    }

    private static async Task Play(HttpClient client, GameEngineClientSimulator game, Guid runId, Guid combatId, JsonElement candidate)
    {
        var run = await game.GetRunStateAsync(runId);
        var combat = await game.GetCombatStateAsync(combatId);
        var payload = candidate.GetProperty("command").EnumerateObject().Where(property =>
            property.Name is "actorId" or "actionType" or "powerId" or "targetId" or "targetIds" or "costOptionId" or "cardInstanceId")
            .Where(property => property.Value.ValueKind != JsonValueKind.Null).ToDictionary(property => property.Name, property => property.Value);
        using var response = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
        { commandId = Guid.NewGuid(), type = "PLAY_CARD", expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = combat.GetProperty("step").GetUInt64(), payload });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task Send(HttpClient client, Guid id, string type, object payload)
    {
        var run = await Read(client, $"/api/v1/runs/{id}");
        using var response = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
        { commandId = Guid.NewGuid(), type, payload, expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = run.GetProperty("step").GetUInt64() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, result.GetRawText());
        return result;
    }
}
