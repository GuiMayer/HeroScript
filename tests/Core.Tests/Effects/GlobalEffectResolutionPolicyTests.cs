using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Logging;
using Core.Math;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class GlobalEffectResolutionPolicyTests
{
    private static (MultiTierCriticalTests.TestFixture Fixture, EffectResolutionPolicyDefinition Policy) Create(float chance = 150, float bonus = 1)
    {
        var original = MultiTierCriticalTests.Fixture();
        var profiles = original.Runtime.GetDefinitions("calculation-pipelines").ToDictionary(pair => pair.Key,
            pair => original.Runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", pair.Key).Value);
        profiles["normal"] = profiles["critical"] with { PipelineId = "normal",
            Buckets = profiles["critical"].Buckets.Where(bucket => bucket.BucketId != "critical").ToArray() };
        var policy = new EffectResolutionPolicyDefinition { PolicyId = "global", Rules = [new() { RuleId = "direct",
            RequiredTags = ["effect.damage"], ExcludedTags = ["residual"], ExcludedChannels = ["plain"],
            AmountPipelineId = "critical", NonCriticalPipelineId = "normal", CriticalStageId = "impact",
            CriticalInput = MultiTierCriticalTests.Damage(chance).RandomInputs[0] with { Probability =
                MultiTierCriticalTests.Damage(chance).RandomInputs[0].Probability! with { Captures =
                    MultiTierCriticalTests.Damage(chance).RandomInputs[0].Probability!.Captures.SetItem("bonus",
                        new() { FlatValue = bonus, Channel = "bonus", PipelineId = "bonus", UnitId = "factor" }) } } }] };
        var bundle = EffectSequenceBudgetTests.Bundle(profiles);
        var artifacts = bundle.Artifacts.Add("effect-resolution-policies/catalog.json", JsonSerializer.SerializeToElement(
            new Dictionary<string, EffectResolutionPolicyDefinition> { [policy.PolicyId] = policy }));
        bundle = bundle with { Artifacts = artifacts, Manifest = bundle.Manifest with { Artifacts = artifacts.Select(pair =>
            new ContentArtifactManifest { Path = pair.Key, Kind = pair.Key.Split('/')[0], DefinitionCount = pair.Value.EnumerateObject().Count() }).ToArray() } };
        var created = ContentRuntime.Create(bundle);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error : null);
        var runtime = created.Value;
        Assert.True(EffectResolutionPolicyResolver.Validate(policy, runtime).IsSuccess);
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(value => value.Resolve("revision", It.IsAny<string?>())).Returns(Result<ContentRuntime>.Success(runtime));
        var formulas = new RuntimeFormulaEvaluator(Mock.Of<IMathEngine>(), new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance);
        var engine = new CalculationEngine(formulas);
        var executor = new EffectTriggerExecutor(formulas, new ImmutableEffectProcessor(), runtimes.Object, engine,
            new CompositeCalculationInfluenceProvider([new CardComponentInfluenceProvider(formulas), new EntityResourceInfluenceProvider()]));
        var run = original.Run with { ResolvedMode = original.Run.ResolvedMode! with { Definition = original.Run.ResolvedMode.Definition with
            { EffectResolutionPolicyId = "global", CalculationPipelineIds = profiles.Keys.ToArray() } } };
        return (new(executor, run, runtime, engine), policy);
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(100, 2)] [InlineData(200, 3)] [InlineData(300, 4)]
    [InlineData(400, 5)]
    public void GlobalDamageInheritsLinearCriticalWithoutAuthoredRandomInputs(float chance, float multiplier)
    {
        var (fixture, _) = Create(chance);
        var damage = MultiTierCriticalTests.Damage(chance) with { RandomInputs = [], Parameters = [] , FlatValue = 4 };
        var request = EffectTransactionTests.Request(damage) with { Run = fixture.Run };
        var before = CanonicalJson.ComputeHash(request);
        var results = Enumerable.Range(0, 5).Select(_ => fixture.Executor.Execute(request)).ToArray();
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(results.Select(result => result.Value.Fingerprint).Distinct());
        var step = results[0].Value.Steps[0];
        Assert.Equal(4 * multiplier, step.Calculation!.Value);
        Assert.Equal("global", step.ResolutionPolicy!.PolicyId);
        Assert.True(step.ResolutionPolicy.CriticalEnabled);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
        Assert.Equal(request.Combat.Determinism.RandomState, results[0].Value.State.Determinism.RandomState);
    }

    [Theory]
    [InlineData(1, 12)] [InlineData(.5f, 8)] [InlineData(1.5f, 16)]
    public void LinearTierUsesConfiguredBonus(float bonus, float expected)
    {
        var (fixture, _) = Create(200, bonus);
        var request = EffectTransactionTests.Request(MultiTierCriticalTests.Damage(0) with { RandomInputs = [] }) with { Run = fixture.Run };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(expected, result.Value.Steps[0].Calculation!.Value);
    }

    [Fact]
    public void CardProhibitionWinsRemovesRollAndLeavesDefinitionsUntouched()
    {
        var (fixture, _) = Create();
        var damage = MultiTierCriticalTests.Damage(150);
        var request = EffectTransactionTests.Request(damage) with { Run = fixture.Run, Card = new() { Components = [
            new CardEffectRuleOverrideComponentDefinition { ComponentId = "no-critical", Rule = new() { Critical = false } },
            new CardEffectRuleOverrideComponentDefinition { ComponentId = "allow", Rule = new() { Critical = true } }] } };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Empty(result.Value.Steps[0].RandomInputs);
        Assert.False(result.Value.Steps[0].ResolutionPolicy!.CriticalEnabled);
        Assert.Equal(4, result.Value.Steps[0].Calculation!.Value);
        Assert.Equal(request.Combat.Determinism.RandomState, result.Value.State.Determinism.RandomState);
        Assert.Single(damage.RandomInputs);
        Assert.Equal("critical", damage.Parameters[0].PipelineId);
    }

    [Fact]
    public void PaymentsResidualAndNonmatchingChannelsDoNotAcquireCriticalRules()
    {
        var (fixture, policy) = Create();
        var damage = MultiTierCriticalTests.Damage(0) with { RandomInputs = [] };
        foreach (var effect in new[] { damage with { Type = EffectType.MODIFY_RESOURCE }, damage with { Tags = ["residual"] },
            damage with { Parameters = [damage.Parameters[0] with { Channel = "plain", PipelineId = "plain" }] } })
        {
            var result = EffectResolutionPolicyResolver.Resolve(effect, policy, fixture.Runtime);
            Assert.True(result.IsSuccess);
            Assert.Null(result.Value.Receipt);
            Assert.Same(effect, result.Value.Effect);
        }
    }

    [Fact]
    public void AmbiguousPoliciesAndInvalidOverridesFailClosed()
    {
        var (fixture, policy) = Create();
        var invalid = policy with { Rules = policy.Rules.Add(policy.Rules[0] with { RuleId = "duplicate-selector" }) };
        Assert.True(EffectResolutionPolicyResolver.Resolve(MultiTierCriticalTests.Damage(0), invalid, fixture.Runtime).IsFailure);
        Assert.True(EffectResolutionPolicyResolver.ValidateOverride(new()).IsFailure);
        Assert.True(EffectResolutionPolicyResolver.Validate(policy with { Rules = [policy.Rules[0] with { CriticalStageId = "missing" }] }, fixture.Runtime).IsFailure);
    }

    [Theory]
    [InlineData(0)] [InlineData(15)] [InlineData(99)] [InlineData(100)] [InlineData(150)] [InlineData(200)] [InlineData(250)] [InlineData(300)]
    public void ChanceAndBonusMatrixMatchesTierContracts(float chance)
    {
        foreach (var percent in new[] { 0f, 50f, 100f, 150f, 200f })
        {
            var (fixture, _) = Create(chance, percent / 100);
            var request = EffectTransactionTests.Request(MultiTierCriticalTests.Damage(0) with { RandomInputs = [] }) with { Run = fixture.Run };
            var result = fixture.Executor.Execute(request);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            var step = result.Value.Steps[0];
            var fact = Assert.Single(step.RandomInputs);
            Assert.Equal(chance % 100 / 100, fact.Probability);
            Assert.Equal(4 * (1 + (System.Math.Floor(chance / 100) + (fact.Success ? 1 : 0)) * percent / 100), step.Calculation!.Value, 4);
            Assert.Equal(result.Value.Fingerprint, fixture.Executor.Execute(request).Value.Fingerprint);
        }
    }
}
