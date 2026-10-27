using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class MultiActorPoolJourneyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JsonPoolSupportsBothActorsBotRetryVisibilityPersistenceAndSemanticReplay(bool publicHand)
    {
        List<string>? baseline = null;
        for (var repetition = 0; repetition < 2; repetition++)
        {
            using var factory = new PoolFactory(publicHand);
            using var client = factory.CreateClient();
            var revision = (await Read(client, "/api/v1/content/revisions?configName=default")).GetProperty("currentRevision").GetString();
            var scenario = new {
                schemaVersion = 2, modeId = "combat_sandbox", contentRevision = revision, seed = 784UL, attemptKey = "participant-pool",
                participants = new object[] {
                    new { instanceId = "hero", entityDefinitionId = "player_warrior", sideId = "player", controllerBinding = new { kind = "Player" } },
                    new { instanceId = "dealer", entityDefinitionId = "enemy_goblin", sideId = "opposition", controllerBinding = new { kind = "AI", policyId = "gambit" } }
                }, startingCards = Enumerable.Range(0, 10).Select(_ => new { definitionId = "basic_attack" }).ToArray()
            };
            var launch = await Post(client, "/api/v1/sandbox/runs", scenario);
            var id = launch.GetProperty("run").GetProperty("runId").GetGuid();
            var combatId = launch.GetProperty("combat").GetProperty("combatId").GetGuid();
            var zones = await Read(client, $"/api/v1/runs/{id}/card-zones");
            Assert.Equal(2, Zone(zones, "hand", "hero").GetProperty("count").GetInt32());
            Assert.Equal(0, Zone(zones, "hand", "dealer").GetProperty("count").GetInt32());
            Assert.Equal(8, Zone(zones, "draw", "$run").GetProperty("count").GetInt32());
            // The same launch is a retry; it must not deal again.
            var duplicateLaunch = await Post(client, "/api/v1/sandbox/runs", scenario);
            Assert.True(duplicateLaunch.GetProperty("duplicate").GetBoolean());
            Assert.Equal(zones.GetRawText(), (await Read(client, $"/api/v1/runs/{id}/card-zones")).GetRawText());

            var run = await Read(client, $"/api/v1/runs/{id}");
            var combat = await Read(client, $"/api/v1/combats/{combatId}");
            var end = new { commandId = Guid.Parse("83000000-0000-4000-8000-000000000001"), type = "END_TURN",
                expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = combat.GetProperty("step").GetUInt64(), payload = new { actorId = "hero" } };
            var receipt = await Post(client, $"/api/v1/combats/{combatId}/commands", end);
            var duplicate = await Post(client, $"/api/v1/combats/{combatId}/commands", end);
            Assert.True(duplicate.GetProperty("duplicate").GetBoolean());
            Assert.Equal(receipt.GetProperty("stateHash").GetString(), duplicate.GetProperty("stateHash").GetString());
            zones = await Read(client, $"/api/v1/runs/{id}/card-zones");
            var dealer = Zone(zones, "hand", "dealer");
            Assert.Equal(1, dealer.GetProperty("count").GetInt32());
            Assert.Equal(publicHand, dealer.GetProperty("contentsVisible").GetBoolean());
            Assert.Equal(publicHand ? 1 : 0, dealer.GetProperty("cards").GetArrayLength());
            Assert.False(dealer.GetProperty("playerCanPlayFrom").GetBoolean());
            Assert.True(Zone(zones, "hand", "hero").GetProperty("playerCanPlayFrom").GetBoolean());

            var store = factory.Services.GetRequiredService<IRunCommitStore>();
            var state = (await store.LoadLatestStateAsync(id))!;
            var originalHash = CanonicalJson.ComputeHash(state);
            var enemyCard = Assert.Single(state.Deck.Topology.GetZone("hand", "dealer")!.InstanceIds);
            var playedCard = Assert.Single(state.Deck.Topology.GetZone("discard", "$run")!.InstanceIds);
            Assert.NotEqual(enemyCard, playedCard);
            Assert.Equal(10, state.Deck.Topology.Zones.Values.SelectMany(zone => zone.InstanceIds).Distinct().Count());
            combat = await Read(client, $"/api/v1/combats/{combatId}");
            using var denied = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new {
                commandId = Guid.Parse("83000000-0000-4000-8000-000000000002"), type = "PLAY_CARD", expectedSequence = state.Sequence,
                expectedStep = combat.GetProperty("step").GetUInt64(), payload = new { actorId = "hero", cardInstanceId = enemyCard, targetIds = new[] { "dealer" } } });
            Assert.False(denied.IsSuccessStatusCode);
            using var controlDenied = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new {
                commandId = Guid.Parse("83000000-0000-4000-8000-000000000003"), type = "PLAY_CARD", expectedSequence = state.Sequence,
                expectedStep = combat.GetProperty("step").GetUInt64(), payload = new { actorId = "dealer", cardInstanceId = enemyCard, targetIds = new[] { "hero" } } });
            Assert.False(controlDenied.IsSuccessStatusCode);
            Assert.Equal(originalHash, CanonicalJson.ComputeHash((await store.LoadLatestStateAsync(id))!));
            var verification = await Post(client, $"/api/v1/runs/{id}/verify", null);
            Assert.True(verification.GetProperty("isValid").GetBoolean(), verification.GetRawText());
            Assert.True(verification.GetProperty("reexecuted").GetBoolean());
            var commits = await store.LoadCommitsAsync(id);
            Assert.Contains(commits.SelectMany(commit => commit.Frames), frame =>
                frame.ActorId == "dealer" && frame.CardZoneSteps.Any(step => step.FlowId == "card.played.to-discard" && step.InstanceIds.Contains(playedCard)));
            var hashes = commits.Select(commit => CanonicalJson.ComputeHash(commit)).ToList();
            baseline ??= hashes; Assert.Equal(baseline, hashes);
            using var restarted = new PoolFactory(publicHand, factory.PersistenceRoot, factory.PackageRoot);
            using var reconnected = restarted.CreateClient();
            Assert.Equal(zones.GetRawText(), (await Read(reconnected, $"/api/v1/runs/{id}/card-zones")).GetRawText());
        }
    }

    private static JsonElement Zone(JsonElement snapshot, string zoneId, string owner) => snapshot.GetProperty("zones").EnumerateArray()
        .Single(zone => zone.GetProperty("zoneId").GetString() == zoneId && zone.GetProperty("ownerId").GetString() == owner);
    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path); var value = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, value.GetRawText()); return value;
    }
    private static async Task<JsonElement> Post(HttpClient client, string path, object? body)
    {
        using var response = await client.PostAsJsonAsync(path, body); var value = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, value.GetRawText()); return value;
    }

    // A real published package, modified only inside a temporary test directory.
    // All rules below are JSON; production content and user saves remain untouched.
    private sealed class PoolFactory : TestWebApplicationFactory
    {
        public string PackageRoot { get; }
        public PoolFactory(bool publicHand) { PackageRoot = Path.Combine(PersistenceRoot, "packages"); Prepare(publicHand); }
        public PoolFactory(bool publicHand, string persistenceRoot, string packageRoot) : base(persistenceRoot) => PackageRoot = packageRoot;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder); builder.UseSetting("Mods:PackageRoots:0", PackageRoot);
        }
        private void Prepare(bool publicHand)
        {
            var source = Path.Combine(AppContext.BaseDirectory, "Resources", "default");
            var target = Path.Combine(PackageRoot, "default");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
            }
            void Patch(string relative, Action<JsonObject> edit)
            {
                var path = Path.Combine(target, "Resources", relative); var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                edit(root); File.WriteAllText(path, root.ToJsonString());
            }
            Patch("card-zone-systems/spire_zones.json", root => {
                var graph = root["spire_zones"]!.AsObject();
                var hand = graph["zones"]!.AsArray().Single(zone => zone!["zoneId"]!.GetValue<string>() == "hand")!;
                hand["ownerScope"] = "Actor"; hand["visibility"]!["contents"] = publicHand ? "All" : "Owner";
                foreach (var flow in graph["flows"]!.AsArray()) {
                    flow!.AsObject().Remove("triggers");
                    foreach (var step in flow["steps"]!.AsArray()) {
                        if (step!["sourceZoneId"]?.GetValue<string>() == "hand") step["sourceOwner"] = "ActiveActor";
                        if (step["targetZoneId"]?.GetValue<string>() == "hand") step["targetOwner"] = "ActiveActor";
                    }
                }
                graph["flows"]!.AsArray().Add(JsonNode.Parse("""
                    {"flowId":"deal-active","triggers":["encounter.started","activation.started"],"actorScope":"Any","allowedInvocations":["Boundary"],
                     "steps":[{"stepId":"deal","operation":"Move","sourceZoneId":"draw","sourceOwner":"RunOwner","targetZoneId":"hand","targetOwner":"ActiveActor","selection":{"strategy":"First","targetZoneCount":2},"onInsufficient":"AllowPartial"}]}
                    """));
            });
            Patch("entities/enemy_goblin.json", root => root["enemy_goblin"]!["components"]!.AsArray()
                .Single(component => component!["type"]!.GetValue<string>() == "resources")!["pools"]!["energy"] = JsonNode.Parse("{\"current\":3,\"max\":3}"));
            Patch("gambits/gambits.json", root => root["enemy_basic_attack"]!["action"] = JsonNode.Parse("""
                {"actionType":"PLAY_CARD","cardDefinitionId":"basic_attack","targetSelector":{"strategy":"FirstOrdinal","relationship":"Enemy"}}
                """));
            Patch("combat-rules/standard_combat.json", root => root["standard_combat"]!["flow"]!["ai"]!["decisionIds"] = new JsonArray("enemy_basic_attack", "enemy_end_turn"));
        }
    }
}
