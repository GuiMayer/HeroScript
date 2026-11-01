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
        { base.ConfigureWebHost(builder); builder.UseSetting("Performance:DiagnosticsEnabled", Environment.GetEnvironmentVariable("HEROSCRIPT_POST_DIAGNOSTICS") ?? "true"); }
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
        var runs = factory.Services.GetRequiredService<IRunQueryService>();
        async Task<Guid> StartInitializedRun(int batch)
        {
            var id = await game.StartRunAsync(setting, definition, player, 541 + (ulong)batch, mode);
            var available = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{id}/available-commands");
            var start = available.GetProperty("commands").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "START_ENCOUNTER");
            var initial = runs.GetRun(id).Value;
            using var initialized = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new { commandId = DeterministicId.Create(541 + (ulong)batch, 0, $"post100:{setting}:start"), type = "START_ENCOUNTER",
                expectedSequence = initial.Sequence, expectedStep = initial.Determinism.Step, payload = start.GetProperty("validPayload") });
            Assert.True(initialized.IsSuccessStatusCode, await initialized.Content.ReadAsStringAsync());
            return id;
        }
        var runId = await StartInitializedRun(0);
        var state = runs.GetRun(runId).Value;
        var samples = int.TryParse(Environment.GetEnvironmentVariable("HEROSCRIPT_POST_SAMPLES"), out var requested) ? Math.Clamp(requested, 1, 1000) : 3;
        var postTimes = new List<double>();
        for (var sample = 0; sample < samples; sample++)
        {
            // Distribute long campaigns over valid encounters instead of ending turns after death.
            if (sample > 0 && sample % 3 == 0) runId = await StartInitializedRun(sample / 3);
            state = runs.GetRun(runId).Value;
            var combat = state.GetActiveEncounter()!.Combat;
            var inputActor = combat.GetAllActors().Single(actor => actor.ControllerBinding.Kind == Core.Combat.Models.ControllerKind.Player).InstanceId;
            var commandId = DeterministicId.Create(541 + (ulong)(sample / 3), (ulong)sample, $"post100:{setting}:end-turn");
            var payload = new { commandId, type = "END_TURN", expectedSequence = state.Sequence,
                expectedStep = combat.Determinism.Step, payload = new { actorId = inputActor } };
            var watch = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/combats/{combat.CombatId}/commands") { Content = JsonContent.Create(payload) };
            if (Environment.GetEnvironmentVariable("HEROSCRIPT_POST_PROFILE") != "default")
                request.Headers.Add("Prefer", "return=minimal");
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            var commandMs = watch.Elapsed.TotalMilliseconds;
            postTimes.Add(commandMs);
            output.WriteLine($"POST_BYTES setting={setting}; sample={sample}; response={System.Text.Encoding.UTF8.GetByteCount(body)}");
            var after = runs.GetRun(runId).Value;
            using var session = await client.GetAsync($"/api/v1/runs/{runId}/session");
            var view = await session.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(session.IsSuccessStatusCode, view.GetRawText());
            var total = watch.Elapsed.TotalMilliseconds;
            Assert.Equal(CanonicalJson.ComputeHash(after), view.GetProperty("version").GetProperty("stateFingerprint").GetString());
            Assert.Equal(CanonicalJson.ComputeHash(after), CanonicalJson.ComputeHash(runs.GetRun(runId).Value));
            output.WriteLine($"setting={setting}; action={sample}; POST={commandMs:F2}ms; REST+session={total:F2}ms");
            if (response.Headers.TryGetValues("Server-Timing", out var timings)) output.WriteLine("Server-Timing: " + string.Join(", ", timings));
            if (response.Headers.TryGetValues("X-HeroScript-Work", out var work)) output.WriteLine("WORK: " + string.Join("", work));
        }
        var ordered = postTimes.Order().ToArray();
        double Percentile(double p) => ordered[(int)Math.Ceiling(p * ordered.Length) - 1];
        output.WriteLine($"POST_SUMMARY setting={setting}; n={ordered.Length}; p50={Percentile(.50):F2}; p95={Percentile(.95):F2}; p99={Percentile(.99):F2}; max={ordered[^1]:F2}ms");
        var configuredBudget = Environment.GetEnvironmentVariable("HEROSCRIPT_POST_BUDGET_MS");
        if (configuredBudget != null)
        {
            Assert.True(double.TryParse(configuredBudget, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var budget) && double.IsFinite(budget) && budget > 0,
                "HEROSCRIPT_POST_BUDGET_MS must be a finite positive duration");
            Assert.All(postTimes, elapsed => Assert.True(elapsed < budget, $"{setting}: POST {elapsed:F2}ms >= {budget:F2}ms"));
        }

    }
}
