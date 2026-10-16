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
        using var verification = await client.PostAsync($"/api/v1/runs/{id}/verify", null);
        verification.EnsureSuccessStatusCode();
        var replay = await verification.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());
        Assert.True(replay.GetProperty("reexecuted").GetBoolean(), replay.GetRawText());
    }
}
