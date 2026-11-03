using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class TerminalRewardJourneyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VictoryRewardIsExclusiveIdempotentAndStoredOutsideTheActiveBuild(bool decompile)
    {
        using var factory = new TestWebApplicationFactory(); using var originalClient = factory.CreateClient();
        var client = originalClient;
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("reward-lab", "reward_lab_run", "player", 8812, "reward_lab");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        var combatId = await game.StartCombatAsync("player", ["enemy_0"], runId: id, heroDefinitionId: "prototype_hero", enemyDefinitionId: "reward_lab_target");
        await game.ExecuteActionAsync(combatId, "player", "enemy_0", cardId: "prototype_strike", runId: id);
        var won = reads.GetRun(id).Value;
        Assert.Equal(Core.Combat.Models.CombatStatus.VICTORY, won.GetActiveEncounter()!.Combat.Status);
        await RunCommand(client, reads, id, "RESOLVE_COMBAT", new { combatId }, won.GetActiveEncounter()!.Combat.Determinism.Step);
        await RunCommand(client, reads, id, "ADVANCE_NODE", new { targetNodeId = "trial_reward" });
        var offer = await game.StartCardSelectionAsync(id, "prototype_reward");
        var selectionId = offer.GetProperty("selectionInstanceId").GetGuid();
        var card = offer.GetProperty("options")[0].GetProperty("cardId").GetString()!;
        Assert.True(offer.GetProperty("decompose").GetProperty("completesSelection").GetBoolean());
        Assert.Equal(offer.GetProperty("options").GetArrayLength(), offer.GetProperty("options").EnumerateArray().Select(option => option.GetProperty("cardId").GetString()).Distinct().Count());
        var before = reads.GetRun(id).Value;
        var payload = decompile ? (object)new { selectionInstanceId = selectionId, cardId = card } : new { selectionInstanceId = selectionId, cardIds = new[] { card } };
        var envelope = new { commandId = Guid.NewGuid(), type = decompile ? "DECOMPOSE_CARD_REWARD" : "PICK_CARD_REWARD",
            expectedSequence = before.Sequence, expectedStep = before.Determinism.Step, payload };
        using var receipt = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", envelope);
        Assert.True(receipt.IsSuccessStatusCode, await receipt.Content.ReadAsStringAsync());
        var after = reads.GetRun(id).Value;
        Assert.True(after.CardSelections[^1].Completed);
        Assert.Equal(decompile ? 3 : 0, after.ResourceState.Current("parts"));
        Assert.Equal(decompile ? 1 : 2, after.Deck.Topology.Instances.Count);
        Assert.Equal(before.DeckLoadout!.Actors["player"].Groups["hero"], after.DeckLoadout!.Actors["player"].Groups["hero"]);
        if (!decompile)
        {
            var granted = Assert.Single(after.Deck.Topology.Instances.Keys.Except(before.Deck.Topology.Instances.Keys));
            Assert.Contains(granted, after.Deck.Topology.GetZone("owned", "$run")!.InstanceIds);
            Assert.DoesNotContain(granted, after.DeckLoadout.Actors["player"].Groups["hero"]);
        }
        var hash = CanonicalJson.ComputeHash(after);
        var directory = factory.PersistenceRoot;
        originalClient.Dispose(); factory.Dispose();
        using var restartedFactory = new TestWebApplicationFactory(directory);
        using var restartedClient = restartedFactory.CreateClient();
        client = restartedClient;
        reads = restartedFactory.Services.GetRequiredService<IRunQueryService>();
        game = new GameEngineClientSimulator(client);
        Assert.Equal(hash, CanonicalJson.ComputeHash(reads.GetRun(id).Value));
        Assert.True(reads.GetRun(id).Value.CardSelections[^1].Completed);
        var replay = await restartedFactory.Services.GetRequiredService<Core.Run.Replay.IRunReplayService>().VerifyAsync(id);
        Assert.True(replay.IsValid, string.Join("; ", replay.Errors));
        Assert.True(replay.Reexecuted);
        Assert.Equal(hash, replay.ActualFinalHash);
        using var duplicate = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", envelope);
        Assert.True(duplicate.IsSuccessStatusCode, await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(hash, CanonicalJson.ComputeHash(reads.GetRun(id).Value));
        var opposite = decompile ? (object)new { selectionInstanceId = selectionId, cardIds = new[] { card } } : new { selectionInstanceId = selectionId, cardId = card };
        using var rejected = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
        { commandId = Guid.NewGuid(), type = decompile ? "PICK_CARD_REWARD" : "DECOMPOSE_CARD_REWARD", expectedSequence = after.Sequence,
            expectedStep = after.Determinism.Step, payload = opposite });
        Assert.False(rejected.IsSuccessStatusCode);
        Assert.Equal(hash, CanonicalJson.ComputeHash(reads.GetRun(id).Value));
        await RunCommand(client, reads, id, "RESOLVE_NODE", new { currentNodeId = "trial_reward" });
        await RunCommand(client, reads, id, "ADVANCE_NODE", new { targetNodeId = "trial_preparation" });
        await RunCommand(client, reads, id, "CREATE_PREPARATION", new { preparationId = "prototype_rebuild" });
        var preparation = reads.GetRun(id).Value.Preparations[^1];
        Assert.DoesNotContain(reads.GetAvailableCommands(id).Value, command => command.Type == "RESOLVE_NODE");
        await RunCommand(client, reads, id, "APPLY_PREPARATION_OPTION", new { preparationInstanceId = preparation.PreparationInstanceId, optionId = "ready" });
        await RunCommand(client, reads, id, "RESOLVE_NODE", new { currentNodeId = "trial_preparation" });
        await RunCommand(client, reads, id, "ADVANCE_NODE", new { targetNodeId = "thermal_trial_2" });
        await game.StartCombatAsync("player", ["enemy_0"], runId: id, heroDefinitionId: "prototype_hero", enemyDefinitionId: "reward_lab_target");
        var next = reads.GetRun(id).Value;
        Assert.Single(next.Deck.Topology.GetZone("hand", "$run")!.InstanceIds);
        Assert.Equal(decompile ? 0 : 1, next.Deck.Topology.GetZone("owned", "$run")!.InstanceIds.Count);
        Assert.Equal(100, next.GetActiveEncounter()!.Combat.GetActor("player")!.ResourceState.Current("health"));
    }
    private static async Task RunCommand(HttpClient client, IRunQueryService reads, Guid id, string type, object payload, ulong? step = null)
    {
        var run = reads.GetRun(id).Value;
        using var response = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
        { commandId = Guid.NewGuid(), type, expectedSequence = run.Sequence, expectedStep = step ?? run.Determinism.Step, payload });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
