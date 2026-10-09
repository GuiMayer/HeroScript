using System.Text.Json;
using Core.Calculations;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Run.Content;
using Core.Logging;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class CalculatedRandomInputTests
{
    [Theory]
    [InlineData(0f, false, 0)]
    [InlineData(1f, true, 0)]
    [InlineData(.5f, null, 3)]
    public void NumericProbabilityIsAuditedAndKeepsZeroAndOneRngConventions(float chance, bool? success, int draws)
    {
        var fixture = Fixture();
        var request = EffectTransactionTests.Request(Change(-1) with
        { Repeat = 3, RandomInputs = [Input(chance)] }) with { Run = fixture.Run };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var expected = request.Combat.Determinism;
        for (var i = 0; i < draws; i++) expected = expected.DrawDouble().Context;
        Assert.Equal(expected.RandomState, result.Value.State.Determinism.RandomState);
        Assert.Equal(7, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        foreach (var step in result.Value.Steps)
        {
            var fact = Assert.Single(step.RandomInputs);
            Assert.Equal(chance, fact.Probability);
            if (success != null) Assert.Equal(success, fact.Success);
            Assert.Equal("revision", fact.ContentRevision);
            Assert.Equal(step.Identity!.ImpactId, fact.CapturedAtImpactId);
            Assert.NotEmpty(fact.SnapshotHash);
            Assert.NotEmpty(fact.RunSnapshotHash!);
            Assert.NotNull(fact.Calculation);
            Assert.True(fact.Calculation.CaptureOnly);
            Assert.Equal("probability", fact.Calculation.Quantity.UnitId);
            Assert.NotEmpty(fact.Calculation.Fingerprint);
        }
        Assert.Equal(3, result.Value.Calculations.Count(item => item.Channel == "probability"));
    }

    [Theory]
    [InlineData(EffectRandomScope.Impact, 3, "0.5,0.4,0.3")]
    [InlineData(EffectRandomScope.Action, 1, "0.5,0.5,0.5")]
    public void ImpactReadsLiveStateWhileExplicitSharedCaptureKeepsFirstEligibleSnapshot(
        EffectRandomScope scope, int draws, string expected)
    {
        var fixture = Fixture();
        var input = Input(0) with { Scope = scope, Probability = Input(0).Probability! with
        { FlatValue = null, FormulaValue = "target.resources.focus.current / 20", SharedContextCapture = EffectRandomSharedContextCapture.FirstEligibleImpact } };
        var request = EffectTransactionTests.Request(Change(-2) with { Repeat = 3, RandomInputs = [input] }) with { Run = fixture.Run };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var facts = result.Value.Steps.Select(step => Assert.Single(step.RandomInputs)).ToArray();
        Assert.Equal(expected, string.Join(",", facts.Select(fact => fact.Probability.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        var rng = request.Combat.Determinism;
        for (var i = 0; i < draws; i++) rng = rng.DrawDouble().Context;
        Assert.Equal(rng.RandomState, result.Value.State.Determinism.RandomState);
        Assert.Equal(draws, result.Value.Calculations.Count(item => item.Channel == "probability"));
        Assert.Equal(draws, facts.Select(fact => fact.SnapshotHash).Distinct().Count());
    }

    [Theory]
    [InlineData(EffectRandomScope.Action, 1)]
    [InlineData(EffectRandomScope.ParentProc, 3)]
    public void ChildInputUsesParentProcOrActionCapture(EffectRandomScope scope, int draws)
    {
        var fixture = Fixture();
        var child = Change(0) with { RandomInputs = [Input(.5f) with { Scope = scope }] };
        var request = EffectTransactionTests.Request(Change(0) with { Repeat = 3, ChainedEffects = [child] }) with { Run = fixture.Run };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var facts = result.Value.Steps.SelectMany(step => step.RandomInputs).ToArray();
        Assert.Equal(draws, facts.Select(fact => fact.ScopeId).Distinct().Count());
        Assert.Equal(draws, result.Value.Calculations.Count(item => item.Channel == "probability"));
    }

    [Fact]
    public void SharedGroupsReuseCalculationDespiteChangesToSourceAndSupportAliasedInputs()
    {
        var fixture = Fixture();
        var input = Input(0) with { GroupId = "shared", Scope = EffectRandomScope.Action, Probability = Input(0).Probability! with
        { FlatValue = null, FormulaValue = "source.resources.focus.current / 20" } };
        var first = Change(-2) with { Target = EffectTarget.SELF, RandomInputs = [input] };
        var second = Change(0) with { RandomInputs = [input with { InputId = "other" }] };
        var result = fixture.Executor.Execute(EffectTransactionTests.Request(first, second) with { Run = fixture.Run });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var a = Assert.Single(result.Value.Steps[0].RandomInputs);
        var b = Assert.Single(result.Value.Steps[1].RandomInputs);
        Assert.Equal(.5f, b.Probability);
        Assert.Equal(a with { InputId = "other" }, b);
        Assert.Single(result.Value.Calculations, item => item.Channel == "probability");
        Assert.NotEmpty(EffectDefinitionValidator.Validate([first, second with { RandomInputs = [input with
        { Probability = input.Probability! with { FlatValue = .2f, FormulaValue = null } }] }]));
    }

    [Fact]
    public void AnyEffectCanConsumeProbabilityFactsWithoutSpecialDamageRules()
    {
        var fixture = Fixture();
        var status = EffectSequenceBudgetTests.Stacks(EffectNumericParameter.StatusStacks, 0, 1);
        status = status with { RandomInputs = [Input(1)], Parameters = [status.Parameters[0] with
        { Distribution = null, StageIds = [], FlatValue = null, FormulaValue = "rolls.proc.success + rolls.proc.probability" }] };
        var result = fixture.Executor.Execute(EffectTransactionTests.Request(status) with { Run = fixture.Run });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, Assert.Single(result.Value.State.StatusEffects["enemy"]).Stacks);
    }

    [Theory]
    [InlineData(-.1f)]
    [InlineData(1.1f)]
    public void OutOfRangeResultFailsInsteadOfClampingSilently(float value)
    {
        var fixture = Fixture();
        var request = EffectTransactionTests.Request(Change(-1) with { RandomInputs = [Input(value)] }) with { Run = fixture.Run };
        var before = CanonicalJson.ComputeHash(request);
        Assert.True(fixture.Executor.Execute(request).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("nonfinite")]
    [InlineData("both-sources")]
    [InlineData("target-shared")]
    [InlineData("settlement")]
    [InlineData("capacity")]
    [InlineData("unknown-pipeline")]
    public void InvalidDormantInputsFailBeforeAnyEffectOrRngMutation(string mutation)
    {
        var pipeline = Pipeline();
        var input = Input(.5f);
        input = mutation switch
        {
            "unit" => input with { Probability = input.Probability! with { UnitId = "points" } },
            "nonfinite" => input with { Probability = input.Probability! with { FlatValue = float.PositiveInfinity } },
            "both-sources" => input with { Chance = .5f },
            "target-shared" => input with { Scope = EffectRandomScope.Action, Probability = input.Probability! with
                { FlatValue = null, FormulaValue = "target.resources.focus.current / 20" } },
            "unknown-pipeline" => input with { Probability = input.Probability! with { PipelineId = "missing" } },
            _ => input
        };
        if (mutation == "settlement") pipeline = pipeline with { ResourceInfluenceBindings = [new()
        { BindingId = "forbidden", ResourceId = "focus", Channel = "probability", Bucket = "chance", Settlement = new() }] };
        if (mutation == "capacity") pipeline = pipeline with { Buckets = [new() { BucketId = "chance", Operation = CalculationBucketOperation.ConsumeCapacity }] };
        var fixture = Fixture(pipeline);
        var request = EffectTransactionTests.Request(Change(-1), Change(0) with { Chance = 0, RandomInputs = [input] }) with { Run = fixture.Run };
        var before = CanonicalJson.ComputeHash(new { request.Combat, request.Run });
        Assert.True(fixture.Executor.Execute(request).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(new { request.Combat, request.Run }));
    }

    [Fact]
    public void TenExecutionsAndSerializedFactsAgreeAndLateFailurePublishesNothing()
    {
        var fixture = Fixture();
        var request = EffectTransactionTests.Request(Change(-1) with { Repeat = 3, RandomInputs = [Input(.5f)] }) with { Run = fixture.Run };
        var results = Enumerable.Range(0, 10).Select(_ => fixture.Executor.Execute(request)).ToArray();
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct());
        var facts = results[0].Value.Steps.SelectMany(step => step.RandomInputs).ToArray();
        var restored = JsonSerializer.Deserialize<EffectRandomInputResult[]>(JsonSerializer.Serialize(facts));
        Assert.Equal(CanonicalJson.ComputeHash(facts), CanonicalJson.ComputeHash(restored));
        var failing = request with { Trigger = request.Trigger with { Effects = [.. request.Trigger.Effects, Change(-1) with { TargetResource = "missing" }] } };
        var before = CanonicalJson.ComputeHash(failing);
        Assert.True(fixture.Executor.Execute(failing).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(failing));
        Assert.True(fixture.Executor.Execute(request with { Variables = new Dictionary<string, float> { ["rolls.proc.probability"] = 1 } }).IsFailure);
    }

    private static CalculationPipelineDefinition Pipeline() => new()
    { PipelineId = "probability", Channel = "probability", UnitId = "probability", Buckets = [new() { BucketId = "chance" }] };
    private static EffectRandomInputDefinition Input(float value) => new()
    { InputId = "proc", Scope = EffectRandomScope.Impact, Probability = new() { FlatValue = value, Channel = "probability", PipelineId = "probability" } };
    private static EffectDefinition Change(float value) => new()
    { Type = EffectType.MODIFY_RESOURCE, TargetResource = "focus", Parameters = [new()
    { Parameter = EffectNumericParameter.Amount, FlatValue = value, Channel = "counts", PipelineId = "counts", UnitId = "stacks" }] };
    private static (EffectTriggerExecutor Executor, Core.Run.RunState Run) Fixture(CalculationPipelineDefinition? pipeline = null)
        => EffectSequenceBudgetTests.Fixture(extraProfile: pipeline ?? Pipeline(),
            evaluator: new RuntimeFormulaEvaluator(Mock.Of<IMathEngine>(), new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance));

    [Fact]
    public void InvalidNonfiniteGroupIsAValidationFailureNotASerializationException()
    {
        var input = Input(float.NaN) with { GroupId = "shared", Scope = EffectRandomScope.Action };
        Assert.NotEmpty(EffectDefinitionValidator.Validate([Change(0) with { RandomInputs = [input] }]));
        Assert.NotEmpty(EffectDefinitionValidator.Validate([Change(0) with { Chance = float.NaN,
            ChanceScope = EffectChanceScope.PerAction, ChanceGroupId = "shared" }]));
        Assert.NotEmpty(EffectDefinitionValidator.Validate([Change(0) with { RandomInputs = [input with { Probability = null, Chance = float.NaN }] }]));
    }

    [Fact]
    public void PublicationChecksProbabilityReferencesAndRejectsHiddenSettlements()
    {
        var profiles = EffectSequenceBudgetTests.Profiles();
        profiles.Add("probability", Pipeline());
        var effect = Change(0) with { RandomInputs = [Input(.5f)] };
        var graph = new ContentGraphValidator().Validate(EffectSequenceBudgetTests.Bundle(profiles, effect));
        Assert.True(graph.IsValid, string.Join(";", graph.Errors));
        profiles["probability"] = Pipeline() with { Buckets = [new() { BucketId = "chance", Operation = CalculationBucketOperation.ConsumeCapacity }] };
        graph = new ContentGraphValidator().Validate(EffectSequenceBudgetTests.Bundle(profiles, effect with { Chance = 0 }));
        Assert.False(graph.IsValid);
        Assert.Contains(graph.Errors, error => error.Contains("capacity consumption", StringComparison.Ordinal));
    }

    [Fact]
    public void ProbabilityKeepsOriginTagsAndOnlyUsesExplicitBounds()
    {
        var fixture = Fixture();
        var request = EffectTransactionTests.Request(Change(0) with { Type = EffectType.DAMAGE,
            RandomInputs = [Input(0)] }) with { Run = fixture.Run, Card = new() { Components = [new CardInfluenceComponentDefinition
            { ComponentId = "conditional", Channel = "probability", Bucket = "chance", Value = .75f, RequiredTags = ["effect.damage"] }] } };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(.75f, Assert.Single(result.Value.Steps[0].RandomInputs).Probability);
        var bounded = Input(2) with { Probability = Input(2).Probability! with { Conversion = new() { Maximum = 1 } } };
        result = fixture.Executor.Execute(EffectTransactionTests.Request(Change(0) with { RandomInputs = [bounded] }) with { Run = fixture.Run });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(1, Assert.Single(result.Value.Steps[0].RandomInputs).Probability);
    }
}
