using Core.Config;
using Core.Content;
using Core.Effects;
using Core.Run.Content;
using Mods;
using Xunit;

namespace Core.Tests.Content;

public sealed class MechanicsLabContentTests
{
    [Fact]
    public async Task DefaultSetting_CompilesEveryMechanicsLabCard()
    {
        var runtime = await CompileDefaultRuntime();
        var compiler = new CardContentCompiler();
        var cardIds = new[]
        {
            "venom_cut", "battle_focus", "expose_weakness", "chain_lightning",
            "execution", "tactical_recall", "conjure_spark", "fading_guard",
            "equilibrium", "piercing_ray", "cleanse", "prismatic_burst",
            "cull_weakest", "renewal"
        };

        foreach (var cardId in cardIds)
        {
            var compiled = compiler.Compile(cardId, runtime);
            Assert.True(compiled.IsSuccess, compiled.IsFailure ? $"{cardId}: {compiled.Error}" : null);
            Assert.NotEmpty(compiled.Value.Fingerprint);
            Assert.Single(compiled.Value.Components.OfType<CardDispositionComponentDefinition>());
        }
    }

    [Fact]
    public async Task MechanicsLab_ExercisesDifferentGenericMechanisms()
    {
        var runtime = await CompileDefaultRuntime();
        var compiler = new CardContentCompiler();

        var chain = compiler.Compile("chain_lightning", runtime).Value;
        Assert.Equal(EffectTarget.ALL_ENEMIES,
            Assert.Single(chain.Components.OfType<CardTargetingComponentDefinition>()).Target);

        var prism = compiler.Compile("prismatic_burst", runtime).Value;
        Assert.Equal(2,
            Assert.Single(prism.Components.OfType<CardCostComponentDefinition>()).Costs.AlternativeCosts.Count);

        var recall = compiler.Compile("tactical_recall", runtime).Value;
        var zoneEffect = Assert.Single(recall.Components.OfType<CardEffectComponentDefinition>()).Effect;
        Assert.Equal(EffectType.CARD_ZONE_FLOW, zoneEffect.Type);
        Assert.Equal("effect.draw", zoneEffect.CardZoneFlowId);
        Assert.Equal(2, zoneEffect.CardCount);

        var execution = compiler.Compile("execution", runtime).Value;
        Assert.Contains(execution.Components.OfType<CardConditionComponentDefinition>(),
            condition => condition.Expression.Contains("target.resources.health.percent", StringComparison.Ordinal));

        var piercing = compiler.Compile("piercing_ray", runtime).Value;
        Assert.Contains(
            Assert.Single(piercing.Components.OfType<CardEffectComponentDefinition>()).Effect.Tags,
            tag => tag == "piercing");

        var retained = compiler.Compile("equilibrium", runtime).Value;
        Assert.Contains("retain", retained.Tags);
        var ethereal = compiler.Compile("fading_guard", runtime).Value;
        Assert.Contains("ethereal", ethereal.Tags);
    }

    private static async Task<ContentRuntime> CompileDefaultRuntime()
    {
        var root = ResourceProviderFactory.FindProjectRoot();
        Assert.NotNull(root);
        var compilation = await new SettingCompiler([
                new DirectoryPackageProvider("base-game", Path.Combine(root!, "data", "configs"))
            ])
            .CompileAsync("default");
        Assert.True(compilation.IsSuccess, compilation.IsFailure ? compilation.Error : null);
        var runtime = ContentRuntime.Create(compilation.Value.Bundle);
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        return runtime.Value;
    }
}
