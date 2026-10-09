using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

/// <summary>Real command gateway, new runtime/store per repetition; no simulated rules.</summary>
public sealed class VolatileCoreJourneyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TenFreshRuntimesAgreeOnWholeJourneyReceiptsAndSemanticReplay()
    {
        List<string>? baseline = null;
        List<string>? baselineCommits = null;
        var timings = new List<double>();
        var commandTimings = new List<(int Sequence, double Milliseconds)>();
        var evaluationTimings = new List<double>();
        for (var repetition = 0; repetition < 10; repetition++)
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            var game = new GameEngineClientSimulator(client);
            var runId = await game.StartRunAsync("volatile-core", "core_volatile_run", "player", 922708, "core_volatile");
            var receipts = new List<string>();
            var commitHashes = new List<string>();
            var store = factory.Services.GetRequiredService<IRunCommitStore>();
            var steps = 0;
            var usedUpgrades = new HashSet<string>();
            var coreCard = Guid.Empty;
            var condensed = false;
            var continued = false;
            var recoveredHop = false;
            while (steps < 160)
            {
                var run = await game.GetRunStateAsync(runId);
                if (run.GetProperty("lifecycle").GetString() != "Active") break;
                string type;
                object payload;
                string route;
                ulong step = run.GetProperty("step").GetUInt64();
                if (run.GetProperty("activeEncounterId").ValueKind == JsonValueKind.String)
                {
                    var combatId = run.GetProperty("activeEncounterId").GetGuid();
                    var combat = await game.GetCombatStateAsync(combatId);
                    if (combat.GetProperty("status").GetString() != "ACTIVE")
                    {
                        type = "RESOLVE_COMBAT";
                        payload = new { combatId };
                        route = $"/api/v1/runs/{runId}/commands";
                        step = combat.GetProperty("step").GetUInt64();
                    }
                    else
                    {
                        var legal = await Read(client, $"/api/v1/combats/{combatId}/legal-actions?actorId=player");
                        var candidates = (legal.ValueKind == JsonValueKind.Array ? legal : legal.GetProperty("candidates")).EnumerateArray().ToArray();
                        Assert.NotEmpty(candidates);
                        // Exercise the transformed instance, not whichever equal-ranked ID sorts first.
                        // IDs include the engine/content revision and their lexical order is not gameplay policy.
                        var selected = candidates.OrderBy(candidate => Rank(candidate,
                            usedUpgrades.Contains("core_cascade") ? coreCard : Guid.Empty, !continued)).First();
                        type = selected.GetProperty("command").GetProperty("actionType").GetString()!;
                        if (type is not ("PLAY_CARD" or "END_TURN")) type = "EXECUTE_ACTION";
                        payload = selected.GetProperty("command").EnumerateObject()
                            .Where(property => property.Name is "actorId" or "actionType" or "powerId" or "targetId" or "targetIds" or "costOptionId" or "cardInstanceId")
                            .Where(property => property.Value.ValueKind != JsonValueKind.Null)
                            .ToDictionary(property => property.Name, property => property.Value);
                        route = $"/api/v1/combats/{combatId}/commands";
                        step = combat.GetProperty("step").GetUInt64();
                        var timer = Stopwatch.StartNew();
                        var before = run.GetProperty("stateHash").GetString();
                        var evaluations = await Read(client, $"/api/v1/combats/{combatId}/cards/evaluations?actorId=player");
                        timings.Add(timer.Elapsed.TotalMilliseconds);
                        evaluationTimings.Add(timer.Elapsed.TotalMilliseconds);
                        Assert.Equal(before, (await game.GetRunStateAsync(runId)).GetProperty("stateHash").GetString());
                        foreach (var card in evaluations.GetProperty("cards").EnumerateArray())
                            Assert.Equal(run.GetProperty("sequence").GetInt32(), card.GetProperty("version").GetProperty("runSequence").GetInt32());
                    }
                }
                else
                {
                    route = $"/api/v1/runs/{runId}/commands";
                    var commands = (await Read(client, $"/api/v1/runs/{runId}/available-commands")).GetProperty("commands").EnumerateArray().ToArray();
                    var command = commands.Where(item => item.GetProperty("type").GetString() != "ABANDON_RUN")
                        .OrderBy(item => item.GetProperty("type").GetString() == "RESOLVE_NODE" ? 0 : 1).First();
                    type = command.GetProperty("type").GetString()!;
                    var valid = command.GetProperty("validPayload");
                    payload = valid;
                    switch (type)
                    {
                        case "ADVANCE_NODE": payload = new { targetNodeId = command.GetProperty("targetNodeIds")[0].GetString() }; break;
                        case "PICK_CARD_REWARD": payload = new { selectionInstanceId = valid.GetProperty("selectionInstanceId").GetGuid(), cardIds = new[] { valid.GetProperty("cardIds")[0].GetString() } }; break;
                        case "UPGRADE_CARD":
                            var options = valid.GetProperty("options").EnumerateArray().ToArray();
                            var preference = new[] { "core_ember", "core_multihit", "core_cascade", "core_sharpen" }.First(id => !usedUpgrades.Contains(id) && options.Any(option => option.GetProperty("upgradeId").GetString() == id));
                            var chosen = options.First(option => option.GetProperty("upgradeId").GetString() == preference && option.GetProperty("cardDefinitionId").GetString() == "core_strike" && (coreCard == Guid.Empty || option.GetProperty("cardInstanceId").GetGuid() == coreCard));
                            coreCard = chosen.GetProperty("cardInstanceId").GetGuid();
                            var preview = await Read(client, $"/api/v1/runs/{runId}/cards/{coreCard}/transformation-preview?upgradeId={preference}");
                            Assert.True(preview.GetProperty("isCompatible").GetBoolean(), preview.GetRawText());
                            Assert.NotEqual(preview.GetProperty("before").GetProperty("fingerprint").GetString(), preview.GetProperty("after").GetProperty("fingerprint").GetString());
                            payload = new { cardInstanceId = coreCard, upgradeId = preference };
                            usedUpgrades.Add(preference);
                            break;
                        case "APPLY_PREPARATION_OPTION": payload = new { preparationInstanceId = valid.GetProperty("preparationInstanceId").GetGuid(), optionId = "train_power" }; break;
                    }
                }
                var commandId = Guid.Parse($"90000000-0000-4000-8000-{++steps:000000000000}");
                var envelope = new { commandId, type, payload, expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = step };
                var commandTimer = Stopwatch.StartNew();
                using var response = await client.PostAsJsonAsync(route, envelope);
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(response.IsSuccessStatusCode, $"{type}: {json}");
                timings.Add(commandTimer.Elapsed.TotalMilliseconds);
                commandTimings.Add((steps, commandTimer.Elapsed.TotalMilliseconds));
                receipts.Add(json.GetRawText());
                var committed = await store.LoadCommitAsync(runId, json.GetProperty("sequence").GetInt32());
                Assert.NotNull(committed);
                commitHashes.Add(CanonicalJson.ComputeHash(committed));
                if (type == "APPLY_PREPARATION_OPTION")
                    Assert.Contains(committed.Frames.SelectMany(frame => frame.Applications),
                        application => application.AttributeOutcome != null);
                condensed |= json.GetRawText().Contains("\"condensation\":{");
                continued |= json.GetRawText().Contains("\"toEntityId\":\"enemy_");
                if (steps == 2 || (continued && !recoveredHop))
                {
                    using var restarted = new TestWebApplicationFactory(factory.PersistenceRoot);
                    using var reconnected = restarted.CreateClient();
                    var recovered = await Read(reconnected, $"/api/v1/runs/{runId}");
                    Assert.Equal((await game.GetRunStateAsync(runId)).GetProperty("stateHash").GetString(), recovered.GetProperty("stateHash").GetString());
                    recoveredHop |= continued;
                }
            }
            var final = await game.GetRunStateAsync(runId);
            Assert.Equal("Completed", final.GetProperty("lifecycle").GetString());
            Assert.True(condensed, "Journey must actually condense, not merely contain a card definition");
            Assert.NotEmpty(usedUpgrades);
            Assert.Equal(4, usedUpgrades.Count);
            Assert.True(continued, "Journey must execute a causal hop, not merely equip its policy");
            var verify = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
            var verification = await verify.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(verify.IsSuccessStatusCode && verification.GetProperty("isValid").GetBoolean(), verification.GetRawText());
            if (baseline == null) baseline = receipts;
            else Assert.Equal(baseline, receipts);
            if (baselineCommits == null) baselineCommits = commitHashes;
            else Assert.Equal(baselineCommits, commitHashes);
            output.WriteLine($"Repetition {repetition + 1}: {steps} commands, state {final.GetProperty("stateHash").GetString()}");
        }
        var ordered = timings.Order().ToArray();
        output.WriteLine($"In-process HTTP commands/evaluations n={ordered.Length}; p95={ordered[(int)(ordered.Length * .95)]:F2}ms; p99={ordered[(int)(ordered.Length * .99)]:F2}ms; max={ordered[^1]:F2}ms");
        foreach (var (label, values) in new[]
        {
            ("commands/start", commandTimings.Where(item => item.Sequence <= 15).Select(item => item.Milliseconds)),
            ("commands/middle", commandTimings.Where(item => item.Sequence is > 15 and <= 30).Select(item => item.Milliseconds)),
            ("commands/end", commandTimings.Where(item => item.Sequence > 30).Select(item => item.Milliseconds)),
            ("evaluations", evaluationTimings.AsEnumerable())
        })
        {
            var samples = values.Order().ToArray();
            output.WriteLine($"{label} n={samples.Length}; p95={samples[(int)(samples.Length * .95)]:F2}ms; p99={samples[(int)(samples.Length * .99)]:F2}ms; max={samples[^1]:F2}ms");
        }
    }

    private static int Rank(JsonElement candidate, Guid transformedCard, bool needsHop)
    {
        var command = candidate.GetProperty("command");
        if (command.GetProperty("actionType").GetString() != "PLAY_CARD")
            return command.GetProperty("actionType").GetString() == "END_TURN" ? 100 : 101;
        if (transformedCard != Guid.Empty)
        {
            if (needsHop && candidate.GetProperty("steps").EnumerateArray().Any(step =>
                step.GetProperty("continuation").ValueKind == JsonValueKind.Object &&
                step.GetProperty("continuation").GetProperty("toEntityId").ValueKind == JsonValueKind.String)) return -2;
            // Keep a wounded target alive until an overflow hit can be demonstrated.
            // The legal projection supplies these outcomes; the test does not change engine rules or use cheats.
            if (needsHop && candidate.GetProperty("applications").EnumerateArray().Any(application =>
                application.GetProperty("resourceOutcome").ValueKind == JsonValueKind.Object &&
                application.GetProperty("resourceOutcome").GetProperty("causedDefeat").GetBoolean())) return 102;
            if (command.TryGetProperty("cardInstanceId", out var instance) && instance.GetGuid() == transformedCard) return -1;
        }
        var source = candidate.TryGetProperty("cardDefinitionId", out var id) ? id.GetString() : "";
        return source switch { "core_charge_card" => 0, "core_release" => 1, "core_strike" => 2, "core_heal" => 3, "core_recover" => 4, _ => 5 };
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, json.GetRawText());
        return json;
    }
}
