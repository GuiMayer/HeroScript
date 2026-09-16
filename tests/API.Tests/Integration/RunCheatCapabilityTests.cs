using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Integration;

public sealed class RunCheatCapabilityTests
{
    [Fact]
    public async Task StandardMode_RejectsDirectResourceCheat()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync(modeId: "standard", seed: 14092026);
        var before = await game.GetRunStateAsync(runId);
        const string commandType = "APPLY_RUN_RESOURCE";
        var payload = new
        {
            resourceId = "gold",
            value = 999,
            operation = "SET",
            field = "Current"
        };

        using var response = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = commandType,
            expectedSequence = before.GetProperty("sequence").GetInt32(),
            expectedStep = before.GetProperty("step").GetUInt64(),
            payload
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("do not allow", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        var after = await game.GetRunStateAsync(runId);
        Assert.Equal(before.GetProperty("stateHash").GetString(), after.GetProperty("stateHash").GetString());
    }
}
