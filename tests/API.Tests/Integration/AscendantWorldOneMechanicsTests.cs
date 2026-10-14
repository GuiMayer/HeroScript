using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Abstractions.Persistence;
using Core.Combat.Models;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class AscendantWorldOneMechanicsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task AllShippedCardEffects_ExecuteThroughProductionServicesAndLeaveInputsImmutable()
    {
        var (run, combat, runtime) = await Fixture();
        var executor = factory.Services.GetRequiredService<IEffectTriggerExecutor>();
        var compiler = new CardContentCompiler();
        var seeded = executor.Execute(Request(run, combat, "seed", null,
            compiler.Compile("w1_poison", runtime).Value.All<CardEffectComponentDefinition>().Single().Effect,
            compiler.Compile("w1_reserve", runtime).Value.All<CardEffectComponentDefinition>().Last().Effect));
        Assert.True(seeded.IsSuccess, seeded.IsFailure ? seeded.Error : null);
        combat = seeded.Value.State;
        var before = CanonicalJson.ComputeHash(new { run, combat });
        var root = Core.Config.ResourceProviderFactory.FindProjectRoot()!;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data/configs/ascendant/Resources/cards/world_one.json")));
        foreach (var entry in json.RootElement.EnumerateObject())
        {
            var definition = compiler.Compile(entry.Name, runtime).Value;
            var card = new EffectiveCardResolver().Resolve(definition, new CardInstanceState
            {
                CardInstanceId = Guid.Parse("10000000-0000-4000-8000-000000000001"), DefinitionId = entry.Name
            }).Value;
            var effects = card.All<CardEffectComponentDefinition>().OrderBy(c => c.Order).Select(c => c.Effect).ToArray();
            var request = Request(run, combat, entry.Name, card, effects) with { Tags = card.Tags.ToImmutableHashSet(StringComparer.Ordinal) };
            var result = executor.Execute(request);
            Assert.True(result.IsSuccess, result.IsFailure ? $"{entry.Name}: {result.Error}" : null);
            Assert.Equal(before, CanonicalJson.ComputeHash(new { run, combat }));
            Assert.Equal(CanonicalJson.ComputeHash(result.Value), CanonicalJson.ComputeHash(executor.Execute(request).Value));
            if (entry.Name is "w1_flurry" or "w1_barrage")
            {
                var facts = result.Value.Steps.SelectMany(step => step.RandomInputs).ToArray();
                Assert.Equal(entry.Name == "w1_flurry" ? 2 : 4, facts.Select(fact => fact.ScopeId).Distinct().Count());
            }
            if (entry.Name is "w1_release" or "w1_release_guard")
                Assert.Contains(result.Value.Records, record => record.Condensation != null);
        }
    }

    [Fact]
    public async Task EveryEnemyAbility_RemainsExecutableUnderFractionalDamageReduction()
    {
        var (run, combat, runtime) = await Fixture();
        var effects = factory.Services.GetRequiredService<IEffectTriggerExecutor>();
        var abilities = factory.Services.GetRequiredService<IAbilityExecutor>();
        var compiler = new CardContentCompiler();
        var root = Core.Config.ResourceProviderFactory.FindProjectRoot()!;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data/configs/ascendant/Resources/entities/world_one_enemies.json")));
        foreach (var entry in json.RootElement.EnumerateObject())
        {
            var entity = PersistentPlayerTransitions.Create("opponent_1", entry.Name, runtime).Value;
            var enemy = combat.GetActor("opponent_1")! with { DefinitionId = entry.Name, Components = entity.Components };
            // A separate fixture per definition, not a gameplay identity replacement.
            var state = combat with { Actors = combat.Actors.ToImmutableDictionary().SetItem(enemy.InstanceId, enemy) };
            var weakened = effects.Execute(Request(run, state, "weaken", null,
                compiler.Compile("w1_dampen", runtime).Value.All<CardEffectComponentDefinition>().First().Effect));
            Assert.True(weakened.IsSuccess, weakened.IsFailure ? weakened.Error : null);
            var ids = entry.Value.GetProperty("components").EnumerateArray()
                .Single(component => component.GetProperty("type").GetString() == "abilities")
                .GetProperty("abilityIds").EnumerateArray().Select(id => id.GetString()!);
            foreach (var ability in ids)
            {
                var result = abilities.Execute(new() { Run = run, Combat = weakened.Value.State, ActorId = enemy.InstanceId,
                    ActionId = ability, SelectedTargetIds = [run.PlayerEntityId] });
                Assert.True(result.IsSuccess, result.IsFailure ? $"{ability}: {result.Error}" : null);
            }
        }
    }

    private async Task<(RunState Run, CombatState Combat, ContentRuntime Runtime)> Fixture()
    {
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var player = "mechanics-" + Guid.NewGuid().ToString("N");
        var id = await game.StartRunAsync("ascendant", "ascendant_world_one_run", player, 250, "ascendant_world_one");
        var commands = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{id}/available-commands");
        var start = commands.GetProperty("commands").EnumerateArray().Single(c => c.GetProperty("type").GetString() == "START_ENCOUNTER");
        using var response = await client.PostAsJsonAsync($"/api/v1/runs/{id}/commands", new
        {
            commandId = Guid.NewGuid(), type = "START_ENCOUNTER", payload = start.GetProperty("validPayload"),
            expectedSequence = start.GetProperty("expectedSequence").GetInt32(), expectedStep = start.GetProperty("expectedStep").GetUInt64()
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var run = (await factory.Services.GetRequiredService<IRunCommitStore>().LoadLatestStateAsync(id))!;
        var runtime = factory.Services.GetRequiredService<IContentRuntimeResolver>().Resolve(run.Determinism.ContentRevision, "ascendant").Value;
        var combat = run.GetActiveEncounter()!.Combat;
        var actor = combat.GetActor(player)!;
        var stats = actor.Component<StatEntityComponentState>()!;
        actor = actor with { Components = actor.Components.ToImmutableDictionary().SetItem(stats.ComponentId,
            stats with { Values = stats.Values.ToImmutableDictionary().SetItem("critical_chance", 250) }) };
        actor = actor with { ResourceState = actor.ResourceState with { Resources = actor.ResourceState.Resources.ToDictionary(p => p.Key,
            p => p.Value.Set(p.Key == "health" ? 80 : p.Key == "block" ? 16 : p.Value.Current)) } };
        var target = combat.GetActor("opponent_1")!;
        target = target with { ResourceState = target.ResourceState with { Resources = target.ResourceState.Resources.ToDictionary(p => p.Key,
            p => p.Key == "health" ? ResourcePool.Materialize(p.Value.Definition, 1000, 1000) : p.Value) } };
        return (run, combat.ReplaceActor(actor).ReplaceActor(target), runtime);
    }

    private static EffectTriggerExecutionRequest Request(RunState run, CombatState combat, string id,
        EffectiveCardDefinition? card, params EffectDefinition[] effects) => new()
    {
        Run = run, Combat = combat, OwnerEntityId = run.PlayerEntityId, SourceEntityId = run.PlayerEntityId,
        ContentRevision = run.Determinism.ContentRevision, Card = card, SelectedTargetEntityIds = ["opponent_1"],
        Trigger = new() { TriggerId = id, Effects = effects }, Provenance = new() { Kind = EffectProvenanceKind.Card, SourceId = id }
    };
}
