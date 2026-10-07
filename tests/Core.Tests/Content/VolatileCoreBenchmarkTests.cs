using System.Diagnostics;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Xunit;
using Xunit.Abstractions;

namespace Core.Tests.Content;

public sealed class VolatileCoreBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CausalSelectionStaysBoundedWithGrowingActorRosters()
    {
        var fixture = await VolatileCoreSettingTests.Fixture();
        var definition = fixture.Runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", "core_cascade").Value;
        var upgraded = CardInstanceUpgradeTransitions.Apply(new() { DefinitionId = "core_strike" },
            CardBundleCompiler.Seal(definition, fixture.Runtime).Value, fixture.Runtime.Manifest.Revision).Value;
        var card = new EffectiveCardResolver().Resolve(
            new CardContentCompiler().Compile("core_strike", fixture.Runtime).Value, upgraded).Value;
        foreach (var count in new[] { 2, 20, 100 })
        {
            var actors = new Dictionary<string, Core.Combat.Models.CombatActorState>
            {
                ["player"] = fixture.Combat.GetActor("player")!
            };
            for (var i = 0; i < count; i++)
            {
                var id = $"enemy_{i:000}";
                var actor = fixture.Combat.GetActor("enemy_0")! with { InstanceId = id };
                actor = actor with { ResourceState = actor.ResourceState with { OwnerId = id } };
                actors[id] = actor.ApplyResourceMutation("setup", "health",
                    Core.Resources.ResourceMutationOperation.Set, 1).Value;
            }
            var state = fixture.Combat with { Actors = actors, ActorOrder = actors.Keys.ToArray() };
            var samples = new List<double>();
            for (var i = 0; i < 30; i++)
            {
                var timer = Stopwatch.StartNew();
                var result = fixture.Executor.Execute(VolatileCoreSettingTests.Request(fixture, card,
                    card.All<CardEffectComponentDefinition>()[0].Effect) with
                    { Combat = state, SelectedTargetEntityIds = ["enemy_000"] });
                samples.Add(timer.Elapsed.TotalMilliseconds);
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
                Assert.InRange(result.Value.Records.Count(record => record.ResourceOutcome != null &&
                    record.CalculationInfluenceId == null), 2, 4);
                Assert.Single(result.Value.Steps.Select(step => step.Identity!.ProcId).Distinct());
            }
            samples.Sort();
            output.WriteLine($"Pure executor targets={count}, n={samples.Count}, p95={samples[28]:F2}ms p99/max={samples[^1]:F2}ms");
        }
    }

    [Fact]
    public async Task CondensationRemainsOneProcAtGrowingPayloadStackCounts()
    {
        foreach (var preparations in new[] { 1, 24, 49 })
        {
            var fixture = await VolatileCoreSettingTests.Fixture();
            var compiler = new CardContentCompiler();
            var resolver = new EffectiveCardResolver();
            var charge = resolver.Resolve(compiler.Compile("core_charge_card", fixture.Runtime).Value,
                new CardInstanceState { DefinitionId = "core_charge_card" }).Value;
            var state = fixture.Combat;
            var run = fixture.Run;
            for (var i = 0; i < preparations; i++)
            {
                var prepared = fixture.Executor.Execute(VolatileCoreSettingTests.Request(fixture, charge,
                    charge.All<CardEffectComponentDefinition>()[0].Effect) with { Combat = state, Run = run });
                Assert.True(prepared.IsSuccess, prepared.IsFailure ? prepared.Error : null);
                state = prepared.Value.State;
                run = prepared.Value.Run!;
            }
            var release = resolver.Resolve(compiler.Compile("core_release", fixture.Runtime).Value,
                new CardInstanceState { DefinitionId = "core_release" }).Value;
            var samples = new List<double>();
            for (var i = 0; i < 30; i++)
            {
                var timer = Stopwatch.StartNew();
                var result = fixture.Executor.Execute(VolatileCoreSettingTests.Request(fixture, release,
                    release.All<CardEffectComponentDefinition>()[0].Effect) with { Combat = state, Run = run, SelectedTargetEntityIds = ["enemy_1"] });
                samples.Add(timer.Elapsed.TotalMilliseconds);
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
                Assert.Single(result.Value.Steps.Select(step => step.Identity!.ProcId).Distinct());
                Assert.Empty(result.Value.State.StatusEffects.GetValueOrDefault("player", []));
                Assert.Equal(preparations * 2, result.Value.Steps.First(step => step.Condensation != null)
                    .Condensation!.Selection.Sources.Sum(source => source.Stacks));
            }
            samples.Sort();
            output.WriteLine($"Pure executor stacks={preparations * 2}, n={samples.Count}, p95={samples[28]:F2}ms p99/max={samples[^1]:F2}ms");
        }
    }
}
