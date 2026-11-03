using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class PrototypeBuildWorkshopTests
{
    [Theory]
    [InlineData("prototype_split_item")]
    [InlineData("prototype_ward_link_item")]
    [InlineData("prototype_stable_item")]
    public async Task CharacterEquipmentAndReserveMutationsRemainSeparateAndDurable(string itemDefinitionId)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("reward-lab", "reward_build_lab_run", "player", 8813, "reward_build_lab");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        RunState Current() => reads.GetRun(id).Value;
        async Task<JsonElement> Loadout() => await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{id}/loadout?actorId=player");
        async Task<HttpResponseMessage> Command(string type, object payload)
        {
            var run = Current();
            return await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
            { commandId = Guid.NewGuid(), type, expectedSequence = run.Sequence, expectedStep = run.Determinism.Step, payload });
        }
        async Task Apply(string type, object payload)
        {
            using var response = await Command(type, payload);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        var initial = Current();
        var deckHash = CanonicalJson.ComputeHash(initial.Deck);
        var loadout = await Loadout();
        Assert.Equal(3, loadout.GetProperty("items").GetArrayLength());
        var owned = loadout.GetProperty("items").EnumerateArray().ToDictionary(
            item => item.GetProperty("definition").GetProperty("inscriptionId").GetString()!,
            item => item.GetProperty("instance").GetProperty("instanceId").GetGuid());
        var assignments = new Dictionary<string, Guid> { ["trace_1"] = owned["prototype_focus"] };
        using var preview = await client.PostAsJsonAsync($"/api/v1/runs/{id}/loadout/preview", new
        { actorId = "player", assignments, expectedSequence = initial.Sequence, expectedStep = initial.Determinism.Step });
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var comparison = await preview.Content.ReadFromJsonAsync<JsonElement>();
        static float Chance(JsonElement snapshot) => snapshot.GetProperty("attributes").GetProperty("attributes").EnumerateArray()
            .Single(attribute => attribute.GetProperty("attributeId").GetString() == "critical_chance").GetProperty("effectiveValue").GetSingle();
        Assert.Equal(Chance(comparison.GetProperty("before")) + 30, Chance(comparison.GetProperty("after")));
        Assert.Equal(CanonicalJson.ComputeHash(initial), CanonicalJson.ComputeHash(Current()));
        await Apply("APPLY_LOADOUT", new { actorId = "player", assignments });
        Assert.Equal(deckHash, CanonicalJson.ComputeHash(Current().Deck));
        using var stale = await client.PostAsJsonAsync($"/api/v1/runs/{id}/loadout/preview", new
        { actorId = "player", assignments, expectedSequence = initial.Sequence, expectedStep = initial.Determinism.Step });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var beforeInvalid = CanonicalJson.ComputeHash(Current());
        using var overflow = await Command("APPLY_LOADOUT", new { actorId = "player", assignments = new Dictionary<string, Guid>
        { ["trace_1"] = owned["prototype_focus"], ["trace_2"] = owned["prototype_ward"], ["trace_3"] = owned["prototype_edge"] } });
        Assert.False(overflow.IsSuccessStatusCode);
        using var badSlot = await Command("APPLY_LOADOUT", new { actorId = "player", assignments = new Dictionary<string, Guid> { ["unknown"] = owned["prototype_focus"] } });
        Assert.False(badSlot.IsSuccessStatusCode);
        Assert.Equal(beforeInvalid, CanonicalJson.ComputeHash(Current()));
        await Apply("ACQUIRE_CARD_MODIFIER_ITEM", new { itemDefinitionId });
        Assert.Equal(0, Current().ResourceState.Current("parts"));
        Assert.True(Core.Run.Content.CardTransformationAccess.SpendCosts(Current()).IsFailure);
        var modifier = Assert.Single(Current().CardModifierItems!.Items);
        var reserve = Current().Deck.Topology.GetZone("owned", "$run")!.InstanceIds
            .First(card => Current().Deck.Topology.Instances[card].DefinitionId == "prototype_strike" &&
                !Current().DeckLoadout!.Actors["player"].Groups.Values.Any(group => group.Contains(card)));
        var guard = Current().Deck.Topology.Instances.Values.First(card => card.DefinitionId == "prototype_guard").CardInstanceId;
        beforeInvalid = CanonicalJson.ComputeHash(Current());
        using var incompatible = await Command("APPLY_CARD_MODIFIER_ITEM", new { itemInstanceId = modifier.ItemInstanceId, cardInstanceId = guard });
        Assert.False(incompatible.IsSuccessStatusCode);
        Assert.Equal(beforeInvalid, CanonicalJson.ComputeHash(Current()));
        var originalCardHash = CanonicalJson.ComputeHash(initial.Deck.Topology.Instances[reserve]);
        await Apply("APPLY_CARD_MODIFIER_ITEM", new { itemInstanceId = modifier.ItemInstanceId, cardInstanceId = reserve });
        Assert.Single(Current().Deck.Topology.Instances[reserve].Upgrades);
        Assert.Equal(originalCardHash, CanonicalJson.ComputeHash(initial.Deck.Topology.Instances[reserve]));
        var equipmentHash = CanonicalJson.ComputeHash(Current().Loadouts);
        beforeInvalid = CanonicalJson.ComputeHash(Current());
        using var repeated = await Command("APPLY_CARD_MODIFIER_ITEM", new { itemInstanceId = modifier.ItemInstanceId, cardInstanceId = reserve });
        Assert.False(repeated.IsSuccessStatusCode);
        Assert.Equal(beforeInvalid, CanonicalJson.ComputeHash(Current()));
        var composition = Current().DeckLoadout!.Actors["player"].Groups["hero"].ToArray();
        await Apply("EDIT_DECK_LOADOUT", new { actorId = "player", groupId = "hero", replacedCardId = composition[0], replacementCardId = reserve });
        Assert.Contains(reserve, Current().DeckLoadout!.Actors["player"].Groups["hero"]);
        await Apply("EDIT_DECK_LOADOUT", new { actorId = "player", groupId = "module", moduleId = "prototype_necro" });
        Assert.Single(Current().Deck.Topology.Instances[reserve].Upgrades);
        Assert.Equal(equipmentHash, CanonicalJson.ComputeHash(Current().Loadouts));
        var finalHash = CanonicalJson.ComputeHash(Current());
        var directory = factory.PersistenceRoot;
        client.Dispose(); factory.Dispose();
        using var restarted = new TestWebApplicationFactory(directory);
        using var resumed = restarted.CreateClient();
        var restored = restarted.Services.GetRequiredService<IRunQueryService>().GetRun(id).Value;
        Assert.Equal(finalHash, CanonicalJson.ComputeHash(restored));
        var replay = await restarted.Services.GetRequiredService<Core.Run.Replay.IRunReplayService>().VerifyAsync(id);
        Assert.True(replay.IsValid, string.Join("; ", replay.Errors));
        Assert.True(replay.Reexecuted);
        Assert.Equal(finalHash, replay.ActualFinalHash);
    }
}
