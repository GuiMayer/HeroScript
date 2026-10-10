using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Controllers;

public sealed class RunCreationEligibilityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task CreationCommandRetriesKeepOriginalInputs_AndDistinctAttemptsHaveDistinctIdentities()
    {
        using var client = factory.CreateClient();
        var game = new Helpers.GameEngineClientSimulator(client);
        var revision = await game.GetCurrentContentRevisionAsync("ascendant");
        var player = "creation-" + Guid.NewGuid().ToString("N");
        var payload = new { settingId = "ascendant", runDefinitionId = "ascendant_showcase_run", playerEntityId = player,
            seed = (ulong?)null, contentRevision = revision, modeId = "ascendant_showcase", commandId = Guid.NewGuid() };
        using var first = await client.PostAsJsonAsync("/api/v1/runs", payload);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var initial = await first.Content.ReadFromJsonAsync<JsonElement>();
        using var retry = await client.PostAsJsonAsync("/api/v1/runs", payload);
        Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        Assert.Equal(initial.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        using var collision = await client.PostAsJsonAsync("/api/v1/runs", payload with { seed = 42UL });
        Assert.Equal(HttpStatusCode.BadRequest, collision.StatusCode);
        Assert.Contains("reused with different inputs", await collision.Content.ReadAsStringAsync());
        using var next = await client.PostAsJsonAsync("/api/v1/runs", payload with { commandId = Guid.NewGuid() });
        Assert.True(next.IsSuccessStatusCode, await next.Content.ReadAsStringAsync());
        Assert.NotEqual(initial.GetProperty("runId").GetGuid(), (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetGuid());
        var reader = factory.Services.GetRequiredService<IRunCommitReader>();
        Assert.Single(await reader.ListCommitSequencesAsync(initial.GetProperty("runId").GetGuid()));
        Assert.Equal(payload.commandId, (await reader.LoadCommitAsync(initial.GetProperty("runId").GetGuid(), 1))!.RootCommand.CommandId);
    }

    [Theory]
    [InlineData("capturedEligibility")]
    [InlineData("eligibility")]
    [InlineData("unlockIds")]
    public async Task GameplayClientsCannotAssertEligibilityOrUnlocks(string forgedField)
    {
        using var client = factory.CreateClient();
        var game = new Helpers.GameEngineClientSimulator(client);
        var payload = new Dictionary<string, object?>
        {
            ["settingId"] = "ascendant", ["runDefinitionId"] = "ascendant_showcase_run",
            ["playerEntityId"] = "spoof-" + Guid.NewGuid().ToString("N"), ["seed"] = 42,
            ["contentRevision"] = await game.GetCurrentContentRevisionAsync("ascendant"), ["modeId"] = "ascendant_showcase",
            [forgedField] = new[] { "critical_calibration_option" }
        };
        using var result = await client.PostAsJsonAsync("/api/v1/runs", payload);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }
}
