using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Run.Content;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.Calculations;

public sealed class CalculationProfileTests
{
    [Fact]
    public void CapturedOriginIsNotAmplifiedAgainAndNewTargetGetsItsOwnStage()
    {
        var engine = new CalculationEngine();
        var captured = engine.Calculate(Request("capture", ["origin"], "a", 10) with
        { CaptureOnly = true, Influences = [Influence("flat", 2)] }, Pipeline());
        Assert.True(captured.IsSuccess, captured.IsFailure ? captured.Error : null);
        Assert.Equal(12, captured.Value.Checkpoints["origin"]);
        var first = engine.Calculate(Request("first", ["defense"], "a", 0) with
        { InputQuantity = captured.Value.Quantity, Influences = [Influence("capacity", 3)] }, Pipeline());
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(9, first.Value.Value);
        var second = engine.Calculate(Request("second", ["defense"], "b", 0) with
        { InputQuantity = first.Value.Quantity, Influences = [Influence("capacity", 2)] }, Pipeline());
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(7, second.Value.Value);
        Assert.Equal(3, second.Value.Quantity.IncorporatedStages.Length);
        Assert.True(engine.Calculate(Request("same-target", ["defense"], "a", 0) with
        { InputQuantity = first.Value.Quantity }, Pipeline()).IsFailure);
        Assert.True(engine.Calculate(Request("origin-again", ["origin"], "b", 0) with
        { InputQuantity = first.Value.Quantity }, Pipeline()).IsFailure);
    }

    [Theory]
    [InlineData(CalculationRounding.Floor, 2)]
    [InlineData(CalculationRounding.Ceiling, 3)]
    [InlineData(CalculationRounding.Round, 3)]
    public void IntegerConversionRecordsDiscardedRemainder(CalculationRounding rounding, int expected)
    {
        var result = new CalculationEngine().Calculate(Request("count", ["origin"], "a", 2.7f) with
        { ValuePolicy = new() { RequireInteger = true, Rounding = rounding, Sign = CalculationSignPolicy.Positive } }, Pipeline());
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(expected, result.Value.Value);
        Assert.Equal((double)2.7f - expected, result.Value.Remainder);
        Assert.Equal(2.7f, result.Value.UnconvertedValue);
    }

    [Fact]
    public void IntegerSignAndBoundsAreExplicitNotGuessedFromTheChannel()
    {
        var request = Request("value", ["origin"], "a", -2.5f);
        Assert.True(new CalculationEngine().Calculate(request, Pipeline()).IsSuccess);
        Assert.True(new CalculationEngine().Calculate(request with
        { ValuePolicy = new() { RequireInteger = true } }, Pipeline()).IsFailure);
        Assert.True(new CalculationEngine().Calculate(request with
        { ValuePolicy = new() { Sign = CalculationSignPolicy.NonNegative } }, Pipeline()).IsFailure);
        var bounded = new CalculationEngine().Calculate(request with
        { ValuePolicy = new() { Minimum = 1, Maximum = 10, Sign = CalculationSignPolicy.Positive } }, Pipeline());
        Assert.Equal(1, bounded.Value.Value);
        Assert.Equal(-3.5, bounded.Value.Remainder);
        Assert.True(new CalculationEngine().Calculate(request with
        { ValuePolicy = new() { Minimum = 1.5f, Rounding = CalculationRounding.Floor } }, Pipeline()).IsFailure);
    }

    [Fact]
    public void TransportRejectsDifferentUnitsRevisionAndPipelineDefinition()
    {
        var engine = new CalculationEngine();
        var quantity = engine.Calculate(Request("capture", ["origin"], "a", 10), Pipeline()).Value.Quantity;
        var continuation = Request("continue", ["defense"], "a", 0) with { InputQuantity = quantity };
        Assert.True(engine.Calculate(continuation with { UnitId = "cards" }, Pipeline()).IsFailure);
        Assert.True(engine.Calculate(continuation with { InputQuantity = quantity with { UnitId = "cards" } }, Pipeline()).IsFailure);
        Assert.True(engine.Calculate(continuation with { ContentRevision = "other" }, Pipeline()).IsFailure);
        Assert.True(engine.Calculate(continuation, Pipeline() with
        { Buckets = Pipeline().Buckets.Select(bucket => bucket with { Minimum = 0 }).ToArray() }).IsFailure);
        Assert.True(engine.Calculate(continuation with { StageIds = ["unknown"] }, Pipeline()).IsFailure);
        Assert.True(engine.Calculate(continuation with { InputQuantity = quantity with { Value = float.PositiveInfinity } }, Pipeline()).IsFailure);
    }

    [Fact]
    public void StagesMustBeCompleteContiguousAndScoped()
    {
        Assert.True(CalculationEngine.ValidateDefinition(Pipeline() with
        { Buckets = [new() { BucketId = "no-stage" }] }).IsFailure);
        Assert.True(CalculationEngine.ValidateDefinition(Pipeline() with
        { Buckets = Pipeline().Buckets.Concat([new() { BucketId = "again", Order = 2, StageId = "origin" }]).ToArray() }).IsFailure);
        Assert.True(new CalculationEngine().Calculate(Request("scoped", ["origin"], "a", 1) with
        { StageContextIds = ImmutableSortedDictionary<string, string>.Empty }, Pipeline()).IsFailure);
    }

    [Fact]
    public void CaptureCannotProduceSettlementsEvenIfDefenseWasIncluded()
    {
        var request = Request("snapshot", [], "a", 10) with
        { CaptureOnly = true, Influences = [Influence("capacity", 4)] };
        var result = new CalculationEngine().Calculate(request, Pipeline());
        Assert.Equal(6, result.Value.Value);
        var settlements = new CalculationSettlementPlanner().Plan(result.Value, Pipeline(), new());
        Assert.True(settlements.IsSuccess, settlements.IsFailure ? settlements.Error : null);
        Assert.Empty(settlements.Value);
    }

