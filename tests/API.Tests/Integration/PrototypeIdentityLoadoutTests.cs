using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class PrototypeIdentityLoadoutTests
{
    [Fact]
    public async Task CharacterIdentitySwapsIndependentlyOfSixCommonPositionsThroughRestAndReplay()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("prototype-test", "prototype_trial", "player", 8819, "prototype_campaign");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        async Task<JsonElement> Read() => await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{id}/loadout?actorId=player");
        var initial = await Read();
        Assert.True(initial.GetProperty("canEquip").GetBoolean());
        var slots = initial.GetProperty("policy").GetProperty("slots").EnumerateArray().ToArray();
        Assert.Equal(7, slots.Length);
        Assert.Equal("identity", slots[0].GetProperty("slotId").GetString());
        Assert.Equal(6, slots.Count(slot => slot.GetProperty("requiredTags").EnumerateArray().Any(tag => tag.GetString() == "common")));
        var items = initial.GetProperty("items").EnumerateArray().ToDictionary(
            row => row.GetProperty("definition").GetProperty("inscriptionId").GetString()!,
            row => row.GetProperty("instance").GetProperty("instanceId").GetGuid());
        var assignments = new Dictionary<string, Guid> { ["capacity_6"] = items["prototype_edge"],
            ["identity"] = items["prototype_identity_focus"] };
        async Task<bool> Preview(Dictionary<string, Guid> draft)
        {
            var run = reads.GetRun(id).Value;
            using var response = await client.PostAsJsonAsync($"/api/v1/runs/{id}/loadout/preview", new
                { actorId = "player", assignments = draft, expectedSequence = run.Sequence, expectedStep = run.Determinism.Step });
            return response.IsSuccessStatusCode;
        }
        async Task Apply()
        {
            var run = reads.GetRun(id).Value;
            var before = CanonicalJson.ComputeHash(run);
            Assert.True(await Preview(assignments));
            Assert.Equal(before, CanonicalJson.ComputeHash(reads.GetRun(id).Value));
            using var response = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
                { commandId = Guid.NewGuid(), type = "APPLY_LOADOUT", expectedSequence = run.Sequence,
                    expectedStep = run.Determinism.Step, payload = new { actorId = "player", assignments } });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        await Apply();
        assignments["identity"] = items["prototype_identity_ward"];
        await Apply();
        var confirmed = reads.GetRun(id).Value;
        Assert.Equal(assignments.OrderBy(pair => pair.Key), confirmed.Loadouts!.Actors["player"].Assignments);
        var hash = CanonicalJson.ComputeHash(confirmed);
        Assert.False(await Preview(new() { ["identity"] = items["prototype_edge"] }));
        Assert.False(await Preview(new() { ["capacity_1"] = items["prototype_identity_focus"] }));
        Assert.False(await Preview(new() { ["capacity_7"] = items["prototype_edge"] }));
        Assert.Equal(hash, CanonicalJson.ComputeHash(reads.GetRun(id).Value));
        var verification = await factory.Services.GetRequiredService<Core.Run.Replay.IRunReplayService>().VerifyAsync(id);
        Assert.True(verification.IsValid, string.Join("; ", verification.Errors));
        Assert.Equal(hash, verification.ActualFinalHash);
    }
}
