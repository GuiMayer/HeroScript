using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Config;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class TagSynergyReplayCertificationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task All24Cards_ThreeIndependentHostsHaveIdenticalHashes_ReadOnlyPreviewAndValidReplay()
    {
        var root = ResourceProviderFactory.FindProjectRoot()!;
        var cardIds = Directory.GetFiles(Path.Combine(root, "data/configs/ascendant/Resources/cards"), "tag_synergy_*.json")
            .SelectMany(path => JsonNode.Parse(File.ReadAllText(path))!.AsObject().Select(p => p.Key))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(24, cardIds.Length);
        var baseline = new Dictionary<string, string>();
        for (var execution = 0; execution < 3; execution++) {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            var revision = (await client.GetFromJsonAsync<JsonElement>("/api/v1/content/revisions?configName=ascendant"))
                .GetProperty("currentRevision").GetString();
            foreach (var id in cardIds) {
                var response = await client.PostAsJsonAsync("/api/v1/sandbox/runs?configName=ascendant", new {
                    schemaVersion = 2, modeId = "w1_synergy_sandbox", contentRevision = revision,
                    seed = 123UL, attemptKey = "certify-" + id,
                    participants = new object[] {
                        new { instanceId = "player", entityDefinitionId = "w1_operator", sideId = "player", controllerBinding = new { kind = "Player" } },
                        new { instanceId = "sentinel", entityDefinitionId = "w1_sentinel", sideId = "opposition", controllerBinding = new { kind = "AI", policyId = "gambit" } } },
                    startingCards = Enumerable.Repeat(new { definitionId = id }, 5).ToArray(),
                    initialState = new { resourcesByActor = new { player = new { health = 50, energy = 3, block = 20 } } }
                });
                var launch = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(response.IsSuccessStatusCode, $"{id}: {launch}");
                var runId = launch.GetProperty("run").GetProperty("runId").GetGuid();
                var combatId = launch.GetProperty("combat").GetProperty("combatId").GetGuid();
                var snapshot = await client.GetFromJsonAsync<JsonElement>($"/api/v1/sandbox/runs/{runId}/snapshot");
                var legal = await client.GetFromJsonAsync<JsonElement>($"/api/v1/combats/{combatId}/legal-actions?actorId=player");
                var candidate = legal.GetProperty("candidates").EnumerateArray().First(c =>
                    c.GetProperty("command").GetProperty("actionType").GetString() == "PLAY_CARD").GetProperty("command");
                var card = candidate.GetProperty("cardInstanceId").GetGuid();
                var targets = candidate.GetProperty("targetIds").EnumerateArray().Select(x => x.GetString()!).ToArray();
                var query = string.Concat(targets.Select(t => "&targetIds=" + Uri.EscapeDataString(t)));
                using var preview = await client.GetAsync($"/api/v1/combats/{combatId}/cards/{card}/evaluation?actorId=player{query}");
                Assert.True(preview.IsSuccessStatusCode, $"{id}: {await preview.Content.ReadAsStringAsync()}");
                var inspected = await client.GetFromJsonAsync<JsonElement>($"/api/v1/sandbox/runs/{runId}/snapshot");
                Assert.Equal(snapshot.GetRawText(), inspected.GetRawText());
                using var action = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new {
                    commandId = Guid.Parse("56000000-0000-4000-8000-000000000001"),
                    expectedSequence = snapshot.GetProperty("run").GetProperty("sequence").GetInt32(),
                    expectedStep = snapshot.GetProperty("combat").GetProperty("step").GetUInt64(),
                    type = "PLAY_CARD", payload = new { actorId = "player", cardInstanceId = card, targetIds = targets } });
                var receipt = await action.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(action.IsSuccessStatusCode, $"{id}: {receipt}");
                var hash = receipt.GetProperty("stateHash").GetString()!;
                if (execution == 0) baseline[id] = hash;
                else Assert.Equal(baseline[id], hash);
                using var verify = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
                var replay = await verify.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(verify.IsSuccessStatusCode && replay.GetProperty("isValid").GetBoolean() &&
                    replay.GetProperty("reexecuted").GetBoolean(), $"{id}: {replay}");
                output.WriteLine($"execution={execution + 1}; {id}; hash={hash}; replay=valid");
            }
        }
    }
}
