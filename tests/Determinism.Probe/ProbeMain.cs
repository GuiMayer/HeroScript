using System.Net.Http.Json;
using System.Text.Json;
using API.Tests;
using API.Tests.Helpers;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Microsoft.Extensions.DependencyInjection;

// Independent OS process: string/hash-set salts must never alter canonical gameplay facts.
internal static class ProbeMain
{
    public static async Task Main()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync("ascendant", "ascendant_showcase_run", "process-probe", 150, "ascendant_showcase");
        var store = factory.Services.GetRequiredService<IRunCommitStore>();
        var transcript = new List<string> { CanonicalJson.ComputeHash(await store.LoadCommitAsync(runId, 1)) };
        var commandNumber = 0;
        async Task Verify()
        {
            using var response = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
            var verification = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (!response.IsSuccessStatusCode || !verification.GetProperty("isValid").GetBoolean())
                throw new InvalidOperationException(verification.GetRawText());
        }
        async Task Command(string type, object payload, Guid? combatId = null)
        {
            var run = await game.GetRunStateAsync(runId);
            var step = combatId == null ? run.GetProperty("step").GetUInt64() : (await game.GetCombatStateAsync(combatId.Value)).GetProperty("step").GetUInt64();
            using var response = await client.PostAsJsonAsync(combatId == null ? $"/api/v1/runs/{runId}/commands" : $"/api/v1/combats/{combatId}/commands", new {
                commandId = Guid.Parse($"83000000-0000-4000-8000-{++commandNumber:000000000000}"), type, payload,
                expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = step });
            var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{type}: {receipt}");
            transcript.Add(CanonicalJson.ComputeHash(receipt));
            transcript.Add(CanonicalJson.ComputeHash(await store.LoadCommitAsync(runId, receipt.GetProperty("sequence").GetInt32())));
        }
        var available = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        await Command("START_ENCOUNTER", available.GetProperty("commands").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "START_ENCOUNTER").GetProperty("validPayload"));
        var combat = (await game.GetRunStateAsync(runId)).GetProperty("activeEncounterId").GetGuid();
        await Command("INVOKE_CARD_ZONE_GAMEPLAY_FLOW", new { flowId = "run.draw", requestedCount = 5 });
        var card = await game.GetPlayableCardInstanceIdAsync(runId, "ascendant_critical_lance");
        await Command("PLAY_CARD", new { actorId = "process-probe", cardInstanceId = card, targetIds = new[] { "drone" } }, combat);
        await Command("END_TURN", new { actorId = "process-probe" }, combat);
        await Verify();
        var criticalHash = (await game.GetRunStateAsync(runId)).GetProperty("stateHash").GetString();

        // Exercise persisted payload sets too, not only execution identities and intent previews.
        runId = await game.StartRunAsync("volatile-core", "core_volatile_run", "process-probe", 922708, "core_volatile");
        transcript.Add(CanonicalJson.ComputeHash(await store.LoadCommitAsync(runId, 1)));
        available = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        await Command("START_ENCOUNTER", available.GetProperty("commands").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "START_ENCOUNTER").GetProperty("validPayload"));
        combat = (await game.GetRunStateAsync(runId)).GetProperty("activeEncounterId").GetGuid();
        await Command("INVOKE_CARD_ZONE_GAMEPLAY_FLOW", new { flowId = "run.draw", requestedCount = 10 });
        card = await game.GetPlayableCardInstanceIdAsync(runId, "core_charge_card");
        await Command("PLAY_CARD", new { actorId = "process-probe", cardInstanceId = card, targetIds = new[] { "process-probe" } }, combat);
        var state = await store.LoadLatestStateAsync(runId);
        if (!state!.Encounters.Any(encounter => encounter.Combat?.StatusEffects.Values.Any(statuses => statuses.Any(status => !status.PayloadLots.IsEmpty)) == true))
            throw new InvalidOperationException("Probe must persist an actual stack payload lot");
        await Command("END_TURN", new { actorId = "process-probe" }, combat);
        await Verify();
        Console.WriteLine("HEROSCRIPT_PROBE:" + CanonicalJson.Serialize(new { engineVersion = DeterministicContext.CurrentEngineVersion, transcript, criticalHash,
            finalHash = (await game.GetRunStateAsync(runId)).GetProperty("stateHash").GetString() }));
    }
}
