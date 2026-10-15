using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.Content;

public sealed class HeroAttributeScalingTests
{
    internal static async Task<(EffectTriggerExecutor Executor, EffectTriggerExecutionRequest Request, ActorAttributeProjectionService Projection, ContentRuntime Runtime)> Fixture(float armor, float chance = 0)
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(value => value.Resolve(runtime.Manifest.Revision, It.IsAny<string?>())).Returns(Result<ContentRuntime>.Success(runtime));
        var math = new MathEngine(Mock.Of<IConfigManager>(), new FormulaLoader(Mock.Of<IResourceLoader>()), NullLogger.Instance);
        var formulas = new RuntimeFormulaEvaluator(math, new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance, runtimes.Object);
        var engine = new CalculationEngine(formulas);
        var providers = new CompositeCalculationInfluenceProvider([new EntityStatInfluenceProvider(), new EntityResourceInfluenceProvider(),
            new RelicCalculationInfluenceProvider(formulas), new CardComponentInfluenceProvider(formulas)]);
        var request = EffectTransactionTests.Request(new EffectDefinition { Type = EffectType.DAMAGE, TargetResource = "focus", FlatValue = 100 });
        var actor = request.Combat.GetActor("hero")!;
        actor = actor with { Components = actor.Components.ToImmutableDictionary().SetItem("stats",
            new StatEntityComponentState { ComponentId = "stats", Values = new Dictionary<string, float> { ["critical_chance"] = chance } }) };
        var target = request.Combat.GetActor("enemy")!;
        target = target with { Components = target.Components.ToImmutableDictionary().SetItem("stats",
            new StatEntityComponentState { ComponentId = "stats", Values = new Dictionary<string, float> { ["armor"] = armor } }),
            ResourceState = new() { OwnerId = "enemy", Resources = new Dictionary<string, ResourcePool>
                { ["focus"] = ResourcePool.Materialize(new ResourceDefinition { ResourceId = "focus", DisplayName = "Focus" }, 2000, 2000) } } };
        var determinism = DeterministicContext.Create(123, runtime.Manifest.Revision);
        var run = new RunState { ConfigName = "ascendant", PlayerEntityId = "hero", Determinism = determinism,
            ResolvedMode = new() { Definition = runtime.GetDefinition<GameModeDefinition>("modes", "ascendant_world_one").Value } };
        request = request with { Run = run, ContentRevision = runtime.Manifest.Revision,
            Combat = request.Combat.ReplaceActor(actor).ReplaceActor(target) with { Determinism = determinism } };
        return (new(formulas, new ImmutableEffectProcessor(), runtimes.Object, engine, providers), request,
            new(runtimes.Object, formulas, engine, providers), runtime);
    }

    [Theory]
    [InlineData(-25, 100)] [InlineData(0, 100)] [InlineData(25, 80)] [InlineData(100, 50)] [InlineData(300, 25)]
    public async Task ArmorHasDiminishingReturnsForAnyResourceWithoutConsumption(float armor, float expected)
    {
        var fixture = await Fixture(armor);
        var before = CanonicalJson.ComputeHash(fixture.Request);
        var result = fixture.Executor.Execute(fixture.Request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(expected, result.Value.Steps[0].Calculation!.Value);
        Assert.Equal(2000 - expected, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(armor, result.Value.State.GetActor("enemy")!.Component<StatEntityComponentState>()!.Values["armor"]);
        Assert.Equal(before, CanonicalJson.ComputeHash(fixture.Request));
    }

    [Fact]
    public async Task DefaultBonusIsOneHundredPercentAndArmorPrecedesBlock()
    {
        var fixture = await Fixture(100, 200);
        var target = fixture.Request.Combat.GetActor("enemy")!;
        var block = ResourcePool.Materialize(new ResourceDefinition { ResourceId = "block", DisplayName = "Block" }, 25, 100);
        var request = fixture.Request with { Combat = fixture.Request.Combat.ReplaceActor(target with {
            ResourceState = target.ResourceState with { Resources = target.ResourceState.Resources.ToDictionary().Append(
                new KeyValuePair<string, ResourcePool>("block", block)).ToDictionary() } }) };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var step = result.Value.Steps[0];
        Assert.Equal(1, step.RandomInputs[0].Captures["bonus"].Value);
        Assert.Equal(125, step.Calculation!.Value); // 100 * (1 + 2 * 1) * .5 - 25
        Assert.Equal(0, result.Value.State.GetActor("enemy")!.GetResource("block")!.Current);
        Assert.Equal(100, result.Value.State.GetActor("enemy")!.Component<StatEntityComponentState>()!.Values["armor"]);
    }

    [Fact]
    public async Task AttributeProjectionIsReadOnlyAndUsesTheSameCurveAndDefaults()
    {
        var fixture = await Fixture(100, 150);
        var before = CanonicalJson.ComputeHash(fixture.Request);
        var projections = Enumerable.Range(0, 10).Select(_ => fixture.Projection.Project(fixture.Request.Run!, fixture.Request.Combat, "hero")).ToArray();
        Assert.All(projections, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(projections.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct());
        Assert.Equal(150, projections[0].Value.Attributes.Single(value => value.AttributeId == "critical_chance").EffectiveValue);
        Assert.Equal(100, projections[0].Value.Attributes.Single(value => value.AttributeId == "critical_damage").EffectiveValue);
        var summary = Assert.Single(projections[0].Value.CriticalRules);
        Assert.Equal(1, summary.GuaranteedTiers);
        Assert.Equal(50, summary.NextTierChancePercent);
        Assert.Equal(100, summary.BonusPerTierPercent);
        var target = fixture.Projection.Project(fixture.Request.Run!, fixture.Request.Combat, "enemy");
        Assert.True(target.IsSuccess, target.IsFailure ? target.Error : null);
        Assert.Equal(50, target.Value.Attributes.Single(value => value.AttributeId == "armor_reduction").EffectiveValue);
        Assert.All(target.Value.Attributes, attribute => Assert.True(attribute.Calculation.CaptureOnly));
        Assert.Equal(before, CanonicalJson.ComputeHash(fixture.Request));
    }

    [Theory]
    [InlineData(25, 20)] [InlineData(100, 50)] [InlineData(300, 75)]
    public async Task CurveUsesRevisionedConfigurableK(float k, float expected)
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var definition = runtime.GetDefinition<FormulaDefinition>("formulas", "W1_ARMOR_REMAINING_AMOUNT").Value;
        var math = new MathEngine(Mock.Of<IConfigManager>(), new FormulaLoader(Mock.Of<IResourceLoader>()), NullLogger.Instance);
        var before = CanonicalJson.ComputeHash(definition);
        var result = math.BuildFromDefinition("W1_ARMOR_REMAINING_AMOUNT", definition, 100,
            new() { ["K"] = k, ["bucket.contributions.sum"] = 100, ["bucket.input"] = 100 }).Build();
        Assert.Equal(expected, result, 3);
        Assert.Equal(before, CanonicalJson.ComputeHash(definition));
        Assert.Throws<ArgumentException>(() => math.BuildFromDefinition("W1_ARMOR_REMAINING_AMOUNT", definition, 100, new() { ["K"] = 0 }));
        var invalid = new FormulaDefinition { Params = new(definition.Params) { ["K"] = 0 }, Operations = definition.Operations,
            ParameterConstraints = definition.ParameterConstraints };
        Assert.NotEmpty(FormulaDefinitionValidator.Validate(invalid));
    }

    [Fact]
    public async Task ArmorBypassIsIndependentFromBlockBypass()
    {
        var fixture = await Fixture(100);
        var effect = fixture.Request.Trigger.Effects[0] with { RuleOverrides = [new() { Armor = false }], Tags = ["piercing"] };
        var request = fixture.Request with { Trigger = fixture.Request.Trigger with { Effects = [effect] } };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(100, result.Value.Steps[0].Calculation!.Value);
        Assert.False(result.Value.Steps[0].ResolutionPolicy!.ArmorEnabled);
    }

    [Fact]
    public async Task SubtractionRequiresExplicitDamageSemanticsToInheritRules()
    {
        var fixture = await Fixture(100);
        var effect = fixture.Request.Trigger.Effects[0] with { Type = EffectType.MODIFY_RESOURCE,
            Operation = ResourceEffectOperation.SUBTRACT, Tags = ["effect.damage"] };
        var request = fixture.Request with { Trigger = fixture.Request.Trigger with { Effects = [effect] } };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(50, result.Value.Steps[0].Calculation!.Value);
        effect = effect with { Tags = [] };
        result = fixture.Executor.Execute(request with { Trigger = request.Trigger with { Effects = [effect] } });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Null(result.Value.Steps[0].ResolutionPolicy);
    }
}
