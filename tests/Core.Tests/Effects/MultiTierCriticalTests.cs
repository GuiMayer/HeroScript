using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class MultiTierCriticalTests
{
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(50f, .5f, 0f)]
    [InlineData(100f, 0f, 1f)]
    [InlineData(150f, .5f, 1f)]
    [InlineData(200f, 0f, 2f)]
    public void AuthoredProbabilityAndImpactPipelineComposeEachTier(float chance, float probability, float guaranteed)
    {
        var fixture = Fixture();
        var request = Request(fixture, Damage(chance));
        var before = CanonicalJson.ComputeHash(request);
        var results = Enumerable.Range(0, 10).Select(_ => fixture.Executor.Execute(request)).ToArray();
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct());
        var result = results[0].Value;
        var fact = Assert.Single(result.Steps[0].RandomInputs);
        Assert.Equal(probability, fact.Probability);
        Assert.Equal(guaranteed, fact.Calculation!.Checkpoints["guaranteed"]);
        Assert.Equal(chance, fact.Captures["chance"].Value);
        Assert.Equal(4 * (1 + guaranteed + (fact.Success ? 1 : 0)), result.Steps[0].Calculation!.Value);
        Assert.All(fact.Captures.Values, capture => Assert.True(capture.CaptureOnly));
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
        Assert.Equal(probability == 0 ? request.Combat.Determinism.RandomState : request.Combat.Determinism.DrawDouble().Context.RandomState,
            result.State.Determinism.RandomState);
        var restored = JsonSerializer.Deserialize<EffectRandomInputResult>(JsonSerializer.Serialize(fact));
        Assert.Equal(CanonicalJson.ComputeHash(fact), CanonicalJson.ComputeHash(restored));
    }

    [Fact]
    public void MultiHitDrawsIndependentlyAfterDistributingOneSourceBudgetAndSettlesLiveDefense()
    {
        var fixture = Fixture();
        var damage = Damage(150, 10, 3);
        damage = damage with { Parameters = [damage.Parameters[0] with { StageIds = ["impact"], Distribution = new()
        { SourceStageIds = ["origin"], Allocation = new() { Mode = CalculationDistributionMode.Quantized, Quantum = 1 } } }] };
        var request = Request(fixture, damage) with { Card = new() { Components = [new CardInfluenceComponentDefinition
        { ComponentId = "added", Channel = "points", Bucket = "flat", Value = 2 }] } };
        var enemy = request.Combat.GetActor("enemy")!;
        request = request with { Combat = request.Combat.ReplaceActor(enemy with { ResourceState = enemy.ResourceState with
        { Resources = enemy.ResourceState.Resources.ToDictionary().Append(new KeyValuePair<string, ResourcePool>("shield",
            ResourcePool.Materialize(new ResourceDefinition { ResourceId = "shield", DisplayName = "Shield" }, 5, 5))).ToDictionary() } }) };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(12, Assert.Single(result.Value.Steps.SelectMany(step => step.SequenceBudgets)).Capture.Value);
        var expectedRng = request.Combat.Determinism;
        foreach (var (step, index) in result.Value.Steps.Select((step, index) => (step, index)))
        {
            var draw = expectedRng.DrawDouble(); expectedRng = draw.Context;
            var fact = Assert.Single(step.RandomInputs);
            Assert.Equal(draw.Value, fact.Roll);
            Assert.Equal(draw.Value < .5, fact.Success);
            Assert.Equal(4, Assert.Single(step.ImpactShares).Share.Quantity.Value);
            Assert.Equal(4 * (2 + (fact.Success ? 1 : 0)) - (index == 0 ? 5 : 0), step.Calculation!.Value);
        }
        Assert.Equal(expectedRng.RandomState, result.Value.State.Determinism.RandomState);
        Assert.Equal(0, result.Value.State.GetActor("enemy")!.GetResource("shield")!.Current);
        Assert.Equal(3, result.Value.Steps.Select(step => step.RandomInputs[0].ScopeId).Distinct().Count());
    }

    [Fact]
    public void EarlierHitBuffChangesNextCaptureButBeforeImpactChildrenCannotRewriteCapturedInputs()
    {
        var fixture = Fixture();
        var damage = Damage(0, repeat: 3);
        var input = damage.RandomInputs[0];
        input = input with { Probability = input.Probability! with { Captures = input.Probability.Captures
            .SetItem("chance", input.Probability.Captures["chance"] with { FlatValue = null, FormulaValue = "source.resources.focus.current * 15" })
            .SetItem("bonus", input.Probability.Captures["bonus"] with { FlatValue = null, FormulaValue = "source.resources.focus.current / 10" }) } };
        var child = new EffectDefinition { Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF, TargetResource = "focus",
            ChildTiming = EffectChildTiming.BeforeParentImpact, Parameters = [new() { Parameter = EffectNumericParameter.Amount,
                FlatValue = -1, Channel = "plain", PipelineId = "plain", UnitId = "points" }] };
        var request = Request(fixture, damage with { RandomInputs = [input], ChainedEffects = [child] });
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var impacts = result.Value.Steps.Where(step => !step.RandomInputs.IsEmpty).ToArray();
        Assert.Equal(new[] { 150f, 135f, 120f }, impacts.Select(step => step.RandomInputs[0].Captures["chance"].Value));
        Assert.Equal(new[] { 1f, .9f, .8f }, impacts.Select(step => step.RandomInputs[0].Captures["bonus"].Value));
        foreach (var step in impacts)
        {
            var fact = step.RandomInputs[0];
            Assert.Equal(4 * (1 + (1 + (fact.Success ? 1 : 0)) * fact.Captures["bonus"].Value), step.Calculation!.Value, 4);
        }
        Assert.Equal(7, result.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
    }

    [Fact]
    public void LocalAlternativesUseSameCalculationEngineWithoutRandomnessOrGlobalRangePromises()
    {
        var fixture = Fixture();
        var request = Request(fixture, Damage(150));
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var before = CanonicalJson.ComputeHash(request);
        var results = Enumerable.Range(0, 20).Select(_ => RandomOutcomePreviewProjector.Project(result.Value.Steps, fixture.Runtime, fixture.Calculations)).ToArray();
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result)).Distinct());
        var preview = results[0];
        Assert.False(preview.IsGlobalOutcomeRange);
        var impact = Assert.Single(preview.Impacts);
        Assert.True(impact.ConditionalOnReachingImpact);
        Assert.Equal(new[] { .5f, .5f }, impact.Alternatives.Select(item => item.Probability));
        Assert.Equal(new[] { 8f, 12f }, impact.Alternatives.Select(item => item.Calculation!.Value));
        Assert.All(impact.Alternatives, item => { Assert.Null(item.UnavailableReason); Assert.True(item.Calculation!.CaptureOnly); });
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
        var many = Enumerable.Range(0, 100).SelectMany(_ => result.Value.Steps).ToArray();
        var bounded = RandomOutcomePreviewProjector.Project(many, fixture.Runtime, fixture.Calculations);
        Assert.True(bounded.Truncated);
        Assert.Equal(RandomOutcomePreviewProjector.MaximumInputs, bounded.Impacts.Length);
        var ambiguous = result.Value.Steps[0] with { RandomInputs = result.Value.Steps[0].RandomInputs.Add(result.Value.Steps[0].RandomInputs[0] with { InputId = "another" }) };
        Assert.All(RandomOutcomePreviewProjector.Project([ambiguous], fixture.Runtime, fixture.Calculations).Impacts,
            item => Assert.All(item.Alternatives, alternative => Assert.Equal("multiple_dependent_inputs", alternative.UnavailableReason)));
        foreach (var (trace, reason) in new[]
        {
            (result.Value.Steps[0].Calculation! with { ContentRevision = "another" }, "content_revision_mismatch"),
            (result.Value.Steps[0].Calculation! with { PipelineFingerprint = "another" }, "pipeline_fingerprint_mismatch")
        })
        {
            var changed = result.Value.Steps[0] with { Parameters = [], Calculation = trace };
            Assert.All(RandomOutcomePreviewProjector.Project([changed], fixture.Runtime, fixture.Calculations).Impacts[0].Alternatives,
                alternative => { Assert.Null(alternative.Calculation); Assert.Equal(reason, alternative.UnavailableReason); });
        }
    }

    [Fact]
    public void CapturesValidateUnitsSharedTargetDependenciesAndReservedNamespaces()
    {
        var fixture = Fixture();
        var damage = Damage(150);
        var input = damage.RandomInputs[0];
        foreach (var changed in new[]
        {
            input with { Probability = input.Probability! with { Captures = input.Probability.Captures.SetItem("chance",
                input.Probability.Captures["chance"] with { UnitId = "stacks" }) } },
            input with { Scope = EffectRandomScope.Action, Probability = input.Probability! with { Captures = input.Probability.Captures.SetItem("chance",
                input.Probability.Captures["chance"] with { FlatValue = null, FormulaValue = "target.resources.focus.current + 0" }) } }
        }) Assert.True(fixture.Executor.Execute(Request(fixture, damage with { Chance = 0, RandomInputs = [changed] })).IsFailure);
        Assert.True(fixture.Executor.Execute(Request(fixture, damage) with { Variables = new Dictionary<string, float> { ["captures.chance"] = 200 } }).IsFailure);
        Assert.True(fixture.Executor.Execute(Request(fixture, damage) with { Variables = new Dictionary<string, float> { ["rolls.critical.values.bonus"] = 9 } }).IsFailure);
        var broken = input with { Probability = input.Probability! with { Captures = input.Probability.Captures.SetItem("chance",
            input.Probability.Captures["chance"] with { FormulaValue = "captures.bonus + 0" }) } };
        Assert.NotEmpty(EffectDefinitionValidator.Validate([damage with { RandomInputs = [broken] }]));
    }

    [Fact]
    public void UnconfiguredChildDoesNotInheritCriticalMultiplierOrDrawAgain()
    {
        var fixture = Fixture();
        var child = new EffectDefinition { Type = EffectType.DAMAGE, TargetResource = "focus",
            Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = 3,
                Channel = "plain", PipelineId = "plain", UnitId = "points" }] };
        var request = Request(fixture, Damage(150) with { ChainedEffects = [child] });
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.Steps.Length);
        Assert.Single(result.Value.Steps[0].RandomInputs);
        Assert.Empty(result.Value.Steps[1].RandomInputs);
        Assert.Equal(3, result.Value.Steps[1].Calculation!.Value);
        Assert.Equal(request.Combat.Determinism.DrawDouble().Context.RandomState, result.Value.State.Determinism.RandomState);
    }

    internal static EffectDefinition Damage(float chance, float value = 4, int repeat = 1) => new()
    {
        Type = EffectType.DAMAGE, TargetResource = "focus", Repeat = repeat,
        Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = value, Channel = "points", PipelineId = "critical", UnitId = "points" }],
        RandomInputs = [new() { InputId = "critical", Scope = EffectRandomScope.Impact, Probability = new()
        { FormulaValue = "captures.chance + 0", Channel = "probability", PipelineId = "probability", Captures =
            ImmutableSortedDictionary<string, EffectNumericParameterDefinition>.Empty
                .Add("chance", new() { FlatValue = chance, Channel = "chance", PipelineId = "chance", UnitId = "percent" })
                .Add("bonus", new() { FlatValue = 1, Channel = "bonus", PipelineId = "bonus", UnitId = "factor" }) } }]
    };

    internal sealed record TestFixture(EffectTriggerExecutor Executor, RunState Run, ContentRuntime Runtime, ICalculationEngine Calculations);
    internal static TestFixture Fixture()
    {
        var formulas = new RuntimeFormulaEvaluator(Mock.Of<IMathEngine>(), new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance);
        var profiles = new Dictionary<string, CalculationPipelineDefinition>
        {
            ["chance"] = new() { PipelineId = "chance", Channel = "chance", UnitId = "percent", Buckets = [new() { BucketId = "flat" }] },
            ["bonus"] = new() { PipelineId = "bonus", Channel = "bonus", UnitId = "factor", Buckets = [new() { BucketId = "flat" }] },
            ["plain"] = new() { PipelineId = "plain", Channel = "plain", UnitId = "points", Buckets = [new() { BucketId = "flat" }] },
            ["probability"] = new() { PipelineId = "probability", Channel = "probability", UnitId = "probability",
                Stages = [new() { StageId = "guaranteed" }, new() { StageId = "fraction" }], Buckets = [new()
                { BucketId = "tiers", StageId = "guaranteed", Operation = CalculationBucketOperation.Formula, Formula = "bucket.input / 100", Rounding = CalculationRounding.Floor }, new()
                { BucketId = "fraction", StageId = "fraction", Order = 1, Operation = CalculationBucketOperation.Formula, Formula = "captures.chance % 100 / 100" }] },
            ["critical"] = new() { PipelineId = "critical", Channel = "points", UnitId = "points",
                Stages = [new() { StageId = "origin", Scope = CalculationStageScope.Actor }, new() { StageId = "impact", Scope = CalculationStageScope.Target }],
                Buckets = [new() { BucketId = "flat", StageId = "origin" }, new() { BucketId = "critical", StageId = "impact", Order = 1,
                    Operation = CalculationBucketOperation.Formula, Formula = "rolls.critical.checkpoints.guaranteed + rolls.critical.success * rolls.critical.values.bonus + 1 * bucket.input" },
                    new() { BucketId = "shield", StageId = "impact", Order = 2, Operation = CalculationBucketOperation.ConsumeCapacity }],
                ResourceInfluenceBindings = [new() { BindingId = "shield", Scope = CalculationEntityScope.Target, ResourceId = "shield",
                    Channel = "points", Bucket = "shield", Settlement = new() }] }
        };
        var runtime = ContentRuntime.Create(EffectSequenceBudgetTests.Bundle(profiles)).Value;
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve("revision", It.IsAny<string?>())).Returns(Result<ContentRuntime>.Success(runtime));
        var engine = new CalculationEngine(formulas);
        return new(new(formulas, new ImmutableEffectProcessor(), runtimes.Object, engine,
            new CompositeCalculationInfluenceProvider([new CardComponentInfluenceProvider(formulas), new EntityResourceInfluenceProvider()])),
            new() { PlayerEntityId = "hero", Determinism = DeterministicContext.Create(123, "revision"),
                ResolvedMode = new() { Definition = new() { CalculationPipelineIds = profiles.Keys.ToArray() } } }, runtime, engine);
    }
    private static EffectTriggerExecutionRequest Request(TestFixture fixture, EffectDefinition effect)
    {
        var request = EffectTransactionTests.Request(effect) with { Run = fixture.Run };
        var actor = request.Combat.GetActor("enemy")!;
        return request with { Combat = request.Combat.ReplaceActor(actor with { ResourceState = actor.ResourceState with
        { Resources = new Dictionary<string, ResourcePool> { ["focus"] = ResourcePool.Materialize(actor.GetResource("focus")!.Definition!, 200, 200) } } }) };
    }
}
