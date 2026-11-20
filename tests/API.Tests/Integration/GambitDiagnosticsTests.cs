using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Integration;

public sealed class GambitDiagnosticsTests
{
    [Fact]
    public async Task CoverageIsPinnedAndBudgeted()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var revisions = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/revisions?configName=alisyum");
        var revision = revisions.GetProperty("currentRevision").GetString();
        var coverage = await client.GetFromJsonAsync<JsonElement>($"/api/v1/diagnostics/gambits/coverage?settingId=alisyum&revision={revision}&budget=12");
        Assert.Equal(revision, coverage.GetProperty("contentRevision").GetString());
        Assert.True(coverage.GetProperty("bindings").GetInt32() >= 28);
        Assert.Equal(12, coverage.GetProperty("checkedCases").GetInt32());
        Assert.True(coverage.GetProperty("incomplete").GetBoolean());
        var scoped = await client.GetFromJsonAsync<JsonElement>($"/api/v1/diagnostics/gambits/coverage?settingId=alisyum&revision={revision}&budget=3000&runDefinitionIds=alisyum_chapter_1&runDefinitionIds=alisyum_servo_run&runDefinitionIds=alisyum_gambler_run&runDefinitionIds=alisyum_renegado_run");
        Assert.Equal(28, scoped.GetProperty("bindings").GetInt32());
        Assert.Equal(2016, scoped.GetProperty("checkedCases").GetInt32());
        Assert.InRange(scoped.GetProperty("failedCases").GetInt32(), 0, 2016);
        Assert.False(scoped.GetProperty("incomplete").GetBoolean());
    }
}
