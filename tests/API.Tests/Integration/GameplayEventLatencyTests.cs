using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Determinism;
using Core.Run;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

public sealed class GameplayEventLatencyTests(ITestOutputHelper output)
{
    private sealed class TimedFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        { base.ConfigureWebHost(builder); builder.UseSetting("Performance:DiagnosticsEnabled", "true"); }
    }

    [Theory]
    [InlineData("default", "spire_showcase_run", "spire_showcase")]
    [InlineData("ascendant", "ascendant_world_one_run", "ascendant_world_one")]
    public async Task SocketRestCommandAndSessionPreserveCommittedStateAndMeasureResponseWork(string setting, string definition, string mode)
    {
        using var factory = new TimedFactory();
        factory.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var player = "player";
        var runId = await game.StartRunAsync(setting, definition, player, 541, mode);
        var runs = factory.Services.GetRequiredService<IRunQueryService>();
        var available = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        var start = available.GetProperty("commands").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "START_ENCOUNTER");
        var state = runs.GetRun(runId).Value;
        using var initialized = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new { commandId = Guid.NewGuid(), type = "START_ENCOUNTER",
            expectedSequence = state.Sequence, expectedStep = state.Determinism.Step, payload = start.GetProperty("validPayload") });
        Assert.True(initialized.IsSuccessStatusCode, await initialized.Content.ReadAsStringAsync());
        for (var sample = 0; sample < 3; sample++)
        {
            state = runs.GetRun(runId).Value;
            var combat = state.GetActiveEncounter()!.Combat;
            var inputActor = combat.GetAllActors().Single(actor => actor.ControllerBinding.Kind == Core.Combat.Models.ControllerKind.Player).InstanceId;
            var commandId = Guid.NewGuid();
            var payload = new { commandId, type = "END_TURN", expectedSequence = state.Sequence,
                expectedStep = combat.Determinism.Step, payload = new { actorId = inputActor } };
            var watch = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/combats/{combat.CombatId}/commands") { Content = JsonContent.Create(payload) };
            request.Headers.Add("Prefer", "return=minimal");
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            var commandMs = watch.Elapsed.TotalMilliseconds;
            var after = runs.GetRun(runId).Value;
            using var session = await client.GetAsync($"/api/v1/runs/{runId}/session");
            var view = await session.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(session.IsSuccessStatusCode, view.GetRawText());
            var total = watch.Elapsed.TotalMilliseconds;
            Assert.Equal(CanonicalJson.ComputeHash(after), view.GetProperty("version").GetProperty("stateFingerprint").GetString());
            Assert.Equal(CanonicalJson.ComputeHash(after), CanonicalJson.ComputeHash(runs.GetRun(runId).Value));
            output.WriteLine($"setting={setting}; action={sample}; POST={commandMs:F2}ms; REST+session={total:F2}ms");
            output.WriteLine("Server-Timing: " + string.Join(", ", response.Headers.GetValues("Server-Timing")));
        }
    }
}
