using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace API.Tests.Controllers;

public sealed class InscriptionGameplayTests
{
    [Fact]
    public async Task PublishedLoadoutPreviewsCommitsRetriesRestartsAndReplaysThroughRealRest()
    {
        string persistence;
        Guid runId;
        string actor = "inscription-" + Guid.NewGuid().ToString("N");
        object command;
        using (var factory = new TestWebApplicationFactory())
        {
            persistence = factory.PersistenceRoot;
            using var client = factory.CreateClient();
            var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/settings");
            var setting = settings.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("settingId").GetString() == "ascendant");
            using var created = await client.PostAsJsonAsync("/api/v1/runs", new { settingId = "ascendant", modeId = "ascendant_world_one",
                runDefinitionId = "ascendant_world_one_run", playerEntityId = actor, seed = 20261015UL,
                contentRevision = setting.GetProperty("currentRevision").GetString() });
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
            var run = await created.Content.ReadFromJsonAsync<JsonElement>();
            runId = run.GetProperty("runId").GetGuid();
            var snapshot = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/loadout?actorId={actor}");
            Assert.Equal(2, snapshot.GetProperty("items").GetArrayLength());
            Assert.Empty(snapshot.GetProperty("assignments").EnumerateObject());
            var first = snapshot.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("definition").GetProperty("inscriptionId").GetString() == "w1_focus");
            var assignments = new Dictionary<string, Guid> { ["trace_1"] = first.GetProperty("instance").GetProperty("instanceId").GetGuid() };
            using var preview = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/loadout/preview", new { actorId = actor, assignments,
                expectedSequence = snapshot.GetProperty("runSequence").GetInt32(), expectedStep = snapshot.GetProperty("expectedStep").GetUInt64() });
            Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
            var projected = await preview.Content.ReadFromJsonAsync<JsonElement>();
            float Chance(JsonElement value) => value.GetProperty("attributes").GetProperty("attributes").EnumerateArray()
                .Single(stat => stat.GetProperty("attributeId").GetString() == "critical_chance").GetProperty("effectiveValue").GetSingle();
            Assert.Equal(Chance(projected.GetProperty("before")) + 30, Chance(projected.GetProperty("after")));
            command = new { commandId = Guid.NewGuid(), type = "APPLY_LOADOUT", expectedSequence = snapshot.GetProperty("runSequence").GetInt32(),
                expectedStep = snapshot.GetProperty("expectedStep").GetUInt64(), payload = new { actorId = actor, assignments } };
            using var applied = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", command);
            Assert.True(applied.IsSuccessStatusCode, await applied.Content.ReadAsStringAsync());
            using var stale = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/loadout/preview", new { actorId = actor, assignments,
                expectedSequence = snapshot.GetProperty("runSequence").GetInt32(), expectedStep = snapshot.GetProperty("expectedStep").GetUInt64() });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }
        using var restarted = new TestWebApplicationFactory(persistence);
        using var resumed = restarted.CreateClient();
        using var retry = await resumed.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", command);
        Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        var restored = await resumed.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/loadout?actorId={actor}");
        Assert.Single(restored.GetProperty("assignments").EnumerateObject());
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var verified = await resumed.PostAsync($"/api/v1/runs/{runId}/verify", null);
            Assert.True(verified.IsSuccessStatusCode, await verified.Content.ReadAsStringAsync());
            var report = await verified.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(report.GetProperty("isValid").GetBoolean(), report.GetRawText());
        }
    }
}
