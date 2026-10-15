using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Hosting;

namespace API.Tests.Integration;

// Published content, canonical REST commands, no custom resources, free draw or tools.
// This deterministic preview-greedy policy is a regression probe, not a human balance gate.
public sealed class AscendantWorldOneJourneyTests(ITestOutputHelper output)
{
    private sealed class TimedFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        { base.ConfigureWebHost(builder); builder.UseSetting("Performance:DiagnosticsEnabled", "true"); }
    }
    [Fact]
    public Task PublishedAct_CanTraverseAllNineEncountersServicesAndReplay() => Traverse(false);

    [Fact]
    public Task CompactSession_CanTraverseTheSamePublishedActAndReplay() => Traverse(true);

    private async Task Traverse(bool compact)
    {
        using var factory = compact ? new TimedFactory() : new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        const string player = "world-one-pilot";
        var runId = await game.StartRunAsync("ascendant", "ascendant_world_one_run", player, 154,
            "ascendant_world_one");
        var times = new List<double>();
        var sessionTimes = new List<double>();
        var sessionBytes = new List<int>();
        var phases = new List<(double Ms, string Type, int Bytes, string Timing)>();
        var sessionPhases = new List<(double Ms, string Timing)>();
        if (compact) client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        var fights = 0;
        var commandIndex = 0;
        var lastEncounterHealth = 100f;
        var sawDecline = false;
        var sawUpgrade = false;
        var sawRecovery = false;

        async Task<JsonElement> Read(string path) => await client.GetFromJsonAsync<JsonElement>(path);
        async Task Command(string type, object payload, JsonElement context, Guid? combatId = null,
            ulong? step = null)
        {
            var timer = Stopwatch.StartNew();
            var route = combatId == null ? $"/api/v1/runs/{runId}/commands" : $"/api/v1/combats/{combatId}/commands";
            using var response = await client.PostAsJsonAsync(route, new
            {
                commandId = Guid.Parse($"83000000-0000-4000-8000-{++commandIndex:000000000000}"), type, payload,
                expectedSequence = context.GetProperty("sequence").GetInt32(),
                expectedStep = step ?? context.GetProperty("step").GetUInt64()
            });
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{type}: {body}");
            times.Add(timer.Elapsed.TotalMilliseconds);
            phases.Add((timer.Elapsed.TotalMilliseconds, type, System.Text.Encoding.UTF8.GetByteCount(body),
                response.Headers.TryGetValues("Server-Timing", out var timing) ? string.Join("; ", timing) : "disabled"));
        }

        for (var iteration = 0; iteration < 700; iteration++)
        {
            JsonElement session = default;
            JsonElement run;
            if (compact)
            {
                var watch = Stopwatch.StartNew();
                using var result = await client.GetAsync($"/api/v1/runs/{runId}/session");
                var bytes = await result.Content.ReadAsByteArrayAsync();
                Assert.True(result.IsSuccessStatusCode, System.Text.Encoding.UTF8.GetString(bytes));
                using var document = JsonDocument.Parse(bytes);
                session = document.RootElement.Clone(); run = session.GetProperty("run");
                sessionTimes.Add(watch.Elapsed.TotalMilliseconds); sessionBytes.Add(bytes.Length);
                sessionPhases.Add((watch.Elapsed.TotalMilliseconds,
                    result.Headers.TryGetValues("Server-Timing", out var timing) ? string.Join("; ", timing) : "disabled"));
            }
            else run = await game.GetRunStateAsync(runId);
            var lifecycle = run.GetProperty("lifecycle").GetString();
            if (lifecycle != "Active")
            {
                output.WriteLine($"terminal={lifecycle}; encounters={fights}; commands={commandIndex}; health={Health(run)}");
                Assert.Equal("Completed", lifecycle);
                break;
            }
            if (run.GetProperty("activeEncounterId").ValueKind != JsonValueKind.Null)
            {
                var combatId = run.GetProperty("activeEncounterId").GetGuid();
                var combat = compact ? session.GetProperty("combat") : await game.GetCombatStateAsync(combatId);
                if (combat.GetProperty("status").GetString() == "ACTIVE")
                {
                    var legal = compact ? session.GetProperty("actions") : await Read($"/api/v1/combats/{combatId}/legal-actions?actorId={player}");
                    var candidates = legal.ValueKind == JsonValueKind.Array ? legal : legal.GetProperty("candidates");
                    var plays = candidates.EnumerateArray().Where(candidate =>
                        candidate.GetProperty("command").GetProperty("actionType").GetString() == "PLAY_CARD").ToArray();
                    if (plays.Length > 0)
                    {
                        var actor = combat.GetProperty("actors").EnumerateArray().Single(a => a.GetProperty("instanceId").GetString() == player);
                        var block = actor.GetProperty("resources").GetProperty("block").GetProperty("current").GetSingle();
                        var threat = combat.GetProperty("activation").GetProperty("intents").EnumerateArray()
                            .SelectMany(intent => intent.GetProperty("previewApplications").EnumerateArray())
                            .Where(a => a.GetProperty("targetEntityId").GetString() == player && a.TryGetProperty("resourceId", out var r) && r.GetString() == "health")
                            .Sum(a => Math.Max(0, Number(a, "previousValue") - Number(a, "currentValue")));
                        var play = plays.OrderByDescending(candidate => Score(candidate, player, Math.Max(0, threat - block)))
                            .ThenBy(candidate => candidate.GetProperty("candidateId").GetString(), StringComparer.Ordinal).First();
                        var payload = play.GetProperty("command").EnumerateObject()
                            .Where(p => p.Name is "actorId" or "cardInstanceId" or "targetId" or "targetIds" or "costOptionId")
                            .Where(p => p.Value.ValueKind != JsonValueKind.Null).ToDictionary(p => p.Name, p => p.Value);
                        await Command("PLAY_CARD", payload, run, combatId, combat.GetProperty("step").GetUInt64());
                    }
                    else await Command("END_TURN", new { actorId = player }, run, combatId, combat.GetProperty("step").GetUInt64());
                    continue;
                }
                fights++;
                lastEncounterHealth = Health(run);
                output.WriteLine($"fight={fights}; node={run.GetProperty("currentNodeId").GetString()}; outcome={combat.GetProperty("status").GetString()}; health={lastEncounterHealth}; round={combat.GetProperty("currentTurn")}");
            }

            var commands = (compact ? session.GetProperty("commands") : await Read($"/api/v1/runs/{runId}/available-commands")).GetProperty("commands").EnumerateArray().ToArray();
            Assert.NotEmpty(commands);
            bool Has(string type) => commands.Any(c => c.GetProperty("type").GetString() == type);
            var type = Has("RESOLVE_COMBAT") ? "RESOLVE_COMBAT" : Has("START_ENCOUNTER") ? "START_ENCOUNTER" :
                Has("CREATE_CARD_SELECTION") ? "CREATE_CARD_SELECTION" : Has("PICK_CARD_REWARD") ? "PICK_CARD_REWARD" :
                Has("UPGRADE_CARD") ? "UPGRADE_CARD" : Has("CREATE_PREPARATION") ? "CREATE_PREPARATION" :
                Has("APPLY_PREPARATION_OPTION") ? "APPLY_PREPARATION_OPTION" : Has("ACQUIRE_RELIC") ? "ACQUIRE_RELIC" :
                Has("RESOLVE_NODE") ? "RESOLVE_NODE" : Has("ADVANCE_NODE") ? "ADVANCE_NODE" : "";
            Assert.NotEqual("", type);
            var command = commands.First(c => c.GetProperty("type").GetString() == type);
            object input = command.GetProperty("validPayload");
            if (type == "START_ENCOUNTER") Assert.Equal(lastEncounterHealth, Health(run));
            if (type == "PICK_CARD_REWARD")
            {
                // Exercise decline after the offers are open, without assuming a special UI command.
                if (!sawDecline)
                {
                    Assert.True(Has("RESOLVE_NODE"));
                    command = commands.First(c => c.GetProperty("type").GetString() == "RESOLVE_NODE");
                    type = "RESOLVE_NODE"; input = command.GetProperty("validPayload"); sawDecline = true;
                }
                else
                {
                    var valid = command.GetProperty("validPayload");
                    var id = valid.GetProperty("cardIds").EnumerateArray().Select(c => c.GetString()!)
                        .OrderByDescending(RewardScore).ThenBy(c => c, StringComparer.Ordinal).First();
                    input = new { selectionInstanceId = valid.GetProperty("selectionInstanceId").GetGuid(), cardIds = new[] { id } };
                }
            }
            if (type == "UPGRADE_CARD")
            {
                var option = command.GetProperty("validPayload").GetProperty("options").EnumerateArray()
                    .OrderByDescending(o => o.GetProperty("cardDefinitionId").GetString() == "w1_flurry" ? 100 : 0)
                    .ThenByDescending(o => o.GetProperty("upgradeId").GetString() == "w1_ranked_edge" ? 20 : o.GetProperty("upgradeId").GetString() == "w1_amplify" ? 10 : 0).First();
                input = new { cardInstanceId = option.GetProperty("cardInstanceId").GetGuid(), upgradeId = option.GetProperty("upgradeId").GetString() };
                sawUpgrade = true;
            }
            if (type == "APPLY_PREPARATION_OPTION")
            {
                var valid = command.GetProperty("validPayload");
                input = new { preparationInstanceId = valid.GetProperty("preparationInstanceId").GetGuid(), optionId = "recover" };
                sawRecovery = true;
            }
            if (type == "ADVANCE_NODE")
            {
                var ids = command.GetProperty("targetNodeIds").EnumerateArray().Select(c => c.GetString()!).ToArray();
                var route = ids.OrderByDescending(id => id is "relic_keen" or "s04_charge" or "s05_corrosion" or "s06_rest" or "s07_guarded" or "s09_patrol" or "s11_swarm" or "s12_rest" or "relic_field" ? 1 : 0).ThenBy(id => id, StringComparer.Ordinal).First();
                input = new { targetNodeId = route };
            }
            var beforeHealth = Health(run);
            await Command(type, input, run, step: command.GetProperty("expectedStep").GetUInt64());
            if (type == "RESOLVE_COMBAT") Assert.Equal(beforeHealth, Health(await game.GetRunStateAsync(runId)));
            if (type == "APPLY_PREPARATION_OPTION")
            {
                var healed = Health(await game.GetRunStateAsync(runId));
                Assert.Equal(Math.Min(100, beforeHealth + 25), healed); lastEncounterHealth = healed;
            }
        }
        Assert.Equal("Completed", (await game.GetRunStateAsync(runId)).GetProperty("lifecycle").GetString());
        Assert.Equal(9, fights);
        Assert.True(sawDecline && sawUpgrade && sawRecovery);
        using var verify = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var verification = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verify.IsSuccessStatusCode && verification.GetProperty("isValid").GetBoolean(), verification.GetRawText());
        using var restart = new TestWebApplicationFactory(factory.PersistenceRoot);
        using var restored = restart.CreateClient();
        Assert.Equal(Core.Determinism.CanonicalJson.ComputeHash(await game.GetRunStateAsync(runId)),
            Core.Determinism.CanonicalJson.ComputeHash(await restored.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}")));
        output.WriteLine($"commands n={times.Count}; p95={times.Order().ElementAt((int)Math.Ceiling(times.Count * .95) - 1):F2}ms; max={times.Max():F2}ms");
        if (compact) output.WriteLine($"compact sessions n={sessionTimes.Count}; p50={sessionTimes.Order().ElementAt(sessionTimes.Count / 2):F2}ms; p95={sessionTimes.Order().ElementAt((int)Math.Ceiling(sessionTimes.Count * .95) - 1):F2}ms; max={sessionTimes.Max():F2}ms; bytes p95={sessionBytes.Order().ElementAt((int)Math.Ceiling(sessionBytes.Count * .95) - 1)}");
        if (compact) foreach (var sample in phases.OrderByDescending(value => value.Ms).Take(10))
            output.WriteLine($"slow command {sample.Type}: {sample.Ms:F2}ms {sample.Bytes}bytes {sample.Timing}");
        if (compact) foreach (var group in phases.GroupBy(value => value.Type))
            output.WriteLine($"command {group.Key}: n={group.Count()}; p50={group.OrderBy(value => value.Ms).ElementAt(group.Count() / 2).Ms:F2}ms; p95={group.OrderBy(value => value.Ms).ElementAt((int)Math.Ceiling(group.Count() * .95) - 1).Ms:F2}ms; max={group.Max(value => value.Ms):F2}ms");
        if (compact) foreach (var sample in sessionPhases.OrderByDescending(value => value.Ms).Take(5))
            output.WriteLine($"slow session: {sample.Ms:F2}ms {sample.Timing}");
    }

    private static double Score(JsonElement candidate, string player, double missingBlock)
    {
        var id = candidate.GetProperty("cardDefinitionId").GetString();
        var score = id is "w1_focus" or "w1_momentum" ? 25d : id == "w1_fortify" ? 14 : id == "w1_poison" ? 9 : id == "w1_scorch" ? 4 : 0;
        foreach (var app in candidate.GetProperty("applications").EnumerateArray())
        {
            if (!app.TryGetProperty("resourceId", out var resource) || resource.ValueKind != JsonValueKind.String) continue;
            var delta = Number(app, "currentValue") - Number(app, "previousValue");
            if (resource.GetString() == "health") score += app.GetProperty("targetEntityId").GetString() == player ? Math.Max(0, delta) * 2 : Math.Max(0, -delta);
            if (resource.GetString() == "block" && app.GetProperty("targetEntityId").GetString() == player) score += Math.Min(missingBlock, Math.Max(0, delta)) * 1.5;
        }
        return score;
    }
    private static int RewardScore(string id) => id switch
    {
        "w1_focus" => 100, "w1_flurry" => 95, "w1_lance" => 90, "w1_bulwark" => 85,
        "w1_mend" => 80, "w1_barrage" => 75, "w1_momentum" => 70, "w1_precision" => 60,
        "w1_scorch" => 55, "w1_shield_bash" => 50, _ => 0
    };
    private static float Health(JsonElement run) => run.GetProperty("characterResources").GetProperty("resources").GetProperty("health").GetProperty("current").GetSingle();
    private static double Number(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
}
