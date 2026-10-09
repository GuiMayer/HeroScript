using Core.Calculations;
using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Logging;
using Core.Math;
using Core.Run;
using Core.Run.Content;
using Core.Tests.Effects;
using Mods;
using Moq;
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

    [Theory]
    [InlineData(false, 150f, 21f, 31f)]
    [InlineData(true, 150f, 24f, 37f)]
    [InlineData(true, 250f, 37f, 50f)]
    public async Task ShippedCriticalCardUsesCapturedStatsAndPermanentUpgradeOnlyOnce(
        bool upgrade, float chance, float lower, float higher)
    {
        var runtime = ContentRuntime.Create((await Compile()).Bundle).Value;
        var mode = runtime.GetDefinition<GameModeDefinition>("modes", "ascendant_showcase").Value;
        var run = new RunState { PlayerEntityId = "hero", ConfigName = "ascendant",
            Determinism = DeterministicContext.Create(123, runtime.Manifest.Revision),
            ResolvedMode = new() { Definition = mode } };
        var compiler = new CardContentCompiler();
        var compiled = compiler.Compile("ascendant_critical_lance", runtime).Value;
        var instance = new CardInstanceState { CardInstanceId = Guid.Parse("10000000-0000-4000-8000-000000000001"),
            DefinitionId = compiled.CardId };
        if (upgrade) instance = CardInstanceUpgradeTransitions.Apply(instance,
            runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", "ascendant_critical_calibration").Value,
            runtime.Manifest.Revision).Value;
        var card = new EffectiveCardResolver().Resolve(compiled, instance).Value;
        var effect = card.All<CardEffectComponentDefinition>().Single().Effect with { TargetResource = "focus" };
        var request = EffectTransactionTests.Request(effect);
        var actor = request.Combat.GetActor("hero")!;
        var stats = PersistentPlayerTransitions.Create("hero", "ascendant_operator", runtime).Value.Component<StatEntityComponentState>()!;
        actor = actor with { Components = actor.Components.ToImmutableDictionary().SetItem(stats.ComponentId,
            stats with { Values = stats.Values.ToImmutableDictionary().SetItem("critical_chance", chance) }) };
        request = request with { Combat = request.Combat.ReplaceActor(actor), Run = run, Card = card,
            ContentRevision = runtime.Manifest.Revision };
        var resolver = new Mock<IContentRuntimeResolver>();
        resolver.Setup(service => service.Resolve(runtime.Manifest.Revision, "ascendant")).Returns(Result<ContentRuntime>.Success(runtime));
        var formulas = new RuntimeFormulaEvaluator(Mock.Of<IMathEngine>(), new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance);
        var calculations = new CalculationEngine(formulas);
        var executor = new EffectTriggerExecutor(formulas, new ImmutableEffectProcessor(), resolver.Object, calculations,
            new CompositeCalculationInfluenceProvider([new CardComponentInfluenceProvider(formulas), new EntityStatInfluenceProvider(),
                new EntityResourceInfluenceProvider(), new GameModeCalculationInfluenceProvider(formulas)]));
        var before = CanonicalJson.ComputeHash(request);
        var result = executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var step = result.Value.Steps[0];
        var fact = Assert.Single(step.RandomInputs);
        Assert.Equal(chance, fact.Captures["chance"].Value);
        Assert.Equal(upgrade ? 1.15f : .9f, fact.Captures["bonus"].Value, 4);
        Assert.Equal(fact.Success ? higher : lower, step.Calculation!.Value);
        var alternatives = RandomOutcomePreviewProjector.Project(result.Value.Steps, runtime, calculations).Impacts[0].Alternatives;
        Assert.Equal(new[] { lower, higher }, alternatives.Select(item => item.Calculation!.Value));
        Assert.Equal(before, CanonicalJson.ComputeHash(request));

        // Two profiles share a channel; implicit effects use the authored default, not arbitrary ordering.
        var normal = compiler.Compile("ascendant_strike", runtime).Value.All<CardEffectComponentDefinition>().Single().Effect;
        Assert.Equal("ascendant_effect_amount", CalculationResolver.ResolvePipeline(normal, run, runtime).Value.PipelineId);
        Assert.True(CalculationResolver.ResolvePipeline(normal, run with { ResolvedMode = new()
            { Definition = mode with { DefaultCalculationPipelines = ImmutableSortedDictionary<string, string>.Empty } } }, runtime).IsFailure);
        foreach (var invalidDefault in new[] { "", "signed_resource_delta", "default_effect_amount" })
            Assert.True(CalculationResolver.ResolvePipeline(normal, run with { ResolvedMode = new()
                { Definition = mode with { DefaultCalculationPipelines = mode.DefaultCalculationPipelines.SetItem("effect_amount", invalidDefault) } } }, runtime).IsFailure);
    }

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
