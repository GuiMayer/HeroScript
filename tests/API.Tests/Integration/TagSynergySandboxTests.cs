using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Config;
using Xunit;

namespace API.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class TagSynergySandboxTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("energy")] [InlineData("defense")] [InlineData("residual")]
    [InlineData("circulation")] [InlineData("mixed")]
    public async Task Presets_LaunchWithPinnedAscendantRulesAndReplay(string presetId)
    {
        using var client = factory.CreateClient();
        var root = ResourceProviderFactory.FindProjectRoot()!;
        var catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(root,
            "examples/godot-engine-showcase/data/labs/tag_synergy_scenarios.json")))!;
        var preset = catalog["presets"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == presetId)!;
        var revisions = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/revisions?configName=ascendant");
        var scenario = preset["scenario"]!.DeepClone().AsObject();
        scenario["schemaVersion"] = 2;
        scenario["modeId"] = "w1_synergy_sandbox";
        scenario["contentRevision"] = revisions.GetProperty("currentRevision").GetString();
        scenario["seed"] = 123;
        scenario["attemptKey"] = "tag-synergy-" + presetId + "-" + Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/v1/sandbox/runs?configName=ascendant", scenario);
        var launch = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, launch.GetRawText());
        var id = launch.GetProperty("run").GetProperty("runId").GetGuid();
        var snapshot = await client.GetFromJsonAsync<JsonElement>($"/api/v1/sandbox/runs/{id}/snapshot");
        Assert.Equal(revisions.GetProperty("currentRevision").GetString(),
            snapshot.GetProperty("run").GetProperty("contentRevision").GetString());
        Assert.Equal("w1_synergy_sandbox", snapshot.GetProperty("run").GetProperty("scenario").GetProperty("modeId").GetString());
        Assert.True(snapshot.GetProperty("combat").GetProperty("activation").GetProperty("waitingForInput").GetBoolean());
        var combatId = snapshot.GetProperty("combat").GetProperty("combatId").GetGuid();
        var legal = await client.GetFromJsonAsync<JsonElement>($"/api/v1/combats/{combatId}/legal-actions?actorId=player");
        var candidate = legal.GetProperty("candidates").EnumerateArray().First(c =>
            c.GetProperty("command").GetProperty("actionType").GetString() == "PLAY_CARD").GetProperty("command");
        using var played = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new {
            commandId = Guid.NewGuid(), type = "PLAY_CARD",
            expectedSequence = snapshot.GetProperty("run").GetProperty("sequence").GetInt32(),
            expectedStep = snapshot.GetProperty("combat").GetProperty("step").GetUInt64(),
            payload = new { actorId = "player", cardInstanceId = candidate.GetProperty("cardInstanceId").GetGuid(),
                targetIds = candidate.GetProperty("targetIds").EnumerateArray().Select(t => t.GetString()).ToArray() } });
        Assert.True(played.IsSuccessStatusCode, await played.Content.ReadAsStringAsync());
        var source = await client.GetFromJsonAsync<JsonElement>($"/api/v1/sandbox/runs/{id}/snapshot");
        var sequence = source.GetProperty("run").GetProperty("sequence").GetInt32();
        using var forked = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/timeline/{sequence}/branches",
            new { branchKey = "certify-" + presetId });
        var branch = await forked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(forked.IsSuccessStatusCode, branch.GetRawText());
        Assert.Equal(source.GetRawText(), (await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/sandbox/runs/{id}/snapshot")).GetRawText());
        using var branchVerify = await client.PostAsync($"/api/v1/runs/{branch.GetProperty("runId").GetGuid()}/verify", null);
        var branchReplay = await branchVerify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(branchVerify.IsSuccessStatusCode && branchReplay.GetProperty("isValid").GetBoolean() &&
            branchReplay.GetProperty("reexecuted").GetBoolean(), branchReplay.GetRawText());
        using var verification = await client.PostAsync($"/api/v1/runs/{id}/verify", null);
        verification.EnsureSuccessStatusCode();
        var replay = await verification.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());
        Assert.True(replay.GetProperty("reexecuted").GetBoolean(), replay.GetRawText());
    }
}
