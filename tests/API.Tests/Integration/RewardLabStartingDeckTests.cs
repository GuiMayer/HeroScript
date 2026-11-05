using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class RewardLabStartingDeckTests
{
    [Fact]
    public async Task WorkshopOwnsItsInheritedStartingCardsAndRecreatesTheSameImmutableSnapshot()
    {
        RunState? previous = null;
        string? previousHash = null;
        // Fresh persistence on each attempt: this must recreate the state, not read a cached run.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            var game = new GameEngineClientSimulator(client);
            var revision = await game.GetCurrentContentRevisionAsync("reward-lab");
            using var response = await client.PostAsJsonAsync("/api/v1/runs", new
            {
                settingId = "reward-lab", runDefinitionId = "reward_build_lab_run", playerEntityId = "player",
                seed = 8813UL, contentRevision = revision, modeId = "reward_build_lab"
            });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            var id = created.GetProperty("runId").GetGuid();
            var reads = factory.Services.GetRequiredService<IRunQueryService>();
            var run = reads.GetRun(id).Value;
            var hash = CanonicalJson.ComputeHash(run);
            var topology = run.Deck.Topology;
            Assert.Equal(27, topology.Instances.Count);
            var actor = run.DeckLoadout!.Actors["player"];
            Assert.Equal(15, actor.Groups["hero"].Length);
            Assert.Equal(5, actor.Groups["module"].Length);
            var active = actor.Groups.Values.SelectMany(group => group).ToArray();
            Assert.Equal(20, active.Distinct().Count());
            Assert.Equal(7, topology.Instances.Keys.Except(active).Count());
            foreach (var definition in new[] { "pyro_barreira_cinzas", "pyro_golpe_brasa" })
            {
                var card = Assert.Single(topology.Instances.Values, card => card.DefinitionId == definition);
                Assert.Contains(card.CardInstanceId, actor.Groups["hero"]);
                Assert.Contains(card.CardInstanceId, topology.GetZone("owned", "$run")!.InstanceIds);
            }
            using var projection = await client.GetAsync($"/api/v1/runs/{id}/deck-loadout");
            Assert.True(projection.IsSuccessStatusCode, await projection.Content.ReadAsStringAsync());
            var replay = await factory.Services.GetRequiredService<Core.Run.Replay.IRunReplayService>().VerifyAsync(id);
            Assert.True(replay.IsValid, string.Join("; ", replay.Errors));
            Assert.Equal(hash, replay.ActualFinalHash);
            Assert.Equal(hash, CanonicalJson.ComputeHash(reads.GetRun(id).Value));
            Assert.Equal(hash, CanonicalJson.ComputeHash(run));
            if (previous != null)
            {
                Assert.Equal(previousHash, hash);
                Assert.Equal(previousHash, CanonicalJson.ComputeHash(previous));
            }
            previous = run;
            previousHash = hash;
        }
    }
}
