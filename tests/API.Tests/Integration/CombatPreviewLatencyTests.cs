using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Abstractions.Persistence;
using Core.Caching;
using Core.Combat.LegalActions;
using Core.Determinism;
using Core.Run.Content;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

public sealed class CombatPreviewLatencyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RepeatedQueriesReuseExactImmutablePreviews_AndColdWarmReadsLeaveRunUnchanged()
    {
        // New in-process host and store: never reads or writes the player's save.
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var player = "latency-" + Guid.NewGuid().ToString("N");
        var id = await game.StartRunAsync("ascendant", "ascendant_world_one_run", player, 250, "ascendant_world_one");
        var commands = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{id}/available-commands");
        var start = commands.GetProperty("commands").EnumerateArray().Single(c => c.GetProperty("type").GetString() == "START_ENCOUNTER");
        using var response = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
        {
            commandId = Guid.NewGuid(), type = "START_ENCOUNTER", payload = start.GetProperty("validPayload"),
            expectedSequence = start.GetProperty("expectedSequence").GetInt32(), expectedStep = start.GetProperty("expectedStep").GetUInt64()
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var store = factory.Services.GetRequiredService<IRunCommitStore>();
        var run = (await store.LoadLatestStateAsync(id))!;
        var before = CanonicalJson.ComputeHash(run);
        var combatId = run.ActiveEncounterId!.Value;
        var legalCache = (ICacheService)factory.Services.GetRequiredService<ILegalActionResolver>();
        var inspectionCache = (ICacheService)factory.Services.GetRequiredService<ICardInspectionService>();
        var registry = factory.Services.GetRequiredService<ICacheCoordinator>();
        var publicationCache = (ICacheService)factory.Services.GetRequiredService<Core.Content.IContentPublicationService>();
        Assert.Contains(publicationCache.CacheName, registry.GetCacheNames());
        Assert.True(publicationCache.GetStats().Hits > 0);
        var typedCache = factory.Services.GetRequiredService<Core.Content.TypedContentDefinitionCache>();
        Assert.Contains(typedCache.CacheName, registry.GetCacheNames());
        Assert.True(typedCache.GetStats().Hits > 0);
        Assert.Contains(legalCache.CacheName, registry.GetCacheNames());
        Assert.Contains(inspectionCache.CacheName, registry.GetCacheNames());
        legalCache.Invalidate(); inspectionCache.Invalidate();

        async Task<(JsonElement Data, double Ms)> Read(string route)
        {
            var timer = Stopwatch.StartNew();
            using var result = await client.GetAsync(route);
            Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
            var json = await result.Content.ReadFromJsonAsync<JsonElement>();
            return (json, timer.Elapsed.TotalMilliseconds);
        }
        var route = $"/api/v1/combats/{combatId}/legal-actions?actorId={player}";
        var cold = await Read(route);
        Assert.NotEmpty(cold.Data.GetProperty("candidates").EnumerateArray());
        var warm = await Read(route);
        Assert.Equal(CanonicalJson.ComputeHash(cold.Data), CanonicalJson.ComputeHash(warm.Data));
        Assert.True(legalCache.GetStats().Hits > 0);

        var targets = cold.Data.GetProperty("candidates").EnumerateArray()
            .First(c => c.GetProperty("source").GetString() == "Card").GetProperty("command").GetProperty("targetIds");
        var query = string.Join("", targets.EnumerateArray().Select(t => "&targetIds=" + Uri.EscapeDataString(t.GetString()!)));
        var inspectionRoute = $"/api/v1/combats/{combatId}/cards/evaluations?actorId={player}{query}";
        var evaluationHits = legalCache.GetStats().Hits;
        var coldInspection = await Read(inspectionRoute);
        var warmInspection = await Read(inspectionRoute);
        Assert.Equal(CanonicalJson.ComputeHash(coldInspection.Data), CanonicalJson.ComputeHash(warmInspection.Data));
        Assert.True(inspectionCache.GetStats().Hits > 0);
        Assert.True(legalCache.GetStats().Hits > evaluationHits); // Inspection reused the exact legality/preview executor result.
        Assert.Equal(before, CanonicalJson.ComputeHash((await store.LoadLatestStateAsync(id))!));
        Assert.InRange(legalCache.GetStats().Count, 1, legalCache.GetStats().Capacity);
        Assert.InRange(inspectionCache.GetStats().Count, 1, inspectionCache.GetStats().Capacity);

        legalCache.Invalidate(); inspectionCache.Invalidate(); typedCache.Invalidate();
        var recomputed = await Read(route);
        Assert.Equal(CanonicalJson.ComputeHash(cold.Data), CanonicalJson.ComputeHash(recomputed.Data));
        Assert.Equal(before, CanonicalJson.ComputeHash((await store.LoadLatestStateAsync(id))!));
        output.WriteLine($"Legal actions: cold={cold.Ms:F1}ms warm={warm.Ms:F1}ms; inspection: cold={coldInspection.Ms:F1}ms warm={warmInspection.Ms:F1}ms");
        var retainedPublications = publicationCache.GetStats().Count;
        Assert.True(retainedPublications > 0);
        registry.InvalidateAll();
        Assert.Equal(retainedPublications, publicationCache.GetStats().Count);
        Assert.True(registry.InvalidateCache(publicationCache.CacheName));
        Assert.Equal(0, publicationCache.GetStats().Count);
    }
}
