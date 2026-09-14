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

        var capabilities = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/capabilities");
        Assert.Equal("normal", capabilities.GetProperty("profile").GetString());
        var granted = capabilities.GetProperty("granted").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains(ToolCapabilities.TimelineRead, granted);
        Assert.DoesNotContain(ToolCapabilities.BranchCreate, granted);
    }
}
