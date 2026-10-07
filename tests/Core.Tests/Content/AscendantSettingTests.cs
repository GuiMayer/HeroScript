using Core.Calculations;
using Core.Config;
using Core.Content;
using Core.Run.Content;
using Mods;
using Xunit;

namespace Core.Tests.Content;

public sealed class AscendantSettingTests
{
    [Fact]
    public async Task AscendantSetting_ComposesBaseAndOwnsAValidScalingGraph()
    {
        var compilation = await Compile();

        Assert.Collection(
            compilation.Packages,
            package => Assert.Equal("heroscript.base", package.PackageId),
            package => Assert.Equal("heroscript.ascendant", package.PackageId));
        var validation = new ContentGraphValidator().Validate(compilation.Bundle);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));

        var runtime = ContentRuntime.Create(compilation.Bundle);
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        var compiler = new CardContentCompiler();
        foreach (var cardId in new[]
                 {
                     "ascendant_strike", "ascendant_elemental_burst", "ascendant_critical_lance",
                     "ascendant_support_cascade", "ascendant_fracture", "ascendant_kinetic_guard"
                 })
        {
            var card = compiler.Compile(cardId, runtime.Value);
            Assert.True(card.IsSuccess, card.IsFailure ? $"{cardId}: {card.Error}" : null);
        }
    }

    [Fact]
    public async Task AscendantPipeline_AddsIncreasedValuesBeforeIndependentMultipliers()
    {
        var compilation = await Compile();
        var runtime = ContentRuntime.Create(compilation.Bundle).Value;
        var pipeline = runtime.GetDefinition<CalculationPipelineDefinition>(
            "calculation-pipelines", "ascendant_effect_amount").Value;
        var result = new CalculationEngine().Calculate(new CalculationRequest
        {
            CalculationId = "hybrid-scaling-proof",
            Channel = "effect_amount",
            BaseValue = 10,
            Influences =
            [
                Influence("added", "flat", 2f),
                Influence("increased-a", "increased", .2f),
                Influence("increased-b", "increased", .3f),
                Influence("elemental", "elemental", .5f),
                Influence("support", "more", 1.2f),
                Influence("critical", "critical", 2f)
            ]
        }, pipeline);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(65, result.Value.Value);
        Assert.Equal(12f, result.Value.Buckets.Single(bucket => bucket.BucketId == "flat").Output, 3);
        Assert.Equal(18f, result.Value.Buckets.Single(bucket => bucket.BucketId == "increased").Output, 3);
        Assert.Equal(27f, result.Value.Buckets.Single(bucket => bucket.BucketId == "elemental").Output, 3);
        Assert.Equal(32.4f, result.Value.Buckets.Single(bucket => bucket.BucketId == "more").Output, 3);
        Assert.Equal(64.8f, result.Value.Buckets.Single(bucket => bucket.BucketId == "critical").Output, 3);
    }

    private static CalculationInfluence Influence(string id, string bucket, float value) => new()
    {
        InfluenceId = id,
        SourceId = id,
        SourceKind = CalculationSourceKind.Card,
        Channel = "effect_amount",
        Bucket = bucket,
        Value = value
    };

    private static async Task<SettingCompilation> Compile()
    {
        var root = ResourceProviderFactory.FindProjectRoot();
        Assert.NotNull(root);
        var compilation = await new SettingCompiler([
                new DirectoryPackageProvider("shipped", Path.Combine(root!, "data", "configs"))
            ])
            .CompileAsync("ascendant");
        Assert.True(compilation.IsSuccess, compilation.IsFailure ? compilation.Error : null);
        return compilation.Value;
    }
}
