using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Services;
using API.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace API.Tests.Integration;

public sealed class ToolAccessSurfaceTests
{
    [Fact]
    public async Task DevMode_CanPreviewContentActivationWithoutMutatingTheRun()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var revision = await game.GetCurrentContentRevisionAsync();
        var runId = await game.StartRunAsync(
            runDefinitionId: "spire_showcase_run",
            modeId: "development_lab",
            seed: 14092029);

        using var response = await client.GetAsync(
            $"/api/v1/runs/{runId}/content/activation-preview?targetRevision={revision}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("compatible").GetBoolean());
        Assert.Equal(revision, preview.GetProperty("currentRevision").GetString());
        Assert.Equal(revision, preview.GetProperty("targetRevision").GetString());
        Assert.Empty(preview.GetProperty("artifactChanges").EnumerateArray());
        Assert.Equal(1, (await game.GetRunStateAsync(runId)).GetProperty("sequence").GetInt32());
    }

    [Theory]
    [InlineData("spire_showcase", "spire_showcase_run", "standard_gameplay", false, false, false)]
    [InlineData("spire_showcase_experimental", "spire_showcase_run", "experimental_tools", true, false, false)]
    [InlineData("combat_sandbox", "default_run", "sandbox_tools", true, true, false)]
    [InlineData("development_lab", "spire_showcase_run", "dev_modder_tools", true, true, true)]
    public async Task ContentModes_ExposeOnlyTheirIntendedToolCeiling(
        string modeId,
        string runDefinitionId,
        string expectedPolicyId,
        bool allowsCheats,
        bool allowsBranches,
        bool allowsContentActivation)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync(
            runDefinitionId: runDefinitionId,
            modeId: modeId,
            seed: 14092028);

        var capabilities = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/capabilities");
        Assert.Equal(expectedPolicyId, capabilities.GetProperty("modeCapabilityPolicyId").GetString());
        var granted = capabilities.GetProperty("granted").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(allowsCheats, granted.Contains(ToolCapabilities.CheatRunResources));
        Assert.Equal(allowsBranches, granted.Contains(ToolCapabilities.BranchCreate));
        Assert.Equal(allowsContentActivation, granted.Contains(ToolCapabilities.ContentActivate));
    }

    [Fact]
    public async Task NormalProfile_ExposesReadOnlyTimelineWithoutHistoricalStateOrTools()
    {
        using var root = new TestWebApplicationFactory();
        using var factory = root.WithWebHostBuilder(builder =>
            builder.UseSetting("ToolAccess:Profile", "normal"));
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync(modeId: "standard", seed: 14092027);

        using var timeline = await client.GetAsync($"/api/v1/runs/{runId}/timeline");
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
        using var commit = await client.GetAsync($"/api/v1/runs/{runId}/commits/1");
        Assert.Equal(HttpStatusCode.Forbidden, commit.StatusCode);
        using var branches = await client.GetAsync($"/api/v1/runs/{runId}/branches");
        Assert.Equal(HttpStatusCode.Forbidden, branches.StatusCode);
        using var replay = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        using var scenario = await client.PostAsJsonAsync("/api/v1/sandbox/scenarios/validate", new { });
        Assert.Equal(HttpStatusCode.Forbidden, scenario.StatusCode);
        using var admin = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/diagnostics/cache/stats");
        admin.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var adminResponse = await client.SendAsync(admin);
        Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);
        var revision = await game.GetCurrentContentRevisionAsync();
        using var activationPreview = await client.GetAsync(
            $"/api/v1/runs/{runId}/content/activation-preview?targetRevision={revision}");
        Assert.Equal(HttpStatusCode.Forbidden, activationPreview.StatusCode);

        var capabilities = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/capabilities");
        Assert.Equal("normal", capabilities.GetProperty("profile").GetString());
        var granted = capabilities.GetProperty("granted").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains(ToolCapabilities.TimelineRead, granted);
        Assert.DoesNotContain(ToolCapabilities.BranchCreate, granted);
    }
}
