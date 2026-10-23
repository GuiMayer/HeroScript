using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Abstractions.Persistence;
using Core.Content;
using Core.Combat.Models;
using Core.Determinism;
using Core.Meta;
using API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

/// <summary>Short published JSON fixture, real REST gateway and fresh store per repetition.</summary>
public sealed class WorldOneFoundationJourneyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TenIsolatedJourneysAgreeOnResourcesCriticalFactsUnlocksReceiptsAndReplay()
    {
        List<string>? baseline = null;
        var commandTimes = new List<double>(); var previewTimes = new List<double>();
        for (var repetition = 0; repetition < 10; repetition++)
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            var game = new GameEngineClientSimulator(client);
            var revision = await PublishFixture(factory);
            var transcript = new List<string>();
            var nextCommand = 0;
            var store = factory.Services.GetRequiredService<IRunCommitStore>();
            var progress = factory.Services.GetRequiredService<IProfileProgressSnapshotReader>();

            async Task<JsonElement> Start(string setting, string player, ulong seed, string? pinned = null)
            {
                if (pinned == null) pinned = (await Read(client, $"/api/v1/content/revisions?configName={setting}")).GetProperty("currentRevision").GetString();
                var json = await Post(client, "/api/v1/runs", new { settingId = setting,
                    runDefinitionId = setting == "ascendant" ? "ascendant_showcase_run" : "default_run",
                    modeId = setting == "ascendant" ? "ascendant_showcase" : "standard",
                    playerEntityId = player, seed, contentRevision = pinned });
                transcript.Add(CanonicalJson.ComputeHash(json));
                var commit = await store.LoadCommitAsync(json.GetProperty("runId").GetGuid(), 1);
                transcript.Add(CanonicalJson.ComputeHash(commit));
                return json;
            }
            var otherSetting = await Start("default", "foundation-player", 750);
            var otherPlayer = await Start("ascendant", "foundation-other", 751, revision);
            var otherProfileHash = CanonicalJson.ComputeHash(await progress.ReadAsync("foundation-other", "ascendant"));
            var secondaryHash = CanonicalJson.ComputeHash(await Read(client, $"/api/v1/profiles/foundation-player?settingId=default"));
            var original = await Start("ascendant", "foundation-player", 150, revision);
            var runId = original.GetProperty("runId").GetGuid();
            var initialEligibility = original.GetProperty("eligibility").GetRawText();
            Assert.Empty(original.GetProperty("eligibility").GetProperty("availableTargets").EnumerateArray());

            async Task<JsonElement> Command(string type, object payload, Guid? combatId = null, ulong? expectedStep = null)
            {
                var run = await Read(client, $"/api/v1/runs/{runId}");
                var step = expectedStep ?? (combatId == null ? run.GetProperty("step").GetUInt64()
                    : (await Read(client, $"/api/v1/combats/{combatId}")).GetProperty("step").GetUInt64());
                var envelope = new { commandId = Guid.Parse($"81000000-0000-4000-8000-{++nextCommand:000000000000}"),
                    type, payload, expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = step };
                var route = combatId == null ? $"/api/v1/runs/{runId}/commands" : $"/api/v1/combats/{combatId}/commands";
                var timer = Stopwatch.StartNew();
                var receipt = await Post(client, route, envelope);
                commandTimes.Add(timer.Elapsed.TotalMilliseconds);
                transcript.Add(CanonicalJson.ComputeHash(receipt));
                var commit = await store.LoadCommitAsync(runId, receipt.GetProperty("sequence").GetInt32());
                transcript.Add(CanonicalJson.ComputeHash(commit));
                // Duplicate delivery cannot charge/heal/sortear/contribute twice.
                var duplicate = await Post(client, route, envelope);
                Assert.True(duplicate.GetProperty("duplicate").GetBoolean());
                Assert.Equal(ReceiptHash(receipt), ReceiptHash(duplicate));
                return receipt;
            }
            async Task<JsonElement> Available(string type, object? payload = null)
            {
                var commands = (await Read(client, $"/api/v1/runs/{runId}/available-commands")).GetProperty("commands");
                var command = commands.EnumerateArray().Single(item => item.GetProperty("type").GetString() == type);
                return await Command(type, payload ?? command.GetProperty("validPayload"), expectedStep: command.GetProperty("expectedStep").GetUInt64());
            }
            async Task RestartBoundary()
            {
                var before = await Read(client, $"/api/v1/runs/{runId}");
                using var restart = new TestWebApplicationFactory(factory.PersistenceRoot);
                using var connected = restart.CreateClient();
                Assert.Equal(CanonicalJson.ComputeHash(before), CanonicalJson.ComputeHash(await Read(connected, $"/api/v1/runs/{runId}")));
                Assert.Equal(CanonicalJson.ComputeHash(await progress.ReadAsync("foundation-player", "ascendant")),
                    CanonicalJson.ComputeHash(await restart.Services.GetRequiredService<IProfileProgressSnapshotReader>().ReadAsync("foundation-player", "ascendant")));
            }
            await RestartBoundary(); // Frozen capture survives restart.
            await Available("CREATE_CARD_SELECTION");
            var offer = (await Read(client, $"/api/v1/runs/{runId}")).GetProperty("cardSelections")[0];
            Assert.DoesNotContain(offer.GetProperty("options").EnumerateArray(), item => item.GetProperty("cardId").GetString() == "foundation_prize");
            await Available("PICK_CARD_REWARD", new { selectionInstanceId = offer.GetProperty("selectionInstanceId").GetGuid(), cardIds = new[] { "ascendant_strike" } });
            await Available("RESOLVE_NODE");
            await Available("ADVANCE_NODE", new { targetNodeId = "calibration" });
            await Available("START_ENCOUNTER");
            var combatId = (await Read(client, $"/api/v1/runs/{runId}")).GetProperty("activeEncounterId").GetGuid();
            await Command("END_TURN", new { actorId = "foundation-player" }, combatId);
            var wounded = await Read(client, $"/api/v1/runs/{runId}");
            var woundedHealth = Health(wounded);
            Assert.True(woundedHealth < 100, "AI must actually damage the player, not just expose an intent");
            // While the encounter owns resources, its baseline remains untouched.
            Assert.Equal(100, (await store.LoadLatestStateAsync(runId))!.PlayerEntity!.Components.Values.OfType<ResourceEntityComponentState>().Single().State.Current("health"));
            var criticalFacts = 0;
            for (var turn = 0; turn < 8; turn++)
            {
                var combat = await Read(client, $"/api/v1/combats/{combatId}");
                if (combat.GetProperty("status").GetString() != "ACTIVE") break;
                await Command("INVOKE_CARD_ZONE_GAMEPLAY_FLOW", new { flowId = "run.draw", requestedCount = 5 });
                var run = await Read(client, $"/api/v1/runs/{runId}");
                var beforeHash = run.GetProperty("stateHash").GetString();
                var timer = Stopwatch.StartNew();
                var previews = await Read(client, $"/api/v1/combats/{combatId}/cards/evaluations?actorId=foundation-player");
                previewTimes.Add(timer.Elapsed.TotalMilliseconds);
                Assert.Equal(beforeHash, (await Read(client, $"/api/v1/runs/{runId}")).GetProperty("stateHash").GetString());
                Assert.Equal(previews.GetRawText(), (await Read(client, $"/api/v1/combats/{combatId}/cards/evaluations?actorId=foundation-player")).GetRawText());
                var legal = await Read(client, $"/api/v1/combats/{combatId}/legal-actions?actorId=foundation-player");
                var candidates = legal.ValueKind == JsonValueKind.Array ? legal : legal.GetProperty("candidates");
                var criticalCard = await game.GetPlayableCardInstanceIdAsync(runId, "ascendant_critical_lance");
                var plays = candidates.EnumerateArray().Where(item => item.GetProperty("command").GetProperty("actionType").GetString() == "PLAY_CARD" &&
                    item.GetProperty("command").GetProperty("cardInstanceId").GetGuid() == criticalCard).ToArray();
                Assert.True(plays.Length > 0, $"turn={turn}; critical card {criticalCard}, previews={previews.GetRawText()}");
                var play = plays[0];
                var payload = play.GetProperty("command").EnumerateObject()
                    .Where(item => item.Name is "actorId" or "cardInstanceId" or "targetId" or "targetIds" or "costOptionId")
                    .Where(item => item.Value.ValueKind != JsonValueKind.Null).ToDictionary(item => item.Name, item => item.Value);
                var receipt = await Command("PLAY_CARD", payload, combatId);
                var committed = await store.LoadCommitAsync(runId, receipt.GetProperty("sequence").GetInt32());
                var facts = JsonSerializer.SerializeToElement(committed);
                var probabilityFacts = Descendants(facts).Where(item => item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("InputId", out var input) && input.GetString() == "critical" && item.TryGetProperty("Roll", out _) && item.TryGetProperty("ScopeId", out _)).ToArray();
                criticalFacts += probabilityFacts.Length;
                Assert.NotEmpty(probabilityFacts);
                Assert.All(probabilityFacts, item => Assert.Equal(.5, item.GetProperty("Probability").GetDouble()));
                if (turn == 0)
                {
                    Assert.Equal(3, probabilityFacts.Select(item => item.GetProperty("ScopeId").GetString()).Distinct().Count());
                    Assert.Equal(3, probabilityFacts.Select(item => item.GetProperty("Roll").GetDouble()).Distinct().Count());
                }
                await RestartBoundary(); // Critical RNG and impact facts survive restart.
                combat = await Read(client, $"/api/v1/combats/{combatId}");
                if (combat.GetProperty("status").GetString() == "ACTIVE") await Command("END_TURN", new { actorId = "foundation-player" }, combatId);
            }
            Assert.Equal("VICTORY", (await Read(client, $"/api/v1/combats/{combatId}")).GetProperty("status").GetString());
            Assert.True(criticalFacts >= 3, "Must execute multiple independently scoped critical impacts");
            var healthAtVictory = Health(await Read(client, $"/api/v1/runs/{runId}"));
            // Regression: the encounter ID still exists until RESOLVE_COMBAT, but
            // Godot already displays navigation after the last combat animation.
            var beforeNavigationRead = (await store.LoadLatestStateAsync(runId))!;
            var navigationHash = CanonicalJson.ComputeHash(beforeNavigationRead);
            var victorySession = await Read(client, $"/api/v1/runs/{runId}/session");
            var completeMap = await Read(client, $"/api/v1/runs/{runId}/map");
            var navigationMap = victorySession.GetProperty("run").GetProperty("map");
            Assert.Equal("VICTORY", victorySession.GetProperty("combat").GetProperty("status").GetString());
            Assert.Equal(combatId, victorySession.GetProperty("run").GetProperty("activeEncounterId").GetGuid());
            Assert.Equal(completeMap.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("nodeId").GetString()),
                navigationMap.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("nodeId").GetString()));
            Assert.Equal(completeMap.GetProperty("legalNextNodeIds").GetRawText(), navigationMap.GetProperty("legalNextNodeIds").GetRawText());
            Assert.Contains(victorySession.GetProperty("commands").GetProperty("commands").EnumerateArray(),
                command => command.GetProperty("type").GetString() == "RESOLVE_COMBAT");
            Assert.Equal(navigationHash, CanonicalJson.ComputeHash((await store.LoadLatestStateAsync(runId))!));
            await Available("RESOLVE_COMBAT");
            Assert.Equal(healthAtVictory, Health(await Read(client, $"/api/v1/runs/{runId}")));
            await RestartBoundary();
            await Available("ADVANCE_NODE", new { targetNodeId = "rest" });
            await Available("CREATE_PREPARATION");
            var beforeRest = await Read(client, $"/api/v1/runs/{runId}");
            var gold = beforeRest.GetProperty("resources").GetProperty("gold").GetProperty("current").GetDouble();
            var prep = beforeRest.GetProperty("preparations")[0];
            await Available("APPLY_PREPARATION_OPTION", new { preparationInstanceId = prep.GetProperty("preparationInstanceId").GetGuid(), optionId = "recover" });
            var afterRest = await Read(client, $"/api/v1/runs/{runId}");
            Assert.Equal(Math.Min(100, healthAtVictory + 15), Health(afterRest));
            Assert.Equal(gold - 10, afterRest.GetProperty("resources").GetProperty("gold").GetProperty("current").GetDouble());
            await RestartBoundary();
            await Available("RESOLVE_NODE");
            var unlocked = await progress.ReadAsync("foundation-player", "ascendant");
            Assert.Equal("recovery-option", Assert.Single(unlocked.Grants).Key);
            await Available("ADVANCE_NODE", new { targetNodeId = "followup" });
            await Available("START_ENCOUNTER");
            Assert.Equal(Health(afterRest), Health(await Read(client, $"/api/v1/runs/{runId}")));
            var final = await Read(client, $"/api/v1/runs/{runId}");
            Assert.Equal(initialEligibility, final.GetProperty("eligibility").GetRawText());
            var newRun = await Start("ascendant", "foundation-player", 152, revision);
            Assert.Single(newRun.GetProperty("eligibility").GetProperty("availableTargets").EnumerateArray());
            var freshId = newRun.GetProperty("runId").GetGuid();
            var freshCommands = (await Read(client, $"/api/v1/runs/{freshId}/available-commands")).GetProperty("commands");
            var createOffer = freshCommands.EnumerateArray().Single(item => item.GetProperty("type").GetString() == "CREATE_CARD_SELECTION");
            var freshReceipt = await Post(client, $"/api/v1/runs/{freshId}/commands", new { commandId = Guid.Parse("82000000-0000-4000-8000-000000000001"), type = "CREATE_CARD_SELECTION",
                payload = createOffer.GetProperty("validPayload"), expectedSequence = newRun.GetProperty("sequence").GetInt32(), expectedStep = newRun.GetProperty("step").GetUInt64() });
            transcript.Add(CanonicalJson.ComputeHash(freshReceipt));
            transcript.Add(CanonicalJson.ComputeHash(await store.LoadCommitAsync(freshId, freshReceipt.GetProperty("sequence").GetInt32())));
            var freshState = await Read(client, $"/api/v1/runs/{freshId}");
            Assert.Contains(freshState.GetProperty("cardSelections")[0].GetProperty("options").EnumerateArray(), item => item.GetProperty("cardId").GetString() == "foundation_prize");
            Assert.Equal(initialEligibility, (await Read(client, $"/api/v1/runs/{runId}")).GetProperty("eligibility").GetRawText());
            var progressBeforeReplay = CanonicalJson.ComputeHash(await progress.ReadAsync("foundation-player", "ascendant"));
            using var verificationResponse = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
            var verification = await verificationResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(verificationResponse.IsSuccessStatusCode && verification.GetProperty("isValid").GetBoolean(), verification.GetRawText());
            Assert.Equal(progressBeforeReplay, CanonicalJson.ComputeHash(await progress.ReadAsync("foundation-player", "ascendant")));
            Assert.Equal(otherProfileHash, CanonicalJson.ComputeHash(await progress.ReadAsync("foundation-other", "ascendant")));
            Assert.Equal(secondaryHash, CanonicalJson.ComputeHash(await Read(client, "/api/v1/profiles/foundation-player?settingId=default")));
            Assert.Equal(otherSetting.GetProperty("stateHash").GetString(), (await Read(client, $"/api/v1/runs/{otherSetting.GetProperty("runId").GetGuid()}")).GetProperty("stateHash").GetString());
            Assert.Equal(otherPlayer.GetProperty("stateHash").GetString(), (await Read(client, $"/api/v1/runs/{otherPlayer.GetProperty("runId").GetGuid()}")).GetProperty("stateHash").GetString());
            transcript.Add(CanonicalJson.ComputeHash(unlocked));
            if (baseline == null) baseline = transcript; else Assert.Equal(baseline, transcript);
            output.WriteLine($"repetition={repetition + 1}; commands={nextCommand}; transcript={CanonicalJson.ComputeHash(transcript)}; final={final.GetProperty("stateHash").GetString()}");
        }
        output.WriteLine($"commands n={commandTimes.Count} p95={Percentile(commandTimes, .95):F2}ms max={commandTimes.Max():F2}ms; previews n={previewTimes.Count} p95={Percentile(previewTimes, .95):F2}ms max={previewTimes.Max():F2}ms");
    }

    private static double Health(JsonElement run) => run.GetProperty("characterResources").GetProperty("resources").GetProperty("health").GetProperty("current").GetDouble();
    private static string ReceiptHash(JsonElement receipt) => CanonicalJson.ComputeHash(receipt.EnumerateObject()
        .Where(item => item.Name != "duplicate").ToDictionary(item => item.Name, item => item.Value));
    private static double Percentile(List<double> samples, double percentile) => samples.Order().ElementAt((int)Math.Ceiling(samples.Count * percentile) - 1);
    private static IEnumerable<JsonElement> Descendants(JsonElement element)
    {
        yield return element;
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) foreach (var child in Descendants(property.Value)) yield return child;
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) foreach (var child in Descendants(item)) yield return child;
    }
    private static async Task<JsonElement> Read(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, $"{route}: {json}"); return json;
    }
    private static async Task<JsonElement> Post(HttpClient client, string route, object payload)
    {
        using var response = await client.PostAsJsonAsync(route, payload);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, $"{route}: {json}"); return json;
    }

    private static async Task<string> PublishFixture(TestWebApplicationFactory factory)
    {
        var manifests = factory.Services.GetRequiredService<IContentManifestProvider>();
        var publications = factory.Services.GetRequiredService<IContentPublicationService>();
        var original = await publications.ResolveBundleAsync(manifests.GetManifest("ascendant").Value.Revision, "ascendant");
        Assert.True(original.IsSuccess, original.IsFailure ? original.Error : null);
        var artifacts = original.Value.Artifacts.ToBuilder();
        JsonObject Load(string path) => JsonNode.Parse(artifacts[path].GetRawText())!.AsObject();
        void Save(string path, JsonObject node) => artifacts[path] = JsonSerializer.SerializeToElement(node);
        const string cardsPath = "cards/ascendant_cards.json";
        var cards = Load(cardsPath);
        var criticalEffect = cards["ascendant_critical_lance"]!["components"]!.AsArray().Single(item => item!["type"]!.GetValue<string>() == "effect")!["effect"]!;
        criticalEffect["repeat"] = 3;
        criticalEffect["targetLoss"] = JsonNode.Parse("""{"policy":"StopRepeat"}""");
        cards["foundation_prize"] = cards["ascendant_strike"]!.DeepClone(); cards["foundation_prize"]!["cardId"] = "foundation_prize";
        Save(cardsPath, cards);
        var entities = Load("entities/ascendant_entities.json");
        var health = entities["ascendant_drone"]!["components"]!.AsArray().Single(item => item!["type"]!.GetValue<string>() == "resources")!["pools"]!["health"]!;
        health["current"] = 110; health["max"] = 110;
        Save("entities/ascendant_entities.json", entities);
        var runs = Load("runs/ascendant_showcase.json");
        var run = runs["ascendant_showcase_run"]!;
        run["startingCards"] = new JsonArray(Enumerable.Range(0, 8).Select(_ => (JsonNode?)JsonValue.Create("ascendant_critical_lance")).ToArray());
        var calibration = run["mapNodes"]!.AsArray()[0]!.DeepClone(); calibration["nextNodeIds"] = JsonNode.Parse("[\"rest\"]");
        var followup = calibration.DeepClone(); followup["nodeId"] = "followup"; followup["nextNodeIds"] = new JsonArray(); followup["exitEffects"] = new JsonArray();
        run["mapNodes"] = new JsonArray(JsonNode.Parse("""{"nodeId":"offer","activity":{"type":"CardSelection","definitionId":"foundation_offer"},"completionPolicy":"Required","nextNodeIds":["calibration"]}"""),
            calibration, JsonNode.Parse("""{"nodeId":"rest","activity":{"type":"Preparation","definitionId":"foundation_rest"},"completionPolicy":"Required","nextNodeIds":["followup"]}"""), followup);
        Save("runs/ascendant_showcase.json", runs);
        artifacts["preparations/foundation_rest.json"] = JsonSerializer.SerializeToElement(JsonNode.Parse("""
            {"foundation_rest":{"preparationId":"foundation_rest","options":[{"optionId":"recover","costs":[{"resourceId":"gold","amount":10}],"effectOwner":"PlayerEntity","effects":[{"effectId":"foundation.recovery","type":"HEAL","target":"SELF","targetResource":"health","flatValue":15}]}]}}
            """));
        artifacts["card-selections/foundation_offer.json"] = JsonSerializer.SerializeToElement(JsonNode.Parse("""
            {"foundation_offer":{"selectionId":"foundation_offer","pickCount":1,"offerCount":2,"cardPool":["ascendant_strike","foundation_prize"]}}
            """));
        Save("profile-progress-policies/ascendant_options.json", JsonNode.Parse("""
            {"ascendant_options":{"profileProgressPolicyId":"ascendant_options","contributingModeIds":["ascendant_showcase"],"allowedProvenances":["Published"],"deduplication":"RootLineage","unlocks":[{"unlockId":"recovery-option","targets":[{"kind":"Card","definitionId":"foundation_prize"}],"condition":{"kind":"NodeCompleted","runDefinitionId":"ascendant_showcase_run","nodeId":"rest"}}]}}
            """)!.AsObject());
        var metadata = original.Value.Manifest.Artifacts.ToDictionary(item => item.Path, StringComparer.Ordinal);
        var artifactManifests = artifacts.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new ContentArtifactManifest {
            Path = item.Key, Kind = metadata.TryGetValue(item.Key, out var prior) ? prior.Kind : item.Key.Split('/')[0],
            Hash = CanonicalJson.ComputeHash(item.Value.Deserialize<Dictionary<string, JsonElement>>()!), DefinitionCount = item.Value.EnumerateObject().Count() }).ToArray();
        var manifest = original.Value.Manifest with { Artifacts = artifactManifests };
        manifest = manifest with { Revision = CanonicalJson.ComputeHash(new { manifest.SchemaVersion, manifest.ConfigName, manifest.ConfigChain, manifest.Artifacts }) };
        var published = await publications.PublishBundleAsync(new ContentBundle { Manifest = manifest, Artifacts = artifacts.ToImmutable() });
        Assert.True(published.IsSuccess, published.IsFailure ? published.Error : null);
        return manifest.Revision;
    }
}
