using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class SupportLoadoutJourneyTests
{
    [Fact]
    public async Task RealHostEquipmentPreviewAndThreeActivationCadenceUseCanonicalGateway()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync("support-lab", "support_lab_run", "player", 71903, "support_lab");
        var query = factory.Services.GetRequiredService<IRunQueryService>();
        var before = CanonicalJson.ComputeHash(query.GetRun(runId).Value);
        var snapshot = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/support-loadout");
        Assert.True(snapshot.GetProperty("enabled").GetBoolean());
        Assert.True(snapshot.GetProperty("canChange").GetBoolean());
        Assert.Single(snapshot.GetProperty("owned").EnumerateArray());
        var seq = snapshot.GetProperty("runSequence").GetInt32(); var step = snapshot.GetProperty("expectedStep").GetUInt64();
        var equipment = new { actorId = "player", supportId = "support_sentinel", routineId = "support_defense" };
        using var preview = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/support-loadout/preview",
            new { expectedSequence = seq, expectedStep = step, equipment });
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        Assert.Equal(before, CanonicalJson.ComputeHash(query.GetRun(runId).Value));
        using var stale = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/support-loadout/preview",
            new { expectedSequence = seq - 1, expectedStep = step, equipment });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var command = new { commandId = Guid.Parse("79900000-0000-4000-8000-000000000001"), type = "EQUIP_SUPPORT", expectedSequence = seq, expectedStep = step, payload = equipment };
        using var receipt = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", command);
        Assert.True(receipt.IsSuccessStatusCode, await receipt.Content.ReadAsStringAsync());
        var committed = CanonicalJson.ComputeHash(query.GetRun(runId).Value);
        using var duplicate = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", command);
        Assert.True(duplicate.IsSuccessStatusCode, await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(committed, CanonicalJson.ComputeHash(query.GetRun(runId).Value));
        var combatId = await game.StartCombatAsync("player", ["enemy_0"], runId: runId, heroDefinitionId: "core_adept", enemyDefinitionId: "payment_target");
        var encounter = query.GetRun(runId).Value.GetActiveEncounter()!;
        Assert.Equal("support_defense", encounter.SupportComposition!.RoutineId);
        Assert.False(encounter.Combat.GetActor("$support::player")!.CountsForOutcome);
        for (var turn = 1; turn <= 3; turn++)
        {
            await game.EndTurnAsync(combatId, runId);
            var combat = query.GetRun(runId).Value.GetActiveEncounter()!.Combat;
            var supportActions = combat.ActionHistory.Where(action => action.ActorId == "$support::player" && action.PowerId == "defend").ToArray();
            Assert.Equal(turn == 3 ? 1 : 0, supportActions.Length);
        }
        var final = query.GetRun(runId).Value;
        Assert.Equal(5, final.GetActiveEncounter()!.Combat.GetActor("$support::player")!.ResourceState.Current("block"));
        var session = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/session");
        var status = session.GetProperty("run").GetProperty("supportCombat");
        Assert.Equal("support_defense", status.GetProperty("routineId").GetString());
        Assert.Equal(3, status.GetProperty("remainingOwnerActivations").GetInt32());
        var locked = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/support-loadout");
        Assert.False(locked.GetProperty("canChange").GetBoolean());
        using var rejected = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/support-loadout/preview", new
        { expectedSequence = final.Sequence, expectedStep = final.Determinism.Step, equipment });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }
}
