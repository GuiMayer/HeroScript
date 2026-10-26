using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration;

public sealed class DeckCompartmentJourneyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ThreeFreshJourneysAgreeOnGrantsCommandsPersistenceAndSemanticReplay()
    {
        List<string>? baseline = null;
        for (var repetition = 0; repetition < 3; repetition++)
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            var game = new GameEngineClientSimulator(client);
            var id = await game.StartRunAsync("compartment-lab", "compartment_lab_run", "player", 407,
                "compartment_lab_sandbox");
            var transcript = new List<string>();
            var timings = new List<double>();
            var store = factory.Services.GetRequiredService<IRunCommitStore>();
            var number = 0;
            var usedGrant = false;
            var usedRecovery = false;
            var blockedDuringCombat = false;
            var realocated = false;

            async Task<JsonElement> Command(string type, object payload, Guid? combatId = null, ulong? expectedStep = null)
            {
                var run = await Read(client, $"/api/v1/runs/{id}");
                var step = expectedStep ?? (combatId == null ? run.GetProperty("step").GetUInt64() :
                    (await Read(client, $"/api/v1/combats/{combatId}")).GetProperty("step").GetUInt64());
                var envelope = new { commandId = Guid.Parse($"73000000-0000-4000-8000-{++number:000000000000}"),
                    type, payload, expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = step };
                var path = combatId == null ? $"/api/v1/runs/{id}/commands" : $"/api/v1/combats/{combatId}/commands";
                var timer = Stopwatch.StartNew();
                var receipt = await Post(client, path, envelope);
                timings.Add(timer.Elapsed.TotalMilliseconds);
                transcript.Add(CanonicalJson.ComputeHash(receipt));
                var commit = await store.LoadCommitAsync(id, receipt.GetProperty("sequence").GetInt32());
                Assert.NotNull(commit);
                transcript.Add(CanonicalJson.ComputeHash(commit));
                usedGrant |= commit.Frames.SelectMany(f => f.Applications).Any(a => a.ResourceId == "block" && a.Identity?.ComponentId?.StartsWith("grant_") == true);
                usedRecovery |= commit.Frames.SelectMany(f => f.Applications).Any(a => a.ResourceId == "health" && a.Identity?.ComponentId?.StartsWith("grant_") == true);
                var duplicate = await Post(client, path, envelope);
                Assert.True(duplicate.GetProperty("duplicate").GetBoolean());
                Assert.Equal(receipt.GetProperty("stateHash").GetString(), duplicate.GetProperty("stateHash").GetString());
                return receipt;
            }

            var snapshot = await Read(client, $"/api/v1/runs/{id}/deck-compartments");
            Assert.True(snapshot.GetProperty("enabled").GetBoolean());
            Assert.True(snapshot.GetProperty("canChange").GetBoolean());
            var cards = snapshot.GetProperty("cards").EnumerateArray().ToArray();
            var strikes = cards.Where(c => c.GetProperty("definitionId").GetString() == "core_strike").Select(c => c.GetProperty("cardInstanceId").GetGuid()).ToArray();
            Assert.Equal(2, strikes.Length);
            var allocations = new[] { new { cardInstanceId = strikes[0], compartmentId = "ward" },
                new { cardInstanceId = strikes[1], compartmentId = "recovery" } };
            var hashBefore = (await game.GetRunStateAsync(id)).GetProperty("stateHash").GetString();
            var previewTimer = Stopwatch.StartNew();
            var preview = await Post(client, $"/api/v1/runs/{id}/deck-compartments/preview", new {
                expectedSequence = snapshot.GetProperty("runSequence").GetInt32(), expectedStep = snapshot.GetProperty("expectedStep").GetUInt64(), allocations });
            output.WriteLine($"Preview repetition {repetition + 1}: {previewTimer.Elapsed.TotalMilliseconds:F2}ms");
            Assert.Equal(hashBefore, (await game.GetRunStateAsync(id)).GetProperty("stateHash").GetString());
            var recoveredCard = preview.GetProperty("after").GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("cardInstanceId").GetGuid() == strikes[1]);
            Assert.Equal(2, recoveredCard.GetProperty("presentation").GetProperty("costs").GetArrayLength());
            Assert.Single(recoveredCard.GetProperty("presentation").GetProperty("conditions").EnumerateArray());
            await Command("APPLY_DECK_COMPARTMENTS", new { actorId = "player", allocations });
            var installed = await game.GetRunStateAsync(id);
            using (var restarted = new TestWebApplicationFactory(factory.PersistenceRoot))
            using (var reconnected = restarted.CreateClient())
            {
                Assert.Equal(installed.GetProperty("stateHash").GetString(), (await Read(reconnected, $"/api/v1/runs/{id}")).GetProperty("stateHash").GetString());
                Assert.Equal(2, (await Read(reconnected, $"/api/v1/runs/{id}/deck-compartments")).GetProperty("allocations").GetArrayLength());
            }
            // Fork a build snapshot before combat and change only the branch's allocations.
            var fork = await Post(client, $"/api/v1/runs/{id}/branches", new { sourceSequence = installed.GetProperty("sequence").GetInt32(), branchKey = "neutral-build" });
            var branchId = fork.GetProperty("runId").GetGuid();
            var branch = await Read(client, $"/api/v1/runs/{branchId}");
            await Post(client, $"/api/v1/runs/{branchId}/commands", new { commandId = Guid.Parse("74000000-0000-4000-8000-000000000001"), type = "APPLY_DECK_COMPARTMENTS",
                expectedSequence = branch.GetProperty("sequence").GetInt32(), expectedStep = branch.GetProperty("step").GetUInt64(), payload = new { actorId = "player", allocations = Array.Empty<object>() } });
            Assert.Equal(installed.GetProperty("stateHash").GetString(), (await game.GetRunStateAsync(id)).GetProperty("stateHash").GetString());
            Assert.Empty((await Read(client, $"/api/v1/runs/{branchId}/deck-compartments")).GetProperty("allocations").EnumerateArray());

            for (var guard = 0; guard < 100; guard++)
            {
                var run = await game.GetRunStateAsync(id);
                if (run.GetProperty("lifecycle").GetString() != "Active") break;
                if (run.GetProperty("activeEncounterId").ValueKind == JsonValueKind.String)
                {
                    var combatId = run.GetProperty("activeEncounterId").GetGuid();
                    var combat = await game.GetCombatStateAsync(combatId);
                    if (!blockedDuringCombat)
                    {
                        var view = await Read(client, $"/api/v1/runs/{id}/deck-compartments");
                        Assert.False(view.GetProperty("canChange").GetBoolean());
                        using var denied = await client.PostAsJsonAsync($"/api/v1/runs/{id}/deck-compartments/preview", new {
                            expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = run.GetProperty("step").GetUInt64(), allocations });
                        Assert.False(denied.IsSuccessStatusCode);
                        blockedDuringCombat = true;
                    }
                    if (combat.GetProperty("status").GetString() != "ACTIVE")
                    {
                        await Command("RESOLVE_COMBAT", new { combatId }, expectedStep: combat.GetProperty("step").GetUInt64());
                        continue;
                    }
                    var legal = (await Read(client, $"/api/v1/combats/{combatId}/legal-actions?actorId=player")).GetProperty("candidates").EnumerateArray().ToArray();
                    var chosen = legal.OrderBy(c => c.GetProperty("command").GetProperty("actionType").GetString() == "PLAY_CARD"
                        ? c.TryGetProperty("cardDefinitionId", out var definition) && definition.GetString() == "core_strike" ? 0 : 10 : 100).First();
                    var command = chosen.GetProperty("command");
                    var type = command.GetProperty("actionType").GetString() == "PLAY_CARD" ? "PLAY_CARD" :
                        command.GetProperty("actionType").GetString() == "END_TURN" ? "END_TURN" : "EXECUTE_ACTION";
                    var payload = command.EnumerateObject().Where(p => p.Name is "actorId" or "actionType" or "powerId" or "targetId" or "targetIds" or "costOptionId" or "cardInstanceId")
                        .Where(p => p.Value.ValueKind != JsonValueKind.Null).ToDictionary(p => p.Name, p => p.Value);
                    await Command(type, payload, combatId);
                }
                else
                {
                    if (run.GetProperty("currentNodeId").GetString() == "workshop_return" && !realocated)
                    {
                        await Command("APPLY_DECK_COMPARTMENTS", new { actorId = "player", allocations = allocations.Reverse().ToArray() });
                        realocated = true;
                    }
                    var commands = (await Read(client, $"/api/v1/runs/{id}/available-commands")).GetProperty("commands").EnumerateArray()
                        .Where(c => c.GetProperty("type").GetString() is "RESOLVE_NODE" or "ADVANCE_NODE" or "START_ENCOUNTER").ToArray();
                    Assert.NotEmpty(commands);
                    var chosen = commands.OrderBy(c => c.GetProperty("type").GetString() == "RESOLVE_NODE" ? 0 : 1).First();
                    var type = chosen.GetProperty("type").GetString()!;
                    object payload = type == "ADVANCE_NODE" ? new { targetNodeId = chosen.GetProperty("targetNodeIds")[0].GetString() } : chosen.GetProperty("validPayload");
                    await Command(type, payload, expectedStep: chosen.GetProperty("expectedStep").GetUInt64());
                }
            }
            var final = await game.GetRunStateAsync(id);
            Assert.Equal("Completed", final.GetProperty("lifecycle").GetString());
            Assert.True(usedGrant, "Granted block must actually execute through the ordinary effect processor");
            Assert.True(usedRecovery, "Granted healing must actually execute with additional cost/condition");
            Assert.True(realocated);
            foreach (var replayId in new[] { id, branchId })
            {
                var verify = await Post(client, $"/api/v1/runs/{replayId}/verify", null);
                Assert.True(verify.GetProperty("isValid").GetBoolean(), verify.GetRawText());
            }
            if (baseline == null) baseline = transcript; else Assert.Equal(baseline, transcript);
            output.WriteLine($"Journey {repetition + 1}: commands={number}; max={timings.Max():F2}ms; p95={timings.Order().ElementAt((int)(timings.Count * .95)):F2}ms");
        }
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, result.GetRawText());
        return result;
    }
    private static async Task<JsonElement> Post(HttpClient client, string path, object? body)
    {
        using var response = body == null ? await client.PostAsync(path, null) : await client.PostAsJsonAsync(path, body);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, result.GetRawText());
        return result;
    }
}
