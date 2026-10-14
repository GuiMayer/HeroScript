using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using API.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using Core.Calculations;
using Core.Combat;
using Core.Effects;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration;

public sealed class HeroAttributesProjectionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AttributesFollowTheActiveAuthorityAndQueriesNeverAdvanceTheRun()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        const string player = "attributes-pilot";
        var runId = await game.StartRunAsync("ascendant", "ascendant_world_one_run", player, 154, "ascendant_world_one");
        var before = await client.GetStringAsync($"/api/v1/runs/{runId}");
        var readings = new List<string>();
        var durations = new List<double>();
        for (var index = 0; index < 5; index++)
        {
            var timer = Stopwatch.StartNew();
            readings.Add(await client.GetStringAsync($"/api/v1/runs/{runId}/character-attributes"));
            durations.Add(timer.Elapsed.TotalMilliseconds);
        }
        output.WriteLine($"attribute reads: {string.Join(", ", durations.Select(value => value.ToString("F1")))}ms; bytes={readings[0].Length}");
        Assert.All(durations.Skip(1), value => Assert.True(value < 1000, $"Attribute read took {value}ms"));
        Assert.Single(readings.Distinct());
        Assert.True(readings[0].Length < 16000);
        using var persistent = JsonDocument.Parse(readings[0]);
        Assert.Equal("PersistentPlayer", persistent.RootElement.GetProperty("authority").GetString());
        var values = persistent.RootElement.GetProperty("attributes").EnumerateArray().ToDictionary(value => value.GetProperty("attributeId").GetString()!);
        Assert.Equal(15, values["critical_chance"].GetProperty("effectiveValue").GetSingle());
        Assert.Equal(100, values["critical_damage"].GetProperty("effectiveValue").GetSingle());
        Assert.Equal(0, values["armor"].GetProperty("effectiveValue").GetSingle());
        Assert.All(values.Values, value => Assert.False(value.TryGetProperty("calculation", out _)));
        Assert.Equal(before, await client.GetStringAsync($"/api/v1/runs/{runId}"));

        var commands = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        var enter = commands.GetProperty("commands").EnumerateArray().Single(command => command.GetProperty("type").GetString() == "START_ENCOUNTER");
        using var entered = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new {
            commandId = Guid.NewGuid(), type = "START_ENCOUNTER", payload = enter.GetProperty("validPayload"),
            expectedSequence = enter.GetProperty("expectedSequence").GetInt32(), expectedStep = enter.GetProperty("expectedStep").GetUInt64() });
        Assert.True(entered.IsSuccessStatusCode, await entered.Content.ReadAsStringAsync());
        var run = await game.GetRunStateAsync(runId);
        var combatId = run.GetProperty("activeEncounterId").GetGuid();
        var runBeforeRead = await client.GetStringAsync($"/api/v1/runs/{runId}");
        var combatBeforeRead = await client.GetStringAsync($"/api/v1/combats/{combatId}");
        var active = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/character-attributes");
        Assert.Equal("ActiveEncounter", active.GetProperty("authority").GetString());
        Assert.Equal(run.GetProperty("sequence").GetInt32(), active.GetProperty("runSequence").GetInt32());
        Assert.Equal(combatId, active.GetProperty("combatId").GetGuid());
        var actor = await client.GetFromJsonAsync<JsonElement>($"/api/v1/combats/{combatId}/actors/{player}/attributes");
        Assert.Equal(active.GetRawText(), actor.GetRawText());
        var loaded = factory.Services.GetRequiredService<ICombatRunCoordinator>().GetCombatState(combatId).Value;
        var temporary = factory.Services.GetRequiredService<IEffectTriggerExecutor>().Execute(new()
        {
            Run = loaded.RunState, Combat = loaded.CombatState, OwnerEntityId = player, SourceEntityId = player,
            ContentRevision = loaded.RunState.Determinism.ContentRevision,
            Trigger = new() { TriggerId = "projection-buffs", Effects = [
                new() { Type = EffectType.APPLY_STATUS, Target = EffectTarget.SELF, StatusId = "w1_focus", StatusStacks = 1 },
                new() { Type = EffectType.APPLY_STATUS, Target = EffectTarget.SELF, StatusId = "w1_precision", StatusStacks = 1 }] }
        });
        Assert.True(temporary.IsSuccess, temporary.IsFailure ? temporary.Error : null);
        var buffed = factory.Services.GetRequiredService<IActorAttributeProjectionService>().Project(loaded.RunState, temporary.Value.State, player);
        Assert.True(buffed.IsSuccess, buffed.IsFailure ? buffed.Error : null);
        var chance = buffed.Value.Attributes.Single(value => value.AttributeId == "critical_chance");
        Assert.Equal(15, chance.BaseValue);
        Assert.Equal(135, chance.EffectiveValue);
        Assert.Equal(1, Assert.Single(buffed.Value.CriticalRules).GuaranteedTiers);
        Assert.Equal(35, buffed.Value.CriticalRules[0].NextTierChancePercent);
        var combat = await game.GetCombatStateAsync(combatId);
        var target = combat.GetProperty("actors").EnumerateArray().First(value => value.GetProperty("instanceId").GetString() != player).GetProperty("instanceId").GetString();
        var hand = await client.GetFromJsonAsync<JsonElement>($"/api/v1/combats/{combatId}/cards/evaluations?actorId={player}&targetIds={target}");
        Assert.Contains(hand.GetProperty("cards").EnumerateArray(), card => card.GetProperty("previewSteps").EnumerateArray()
            .Any(step => step.TryGetProperty("resolutionPolicy", out var policy) && policy.GetProperty("criticalEnabled").GetBoolean()));
        Assert.Equal(runBeforeRead, await client.GetStringAsync($"/api/v1/runs/{runId}"));
        Assert.Equal(combatBeforeRead, await client.GetStringAsync($"/api/v1/combats/{combatId}"));
    }
}
