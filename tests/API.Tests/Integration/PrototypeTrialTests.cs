using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed partial class PrototypeTrialTests
{
    [Theory]
    [InlineData("void", "hand", "banish")]
    [InlineData("necro", "discard", "banish")]
    public async Task ModulePaymentsAndRetryPreserveOwnedInstancesAndCaptureTheNextAttempt(string module, string origin, string destination)
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("prototype-test", "prototype_trial", "player", 8811, "prototype_campaign");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        if (module == "necro")
        {
            var build = reads.GetRun(id).Value;
            using var edited = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
            { commandId = Guid.NewGuid(), type = "EDIT_DECK_LOADOUT", expectedSequence = build.Sequence, expectedStep = build.Determinism.Step,
                payload = new { actorId = "player", groupId = "module", moduleId = "prototype_necro" } });
            Assert.True(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync());
        }
        var combatId = await game.StartCombatAsync("player", ["enemy_0"], runId: id, heroDefinitionId: "prototype_hero", enemyDefinitionId: "prototype_target");
        for (var i = 0; i < 2; i++) await EndTurn(client, reads, id, combatId);
        var run = reads.GetRun(id).Value;
        var strike = run.Deck.Topology.GetZone("hand", "$run")!.InstanceIds.First(card => run.Deck.Topology.Instances[card].DefinitionId == "prototype_strike");
        await Play(client, reads, id, combatId, strike, ["enemy_0"]);
        run = reads.GetRun(id).Value;
        var payable = run.Deck.Topology.GetZone("hand", "$run")!.InstanceIds.First(card =>
            run.Deck.Topology.Instances[card].DefinitionId.StartsWith("prototype_" + module + "_", StringComparison.Ordinal));
        var definition = run.Deck.Topology.Instances[payable].DefinitionId;
        var fee = origin == "discard" ? strike : run.Deck.Topology.GetZone("hand", "$run")!.InstanceIds.First(card => card != payable);
        var command = new { actorId = "player", actionType = "PLAY_CARD", cardInstanceId = payable,
            targetIds = definition.EndsWith("_guard", StringComparison.Ordinal) ? Array.Empty<string>() : new[] { "enemy_0" },
            paymentSelections = new[] { new { componentId = "cost", paymentId = "fuel", cardInstanceIds = new[] { fee } } } };
        using var preview = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/cards/payment-preview", new
        { expectedSequence = run.Sequence, expectedStep = run.GetActiveEncounter()!.Combat.Determinism.Step, command });
        var previewText = await preview.Content.ReadAsStringAsync();
        Assert.True(preview.IsSuccessStatusCode, previewText);
        Assert.True(JsonDocument.Parse(previewText).RootElement.GetProperty("isLegal").GetBoolean(), previewText);
        using var paid = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
        { commandId = Guid.NewGuid(), type = "PLAY_CARD", expectedSequence = run.Sequence, expectedStep = run.GetActiveEncounter()!.Combat.Determinism.Step, payload = command });
        Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
        var afterPayment = reads.GetRun(id).Value;
        Assert.Contains(fee, afterPayment.Deck.Topology.GetZone(destination, "$run")!.InstanceIds);
        Assert.DoesNotContain(fee, afterPayment.Deck.Topology.GetZone(origin, "$run")!.InstanceIds);
        Assert.DoesNotContain(fee, afterPayment.Deck.Topology.GetZone("discard", "$run")!.InstanceIds);
        Assert.DoesNotContain(fee, run.Deck.Topology.GetZone(destination, "$run")!.InstanceIds);
        for (var i = 0; i < 30 && reads.GetRun(id).Value.GetActiveEncounter()!.Combat.IsActive; i++) await EndTurn(client, reads, id, combatId);
        run = reads.GetRun(id).Value;
        var lost = run.GetActiveEncounter()!;
        Assert.Equal(Core.Combat.Models.CombatStatus.DEFEAT, lost.Combat.Status);
        using var resolved = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
        { commandId = Guid.NewGuid(), type = "RESOLVE_COMBAT", expectedSequence = run.Sequence, expectedStep = lost.Combat.Determinism.Step, payload = new { combatId } });
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        run = reads.GetRun(id).Value;
        Assert.Equal(RunLifecycleState.Active, run.Lifecycle);
        Assert.Null(run.ActiveEncounterId);
        Assert.Empty(run.Map.ResolvedNodeIds);
        Assert.Equal(27, run.Deck.Topology.GetZone("owned", "$run")!.InstanceIds.Count);
        Assert.Empty(run.Deck.Topology.GetZone("banish", "$run")!.InstanceIds);
        Assert.Equal(afterPayment.Deck.Topology.Instances.Keys.Order(), run.Deck.Topology.Instances.Keys.Order());
        Assert.True(run.Encounters[0].Resolved);
        var nextId = await game.StartCombatAsync("player", ["enemy_0"], runId: id, heroDefinitionId: "prototype_hero", enemyDefinitionId: "prototype_target");
        var next = reads.GetRun(id).Value.GetActiveEncounter()!;
        Assert.NotEqual(combatId, nextId);
        Assert.Equal(100, next.Combat.GetActor("player")!.ResourceState.Current("health"));
        Assert.Equal(0, next.Combat.GetActor("player")!.ResourceState.Current("heat"));
        Assert.Equal(0, next.Combat.GetActor("player")!.ResourceState.Current("block"));
        Assert.Equal("prototype_" + module, next.DeckComposition!.Modules["module"]);
        Assert.Null(next.Combat.ActivationState!.CadenceProgress);
        Assert.Equal(0, lost.Combat.GetActor("player")!.ResourceState.Current("health"));
    }

    [Fact]
    public async Task PublishedTrialKeepsReserveOutOfCirculationRetainsHandAndCountsSupportActivations()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("prototype-test", "prototype_trial", "player", 8811, "prototype_campaign");
        var runs = factory.Services.GetRequiredService<IRunQueryService>();
        var initial = runs.GetRun(id).Value;
        Assert.Equal(27, initial.Deck.Topology.Instances.Count);
        Assert.Equal(15, initial.DeckLoadout!.Actors["player"].Groups["hero"].Length);
        Assert.Equal(5, initial.DeckLoadout.Actors["player"].Groups["module"].Length);
        var combatId = await game.StartCombatAsync("player", ["enemy_0"], runId: id, heroDefinitionId: "prototype_hero", enemyDefinitionId: "prototype_target");
        var run = runs.GetRun(id).Value;
        Assert.Equal(7, run.Deck.Topology.GetZone("owned", "$run")!.InstanceIds.Count);
        Assert.Equal(5, run.Deck.Topology.GetZone("hand", "$run")!.InstanceIds.Count);
        Assert.Equal(15, run.Deck.Topology.GetZone("draw", "$run")!.InstanceIds.Count);
        Assert.Equal(3, run.GetActiveEncounter()!.Combat.GetActor("player")!.ResourceState.Current("energy"));
        Assert.Equal(3, run.GetActiveEncounter()!.Combat.GetActor("enemy_0")!.ResourceState.Current("energy"));
        Assert.Equal(100, run.GetActiveEncounter()!.Combat.GetActor("player")!.ResourceState.Current("health"));
        var retained = run.Deck.Topology.GetZone("hand", "$run")!.InstanceIds.ToArray();
        for (var activation = 0; activation < 3; activation++)
        {
            await EndTurn(client, runs, id, combatId);
            run = runs.GetRun(id).Value;
            var hand = run.Deck.Topology.GetZone("hand", "$run")!.InstanceIds;
            Assert.All(retained, card => Assert.Contains(card, hand));
            Assert.Equal(activation == 0 ? 7 : 8, hand.Count);
            Assert.Equal(27, run.Deck.Topology.Instances.Count);
            Assert.Equal(7, run.Deck.Topology.GetZone("owned", "$run")!.InstanceIds.Count);
        }
        var combat = run.GetActiveEncounter()!.Combat;
        Assert.Single(combat.ActionHistory, action => action.ActorId == "$support::player" && action.PowerId == "prototype_mark");
        Assert.Contains(combat.StatusEffects["enemy_0"], status => status.Definition.StatusId == "prototype_wet");
        Assert.False(combat.GetActor("player")!.ResourceState.Resources.ContainsKey("heat"));
        var cardId = run.Deck.Topology.GetZone("hand", "$run")!.InstanceIds.First(card => run.Deck.Topology.Instances[card].DefinitionId == "prototype_strike");
        using var played = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
        {
            commandId = Guid.Parse("81100000-0000-4000-8000-000000000001"), type = "PLAY_CARD", expectedSequence = run.Sequence,
            expectedStep = combat.Determinism.Step, payload = new { actorId = "player", actionType = "PLAY_CARD", cardInstanceId = cardId, targetIds = new[] { "enemy_0" } }
        });
        Assert.True(played.IsSuccessStatusCode, await played.Content.ReadAsStringAsync());
        run = runs.GetRun(id).Value;
        Assert.False(run.GetActiveEncounter()!.Combat.GetActor("player")!.ResourceState.Resources.ContainsKey("heat"));
        Assert.Equal(2, run.GetActiveEncounter()!.Combat.GetActor("player")!.ResourceState.Current("energy"));
        await EndTurn(client, runs, id, combatId);
        Assert.False(runs.GetRun(id).Value.GetActiveEncounter()!.Combat.GetActor("player")!.ResourceState.Resources.ContainsKey("heat"));
        Assert.Equal(3, runs.GetRun(id).Value.GetActiveEncounter()!.Combat.GetActor("player")!.ResourceState.Current("energy"));
        Assert.Equal(27, initial.Deck.Topology.Instances.Count);
        Assert.Null(initial.ActiveEncounterId);
    }
    private static async Task EndTurn(HttpClient client, IRunQueryService runs, Guid runId, Guid combatId)
    {
        var run = runs.GetRun(runId).Value;
        using var response = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
        { commandId = Guid.NewGuid(), type = "END_TURN", expectedSequence = run.Sequence,
            expectedStep = run.GetActiveEncounter()!.Combat.Determinism.Step, payload = new { actorId = "player" } });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task Play(HttpClient client, IRunQueryService runs, Guid runId, Guid combatId, Guid card, string[] targets)
    {
        var run = runs.GetRun(runId).Value;
        using var response = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
        { commandId = Guid.NewGuid(), type = "PLAY_CARD", expectedSequence = run.Sequence,
            expectedStep = run.GetActiveEncounter()!.Combat.Determinism.Step,
            payload = new { actorId = "player", cardInstanceId = card, targetIds = targets } });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

}
