using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Controllers;

public sealed class FoundationReadModelTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task RunChoosesResourceAuthority_AndCatalogReadsNeverGrantOrCrossScopes()
    {
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var player = "read-model-" + Guid.NewGuid().ToString("N");
        var runId = await game.StartRunAsync("ascendant", "ascendant_showcase_run", player, 150, "ascendant_showcase");
        var initial = await game.GetRunStateAsync(runId);
        var character = initial.GetProperty("characterResources");
        Assert.Equal("PersistentPlayer", character.GetProperty("authority").GetString());
        Assert.Equal(player, character.GetProperty("ownerId").GetString());
        Assert.Equal(100, character.GetProperty("resources").GetProperty("health").GetProperty("current").GetSingle());
        Assert.NotEqual(player, initial.GetProperty("runResourceOwnerId").GetString());
        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{player}?settingId=ascendant");
        var options = profile.GetProperty("availability").GetProperty("options");
        Assert.Equal(6, options.GetArrayLength()); // Three fixture options, two card sidegrades and one inscription option.
        Assert.All(options.EnumerateArray(), item => Assert.Empty(item.GetProperty("availableTargets").EnumerateArray()));
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var unlocks = await client.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{player}/unlocks?settingId=ascendant");
            Assert.Equal(profile.GetProperty("availability").GetRawText(), unlocks.GetProperty("availability").GetRawText());
            Assert.Empty(unlocks.GetProperty("items").EnumerateArray());
            Assert.Empty(unlocks.GetProperty("unlockProofs").EnumerateArray());
        }
        var other = await client.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{player}-other?settingId=ascendant");
        Assert.Equal(0, other.GetProperty("progressSequence").GetInt64());
        var secondary = await client.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{player}?settingId=default");
        Assert.Equal(0, secondary.GetProperty("progressSequence").GetInt64());
        Assert.Equal(initial.GetRawText(), (await game.GetRunStateAsync(runId)).GetRawText());
        var commands = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        var start = commands.GetProperty("commands").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "START_ENCOUNTER");
        using var response = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        { commandId = Guid.NewGuid(), type = "START_ENCOUNTER", payload = start.GetProperty("validPayload"),
            expectedSequence = start.GetProperty("expectedSequence").GetInt32(), expectedStep = start.GetProperty("expectedStep").GetUInt64() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var active = await game.GetRunStateAsync(runId);
        Assert.Equal("ActiveEncounter", active.GetProperty("characterResources").GetProperty("authority").GetString());
        Assert.Equal(active.GetProperty("activeEncounterId").GetGuid(), active.GetProperty("characterResources").GetProperty("combatId").GetGuid());
        foreach (var kind in new[] { "actor-resource-lifecycle-policies", "profile-progress-policies" })
        {
            using var content = await client.GetAsync($"/api/v1/content/{kind}?configName=ascendant&revision={initial.GetProperty("contentRevision").GetString()}");
            Assert.True(content.IsSuccessStatusCode, await content.Content.ReadAsStringAsync());
        }
    }
}
