using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class CardPaymentJourneyTests
{
    [Fact]
    public async Task HandAndDiscardPaymentsUseTheRealGatewayAndReadOnlyVersionedPreviews()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync("payment-lab", "payment_lab_run", "player", 51307, "payment_lab");
        var combatId = await game.StartCombatAsync("player", ["enemy_0"], runId: runId, heroDefinitionId: "core_adept", enemyDefinitionId: "payment_target");
        var fee = await game.GetPlayableCardInstanceIdAsync(runId, "core_strike");
        var session = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/session");
        Assert.Contains(session.GetProperty("cardPresentations").GetProperty("cards").EnumerateArray(), card =>
            card.GetProperty("identity").GetProperty("definitionId").GetString() == "payment_hand" && card.GetProperty("hasCardPayments").GetBoolean());
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        var ordinal = 0;
        foreach (var definition in new[] { "payment_hand", "payment_spent" })
        {
            var cardId = await game.GetPlayableCardInstanceIdAsync(runId, definition);
            var run = await game.GetRunStateAsync(runId); var combat = await game.GetCombatStateAsync(combatId);
            var input = CanonicalJson.ComputeHash(reads.GetRun(runId).Value);
            var command = new { actorId = "player", actionType = "PLAY_CARD", cardInstanceId = cardId, targetIds = new[] { "enemy_0" },
                paymentSelections = new[] { new { componentId = "cost", paymentId = "offer", cardInstanceIds = new[] { fee } } } };
            var version = new { expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = combat.GetProperty("step").GetUInt64(), command };
            using var response = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/cards/payment-preview", version);
            var body = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, body);
            var preview = JsonDocument.Parse(body).RootElement.Clone();
            Assert.True(preview.GetProperty("isLegal").GetBoolean(), body);
            Assert.False(preview.GetProperty("evaluation").TryGetProperty("zonePayments", out _));
            Assert.All(preview.GetProperty("applications").EnumerateArray(), application =>
                Assert.Empty(application.GetProperty("cardZoneSteps").EnumerateArray()));
            Assert.Equal(input, CanonicalJson.ComputeHash(reads.GetRun(runId).Value));
            using var stale = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/cards/payment-preview", new { expectedSequence = version.expectedSequence - 1, version.expectedStep, command });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var envelope = new { commandId = Guid.Parse($"80000000-0000-4000-8000-{++ordinal:000000000000}"), type = "PLAY_CARD", version.expectedSequence, version.expectedStep, payload = command };
            using var committed = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", envelope);
            Assert.True(committed.IsSuccessStatusCode, await committed.Content.ReadAsStringAsync());
            var after = CanonicalJson.ComputeHash(reads.GetRun(runId).Value);
            using var duplicate = await client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", envelope);
            Assert.True(duplicate.IsSuccessStatusCode, await duplicate.Content.ReadAsStringAsync());
            Assert.Equal(after, CanonicalJson.ComputeHash(reads.GetRun(runId).Value));
        }
        var final = reads.GetRun(runId).Value;
        Assert.Contains(fee, final.Deck.Topology.GetZone(new() { ZoneId = "exhaust", OwnerId = "$run" })!.InstanceIds);
        Assert.Equal(5, final.Deck.Topology.Instances.Count);
    }
}
