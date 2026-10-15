using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Determinism;
using Core.Run;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

public sealed class RunCreationLatencyTests(ITestOutputHelper output)
{
    private sealed class TimedFactory : TestWebApplicationFactory
    {
        public TimedFactory() { }
        public TimedFactory(string root) : base(root) { }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        { base.ConfigureWebHost(builder); builder.UseSetting("Performance:DiagnosticsEnabled", "true"); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColdAndWarmCreationPreserveInitialSnapshotAndExposeCompleteInteractionCost(bool restartPublishedHost)
    {
        string? root = null;
        if (restartPublishedHost)
        {
            using var initialHost = new TestWebApplicationFactory();
            using var initialClient = initialHost.CreateClient();
            using var catalog = await initialClient.GetAsync("/api/v1/content/settings");
            catalog.EnsureSuccessStatusCode();
            root = initialHost.PersistenceRoot;
        }
        using var factory = root == null ? new TimedFactory() : new TimedFactory(root);
        factory.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        using var client = factory.CreateClient();
        // Match the Godot launch path; listing revisions would warm the publication cache.
        var catalogResponse = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/settings");
        var revision = catalogResponse.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("settingId").GetString() == "ascendant")
            .GetProperty("currentRevision").GetString()!;
        var runs = factory.Services.GetRequiredService<IRunQueryService>();
        for (var sample = 0; sample < 2; sample++)
        {
            var watch = Stopwatch.StartNew();
            using var response = await client.PostAsJsonAsync("/api/v1/runs", new
            {
                settingId = "ascendant", runDefinitionId = "ascendant_world_one_run", playerEntityId = $"creation-probe-{sample}",
                seed = 154UL, contentRevision = revision, modeId = "ascendant_world_one"
            });
            var initial = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.IsSuccessStatusCode, initial.GetRawText());
            var creationMs = watch.Elapsed.TotalMilliseconds;
            var id = initial.GetProperty("runId").GetGuid();
            var state = runs.GetRun(id).Value;
            var hash = CanonicalJson.ComputeHash(state);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                CanonicalJson.Serialize(state)))).ToLowerInvariant(), hash);
            var read = Stopwatch.StartNew();
            using var sessionResponse = await client.GetAsync($"/api/v1/runs/{id}/session");
            var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(sessionResponse.IsSuccessStatusCode, session.GetRawText());
            var readMs = read.Elapsed.TotalMilliseconds;
            Assert.Equal(hash, session.GetProperty("version").GetProperty("stateFingerprint").GetString());
            Assert.Equal(hash, CanonicalJson.ComputeHash(runs.GetRun(id).Value));
            Assert.Equal(revision, state.Determinism.ContentRevision);
            Assert.Null(state.ActiveEncounterId);
            output.WriteLine($"creation persistedHost={restartPublishedHost}; sample={sample}; POST={creationMs:F2}ms; session={readMs:F2}ms; total={creationMs + readMs:F2}ms");
            output.WriteLine("creation timing: " + string.Join(", ", response.Headers.GetValues("Server-Timing")));
            var limit = Environment.GetEnvironmentVariable("HEROSCRIPT_INTERACTION_BUDGET_MS");
            if (limit != null)
            {
                Assert.True(double.TryParse(limit, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var budget) && double.IsFinite(budget) && budget > 0,
                    "HEROSCRIPT_INTERACTION_BUDGET_MS must be a positive millisecond budget");
                Assert.True(creationMs + readMs < budget,
                    $"Creation persistedHost={restartPublishedHost}, sample={sample}: {creationMs + readMs:F2}ms exceeds {budget:F2}ms");
            }
        }
    }
}