    [Fact]
    public void TenCapturesHaveIdenticalFingerprintsAndPersistTheirQuantity()
    {
        var results = Enumerable.Range(0, 10).Select(_ => new CalculationEngine().Calculate(
            Request("snapshot", ["origin"], "a", 3) with { CaptureOnly = true }, Pipeline()).Value).ToArray();
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result)).Distinct());
        var restored = JsonSerializer.Deserialize<CalculationResult>(JsonSerializer.Serialize(results[0]))!;
        Assert.Equal(CanonicalJson.ComputeHash(results[0]), CanonicalJson.ComputeHash(restored));
        Assert.Equal(restored.Fingerprint, restored.Quantity.CalculationFingerprint);
    }

    [Fact]
    public void ResolverEvaluatesOnlySelectedStagesAndCapturesCardBonusOnce()
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>(MockBehavior.Strict);
        var resolver = new CalculationResolver(formulas.Object, engine: new CalculationEngine(),
            influences: new CompositeCalculationInfluenceProvider([
                new CardComponentInfluenceProvider(formulas.Object), new EntityResourceInfluenceProvider()]),
            allowUnconfiguredCalculations: true);
        var card = new EffectiveCardDefinition { Components = [new CardInfluenceComponentDefinition
        { ComponentId = "source-bonus", Channel = "magnitude", Bucket = "flat", Value = 2 }, new CardInfluenceComponentDefinition
        { ComponentId = "target-not-evaluated", Channel = "magnitude", Bucket = "capacity", Formula = "missing_target" }] };
        var parameter = new EffectNumericParameterDefinition
        { Parameter = EffectNumericParameter.Amount, FlatValue = 10, Channel = "magnitude", UnitId = "points", StageIds = ["origin"] };
        var effect = new EffectDefinition { Type = EffectType.DAMAGE, TargetResource = "focus" };
        var context = new CalculationSourceContext
        { Actor = GameplayOwnershipTests.State().GetActor("hero"), Pipeline = Pipeline(), Card = card, ContentRevision = "revision", CaptureOnly = true };
        var snapshot = resolver.ResolveParameter(effect, parameter, "snapshot", context);
        Assert.True(snapshot.IsSuccess, snapshot.IsFailure ? snapshot.Error : null);
        Assert.Equal(12, snapshot.Value.Value);
        Assert.True(snapshot.Value.Calculation!.CaptureOnly);
        Assert.Single(snapshot.Value.Calculation.Buckets);
        Assert.Empty(new CalculationSettlementPlanner().Plan(snapshot.Value.Calculation, Pipeline(), context).Value);
        formulas.VerifyNoOtherCalls();
        // Reusing the capture through the same origin stage is an error, not a second flat bonus.
        Assert.True(resolver.ResolveParameter(effect, parameter with { FlatValue = null }, "again", context with
        { InputQuantity = snapshot.Value.Calculation.Quantity }).IsFailure);
    }

    [Fact]
    public void RepeatedSemanticStageIsRejectedAcrossDifferentProfiles()
    {
        var engine = new CalculationEngine();
        var quantity = engine.Calculate(Request("capture", ["origin"], "a", 1), Pipeline()).Value.Quantity;
        var otherProfile = Pipeline() with { PipelineId = "other-profile" };
        Assert.True(engine.Calculate(Request("again", ["origin"], "b", 0) with
        { InputQuantity = quantity }, otherProfile).IsFailure);
        Assert.True(engine.Calculate(Request("target", ["defense"], "b", 0) with
        { InputQuantity = quantity }, otherProfile).IsSuccess);
    }

    [Fact]
    public void SignedAttributeDeltaIsAnOrdinaryUnitNotResourceSpecificMath()
    {
        var pipeline = Pipeline() with { UnitId = "attribute_points" };
        var request = Request("stat-delta", ["origin"], "a", -2.5f) with
        { UnitId = "attribute_points", ValuePolicy = new() { Sign = CalculationSignPolicy.Any } };
        var result = new CalculationEngine().Calculate(request, pipeline);
        Assert.Equal(-2.5f, result.Value.Quantity.Value);
        Assert.Equal("attribute_points", result.Value.Quantity.UnitId);
    }

    internal static CalculationPipelineDefinition Pipeline() => new()
    {
        PipelineId = "scoped", Channel = "magnitude", UnitId = "points",
        Stages = [new() { StageId = "origin", Scope = CalculationStageScope.Actor },
            new() { StageId = "defense", Scope = CalculationStageScope.Target }],
        Buckets = [new() { BucketId = "flat", StageId = "origin" },
            new() { BucketId = "capacity", StageId = "defense", Order = 1, Operation = CalculationBucketOperation.ConsumeCapacity }],
        ResourceInfluenceBindings = [new()
        {
            BindingId = "capacity", Channel = "magnitude", Bucket = "capacity", Scope = CalculationEntityScope.Target,
            ResourceId = "shield", Settlement = new()
        }]
    };

    private static CalculationRequest Request(string id, ImmutableArray<string> stages, string targetId, float value) => new()
    {
        CalculationId = id, ContentRevision = "revision", Channel = "magnitude", UnitId = "points",
        StageIds = stages, BaseValue = value,
        StageContextIds = new Dictionary<string, string> { ["origin"] = "source", ["defense"] = targetId }
            .ToImmutableSortedDictionary(StringComparer.Ordinal)
    };

    private static CalculationInfluence Influence(string bucket, float value) => new()
    { InfluenceId = bucket, SourceId = "test", Channel = "magnitude", Bucket = bucket, Value = value };
}
