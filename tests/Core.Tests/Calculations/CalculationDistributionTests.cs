using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.Calculations;

public sealed class CalculationDistributionTests
{
    [Theory]
    [InlineData(10f, 3, CalculationRemainderAllocation.Earliest, 4f, 3f, 3f)]
    [InlineData(10f, 3, CalculationRemainderAllocation.Latest, 3f, 3f, 4f)]
    [InlineData(2f, 3, CalculationRemainderAllocation.Earliest, 1f, 1f, 0f)]
    [InlineData(-10f, 3, CalculationRemainderAllocation.Latest, -3f, -3f, -4f)]
    [InlineData(0f, 3, CalculationRemainderAllocation.Earliest, 0f, 0f, 0f)]
    public void QuantizedAllocationConservesBudgetAndDeclaresRemainderBias(float value, int count,
        CalculationRemainderAllocation order, float a, float b, float c)
    {
        ICalculationEngine engine = new CalculationEngine();
        var result = Success(engine.Distribute(Request(value, count) with
            { Policy = new() { Mode = CalculationDistributionMode.Quantized, Quantum = 1, RemainderAllocation = order } }));
        Assert.Equal(new[] { a, b, c }, result.Shares.Select(share => share.Quantity.Value));
        Assert.Equal((double)value, result.AllocatedTotal); Assert.Equal(0, result.ConservationRemainder);
        Assert.Equal(System.Math.Abs(value), result.TotalUnits);
        Assert.Equal(3, result.Shares.Length); Assert.Equal("points", result.UnitId);
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(0.1f)]
    [InlineData(-0.1f)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    [InlineData(float.Epsilon)]
    [InlineData(-float.Epsilon)]
    [InlineData(0f)]
    public void ContinuousAllocationConservesExactFloat32ForNormalExtremeAndSubnormalValues(float value)
    {
        foreach (var count in new[] { 1, 3, 7, 257, CalculationEngine.MaximumDistributionRecipients })
        {
            var result = Success(new CalculationEngine().Distribute(Request(value, count)));
            Assert.Equal((double)value, result.Shares.Sum(share => (double)share.Quantity.Value));
            Assert.Equal((double)value, result.AllocatedTotal); Assert.Equal(0, result.ConservationRemainder);
            Assert.Equal(count, result.Shares.Length);
            Assert.All(result.Shares, share => Assert.True(float.IsFinite(share.Quantity.Value)));
            Assert.InRange(result.Shares.Max(share => System.Math.Abs((double)share.Quantity.Value)) -
                result.Shares.Min(share => System.Math.Abs((double)share.Quantity.Value)), 0, result.AllocationQuantum);
        }
    }

    [Fact]
    public void ContinuousAllocationConservesDiverseBitPatternsWithoutAnEpsilonTolerance()
    {
        for (uint index = 1; index <= 128; index++)
        {
            var bits = unchecked(index * 2_654_435_761u);
            if (((bits >> 23) & 255) == 255) bits &= 0xfeffffff;
            var value = BitConverter.UInt32BitsToSingle(bits);
            var result = Success(new CalculationEngine().Distribute(Request(value, (int)(index % 63) + 1)));
            Assert.Equal((double)value, result.Shares.Sum(share => (double)share.Quantity.Value));
        }
    }

    [Fact]
    public void QuantizedFractionalAndSignedUnitsDoNotDependOnResourceOrEffectNames()
    {
        var request = Request(2.5f, 3, "arbitrary_potency") with
            { Policy = new() { Mode = CalculationDistributionMode.Quantized, Quantum = .25f } };
        var result = Success(new CalculationEngine().Distribute(request));
        Assert.Equal(new[] { 1f, .75f, .75f }, result.Shares.Select(share => share.Quantity.Value));
        Assert.All(result.Shares, share => Assert.Equal("arbitrary_potency", share.Quantity.UnitId));
        var signed = Success(new CalculationEngine().Distribute(Request(-2.5f, 3, "attribute_delta") with { Policy = request.Policy }));
        Assert.Equal(new[] { -1f, -.75f, -.75f }, signed.Shares.Select(share => share.Quantity.Value));
    }

    [Fact]
    public void InputOrderingDoesNotChangeAllocationButExplicitOrderAndTieBreakDo()
    {
        var request = Request(10, 3) with { Recipients = [new() { RecipientId = "z", Order = -1 },
            new() { RecipientId = "a", Order = 2 }, new() { RecipientId = "A", Order = 2 }],
            Policy = new() { Mode = CalculationDistributionMode.Quantized, Quantum = 1 } };
        var first = Success(new CalculationEngine().Distribute(request));
        var reversed = Success(new CalculationEngine().Distribute(request with { Recipients = request.Recipients.Reverse().ToArray() }));
        Assert.Equal(new[] { "z", "A", "a" }, first.Shares.Select(share => share.RecipientId));
        Assert.Equal(first.Fingerprint, reversed.Fingerprint);
        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(reversed));
        var changed = Success(new CalculationEngine().Distribute(request with { Recipients = request.Recipients.Select(recipient =>
            recipient.RecipientId == "z" ? recipient with { Order = 3 } : recipient).ToArray() }));
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
        Assert.Equal("A", changed.Shares[0].RecipientId);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("whitespace")]
    [InlineData("too_long")]
    [InlineData("too_many")]
    public void InvalidRecipientSetsFailWithoutPartialAllocation(string problem)
    {
        var request = Request(10, 3);
        IReadOnlyList<CalculationDistributionRecipient> recipients = problem switch
        {
            "empty" => [], "duplicate" => [request.Recipients[0], request.Recipients[0]], "null" => [null!],
            "whitespace" => [new() { RecipientId = " " }], "too_long" => [new() { RecipientId = new string('x', 129) }],
            _ => Enumerable.Range(0, 4097).Select(index => new CalculationDistributionRecipient { RecipientId = index.ToString() }).ToArray()
        };
        Assert.True(new CalculationEngine().Distribute(request with { Recipients = recipients }).IsFailure);
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("infinite")]
    [InlineData("unit")]
    [InlineData("revision")]
    [InlineData("provenance")]
    [InlineData("empty_stages")]
    [InlineData("duplicate_stages")]
    [InlineData("invalid_scope")]
    [InlineData("missing_context")]
    [InlineData("missing_pipeline")]
    [InlineData("shared_context")]
    [InlineData("null_receipt")]
    public void InvalidTransportedQuantityIsRejected(string problem)
    {
        var request = Request(10, 3); var quantity = request.InputQuantity;
        quantity = problem switch
        {
            "nan" => quantity with { Value = float.NaN }, "infinite" => quantity with { Value = float.PositiveInfinity },
            "unit" => quantity with { UnitId = "other" }, "revision" => quantity with { ContentRevision = "other" },
            "provenance" => quantity with { CalculationFingerprint = "" }, "empty_stages" => quantity with { IncorporatedStages = [] },
            "duplicate_stages" => quantity with { IncorporatedStages = quantity.IncorporatedStages.Add(quantity.IncorporatedStages[0]) },
            "invalid_scope" => quantity with { IncorporatedStages = [quantity.IncorporatedStages[0] with { Scope = (CalculationStageScope)99 }] },
            "missing_context" => quantity with { IncorporatedStages = [quantity.IncorporatedStages[0] with { ContextId = "" }] },
            "missing_pipeline" => quantity with { IncorporatedStages = [quantity.IncorporatedStages[0] with { PipelineFingerprint = "" }] },
            "shared_context" => quantity with { IncorporatedStages = [quantity.IncorporatedStages[0] with { Scope = CalculationStageScope.Shared, ContextId = "not-shared" }] },
            _ => quantity with { IncorporatedStages = [null!] }
        };
        Assert.True(new CalculationEngine().Distribute(request with { InputQuantity = quantity }).IsFailure);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("order")]
    [InlineData("continuous_quantum")]
    [InlineData("missing_quantum")]
    [InlineData("zero_quantum")]
    [InlineData("negative_quantum")]
    [InlineData("infinite_quantum")]
    [InlineData("nan_quantum")]
    [InlineData("fractional_units")]
    [InlineData("unrepresentable_share")]
    [InlineData("excessive_units")]
    public void InvalidOrNonConservingPolicyIsRejected(string problem)
    {
        var request = Request(10, 3);
        var policy = new CalculationDistributionPolicy { Mode = CalculationDistributionMode.Quantized, Quantum = 1 };
        policy = problem switch
        {
            "mode" => policy with { Mode = (CalculationDistributionMode)99 }, "order" => policy with { RemainderAllocation = (CalculationRemainderAllocation)99 },
            "continuous_quantum" => policy with { Mode = CalculationDistributionMode.Continuous }, "missing_quantum" => policy with { Quantum = null },
            "zero_quantum" => policy with { Quantum = 0 }, "negative_quantum" => policy with { Quantum = -1 },
            "infinite_quantum" => policy with { Quantum = float.PositiveInfinity }, "nan_quantum" => policy with { Quantum = float.NaN },
            "fractional_units" => policy with { Quantum = 3 }, "excessive_units" => policy with { Quantum = float.Epsilon },
            _ => policy with { Quantum = 1.0000001192092896f }
        };
        if (problem == "unrepresentable_share") request = Request(8.000000953674316f, 3);
        Assert.True(new CalculationEngine().Distribute(request with { Policy = policy }).IsFailure);
    }

    [Fact]
    public void RequestOwnsDefensiveRecipientCopyAndRoundTripsWithItsTrace()
    {
        var mutable = new List<CalculationDistributionRecipient> { new() { RecipientId = "impact", Order = 5 } };
        var request = Request(10, 1) with { Recipients = mutable };
        var hash = CanonicalJson.ComputeHash(request); mutable.Clear();
        Assert.Equal(hash, CanonicalJson.ComputeHash(request)); Assert.Single(request.Recipients);
        var restoredRequest = JsonSerializer.Deserialize<CalculationDistributionRequest>(JsonSerializer.Serialize(request))!;
        Assert.Equal(hash, CanonicalJson.ComputeHash(restoredRequest));
        var result = Success(new CalculationEngine().Distribute(request));
        var restoredResult = JsonSerializer.Deserialize<CalculationDistributionResult>(JsonSerializer.Serialize(result))!;
        Assert.Equal(CanonicalJson.ComputeHash(result), CanonicalJson.ComputeHash(restoredResult));
        Assert.Equal(result.Fingerprint, Success(new CalculationEngine().Distribute(restoredRequest)).Fingerprint);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("revision")]
    [InlineData("unit")]
    [InlineData("policy")]
    [InlineData("quantity")]
    public void RequestMustIdentifyItsNumericContract(string problem)
    {
        var request = Request(10, 3);
        request = problem switch
        {
            "identity" => request with { DistributionId = "" }, "revision" => request with { ContentRevision = "" },
            "unit" => request with { UnitId = "" }, "policy" => request with { Policy = null! },
            _ => request with { InputQuantity = null! }
        };
        Assert.True(new CalculationEngine().Distribute(request).IsFailure);
        Assert.Throws<ArgumentNullException>(() => new CalculationEngine().Distribute(null!));
    }

    [Fact]
    public void TechnicalUnitBoundIsInclusiveAndExcessIsRejectedWithoutTruncation()
    {
        var policy = new CalculationDistributionPolicy { Mode = CalculationDistributionMode.Quantized, Quantum = 1 };
        var result = Success(new CalculationEngine().Distribute(Request(16_777_216, 3) with { Policy = policy }));
        Assert.Equal(16_777_216, result.TotalUnits); Assert.Equal(16_777_216d, result.AllocatedTotal);
        Assert.True(new CalculationEngine().Distribute(Request(16_777_218, 3) with { Policy = policy }).IsFailure);
    }

    [Fact]
    public void DistributionDoesNotCreateSettlementOrCallExternalFormulaServices()
    {
        var formulas = new Moq.Mock<Core.Math.IRuntimeFormulaEvaluator>(Moq.MockBehavior.Strict);
        var result = Success(new CalculationEngine(formulas.Object).Distribute(Request(10, 3)));
        Assert.Equal(10, result.AllocatedTotal);
        Assert.Single(result.InputQuantity.IncorporatedStages);
        Assert.All(result.Shares, share => Assert.Single(share.Quantity.IncorporatedStages));
        formulas.VerifyNoOtherCalls();
    }

    [Fact]
    public void ProvenanceAndPolicyChangesAffectAllocationFingerprintsEvenWhenValuesMatch()
    {
        var request = Request(12, 3); var engine = new CalculationEngine();
        var first = Success(engine.Distribute(request));
        var differentParent = Success(engine.Distribute(request with { InputQuantity = request.InputQuantity with { CalculationFingerprint = "different-parent" } }));
        var differentPolicy = Success(engine.Distribute(request with { Policy = new() { RemainderAllocation = CalculationRemainderAllocation.Latest } }));
        Assert.NotEqual(first.Fingerprint, differentParent.Fingerprint); Assert.NotEqual(first.Fingerprint, differentPolicy.Fingerprint);
        Assert.Equal(first.Shares.Select(share => share.Quantity.Value), differentParent.Shares.Select(share => share.Quantity.Value));
        Assert.Equal(first.Shares.Select(share => share.Quantity.Value), differentPolicy.Shares.Select(share => share.Quantity.Value));
    }

    [Fact]
    public void TenAllocationsPreserveInputAndHaveIdenticalSharesTracesAndHashes()
    {
        var request = Request(10, 3); var before = CanonicalJson.ComputeHash(request);
        var results = Enumerable.Range(0, 10).Select(_ => Success(new CalculationEngine().Distribute(request))).ToArray();
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result)).Distinct());
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
        Assert.Equal(3, results[0].Shares.Select(share => share.Quantity.CalculationFingerprint).Distinct().Count());
        Assert.DoesNotContain(results[0].Shares, share => share.Quantity.CalculationFingerprint == request.InputQuantity.CalculationFingerprint);
        Assert.All(results[0].Shares, share => Assert.Equal(request.InputQuantity.IncorporatedStages, share.Quantity.IncorporatedStages));
        Assert.NotEqual(results[0].Fingerprint, Success(new CalculationEngine().Distribute(request with { DistributionId = "other" })).Fingerprint);
    }

    [Fact]
    public void DividedOriginCannotReapplyItsFlatBonusAndEachTargetStageRemainsIndependent()
    {
        var engine = new CalculationEngine(); var pipeline = CalculationProfileTests.Pipeline();
        var captured = Success(engine.Calculate(Calculation("capture", 10, ["origin"]) with { CaptureOnly = true,
            Influences = [new() { InfluenceId = "flat", SourceId = "hero", Channel = "magnitude", Bucket = "flat", Value = 2 }] }, pipeline));
        var divided = Success(engine.Distribute(Request(10, 3) with { InputQuantity = captured.Quantity }));
        Assert.Equal(12, divided.AllocatedTotal);
        foreach (var share in divided.Shares)
        {
            Assert.Equal(4, share.Quantity.Value);
            Assert.True(engine.Calculate(Calculation("duplicate-source", 0, ["origin"]) with { InputQuantity = share.Quantity }, pipeline).IsFailure);
            var target = Success(engine.Calculate(Calculation("target:" + share.Index, 0, ["defense"]) with { InputQuantity = share.Quantity,
                Influences = [new() { InfluenceId = "capacity", SourceId = "enemy", Channel = "magnitude", Bucket = "capacity", Value = share.Index }] }, pipeline));
            Assert.Equal(4 - share.Index, target.Value);
            Assert.Equal(2, target.Quantity.IncorporatedStages.Length);
        }
    }

    [Fact]
    public void TargetScopedInputNeedsExplicitOptInAndNeverLosesItsReceipt()
    {
        var engine = new CalculationEngine(); var captured = Capture(10);
        var target = Success(engine.Calculate(Calculation("target", 0, ["defense"]) with { InputQuantity = captured.Quantity }, CalculationProfileTests.Pipeline()));
        var request = Request(10, 3) with { InputQuantity = target.Quantity };
        Assert.True(engine.Distribute(request).IsFailure);
        var divided = Success(engine.Distribute(request with { Policy = new() { AllowTargetScopedInput = true } }));
        Assert.All(divided.Shares, share =>
        {
            Assert.Equal(target.Quantity.IncorporatedStages, share.Quantity.IncorporatedStages);
            Assert.True(engine.Calculate(Calculation("same-target", 0, ["defense"]) with { InputQuantity = share.Quantity }, CalculationProfileTests.Pipeline()).IsFailure);
        });
    }

    [Fact]
    public void ZeroDiscreteSharesRemainValidQuantitiesWithoutInventingAnExtraStack()
    {
        var request = Request(1, 4, "stacks") with { Policy = new() { Mode = CalculationDistributionMode.Quantized, Quantum = 1 } };
        var result = Success(new CalculationEngine().Distribute(request));
        Assert.Equal(new[] { 1f, 0f, 0f, 0f }, result.Shares.Select(share => share.Quantity.Value));
        Assert.Equal(1, result.Shares.Sum(share => share.Quantity.Value));
        Assert.All(result.Shares, share => Assert.NotEmpty(share.Quantity.CalculationFingerprint));
        // The numerical layer does not apply statuses; skipping a zero-stack impact belongs to the executor integration.
    }

    [Fact]
    public void TargetDefenseSettlesPerShareAgainstLiveCapacityWithoutReapplyingOrigin()
    {
        var engine = new CalculationEngine(); var pipeline = CalculationProfileTests.Pipeline();
        var state = GameplayOwnershipTests.State(); var enemy = state.GetActor("enemy")!;
        var pools = enemy.ResourceState.Resources.ToDictionary(pair => pair.Key, pair => pair.Value);
        pools.Add("shield", ResourcePool.Materialize(new ResourceDefinition { ResourceId = "shield", DisplayName = "Shield", DefaultMax = 5 }, 5, 5));
        state = state.ReplaceActor(enemy with { ResourceState = enemy.ResourceState with { Resources = pools } });
        var initial = CanonicalJson.ComputeHash(state);
        var source = Success(engine.Calculate(Calculation("capture", 10, ["origin"]) with { CaptureOnly = true,
            Influences = [new() { InfluenceId = "bonus", SourceId = "hero", Channel = "magnitude", Bucket = "flat", Value = 2 }] }, pipeline));
        var shares = Success(engine.Distribute(Request(10, 3) with { InputQuantity = source.Quantity }));
        var requested = new List<float>(); var consumed = new List<float>();
        foreach (var share in shares.Shares)
        {
            var context = new CalculationSourceContext { ContentRevision = "revision", Combat = state,
                Actor = state.GetActor("hero"), Target = state.GetActor("enemy"), Pipeline = pipeline, StageIds = ["defense"] };
            var influences = new EntityResourceInfluenceProvider().Collect(context);
            Assert.True(influences.IsSuccess, influences.IsFailure ? influences.Error : null);
            var calculated = Success(engine.Calculate(Calculation("impact:" + share.Index, 0, ["defense"]) with
                { InputQuantity = share.Quantity, Influences = influences.Value }, pipeline));
            var settlements = Success(new CalculationSettlementPlanner().Plan(calculated, pipeline, context));
            requested.Add(calculated.Value); consumed.Add(settlements.Sum(settlement => settlement.Value));
            var applied = Success(new ImmutableEffectProcessor().Apply(state, [new ResolvedEffectCommand
            {
                EffectInstanceId = "impact:" + share.Index, TargetEntityIds = ["enemy"], SourceEntityId = "hero",
                Definition = new() { Type = EffectType.DAMAGE, TargetResource = "focus" }, ResolvedValue = calculated.Value,
                Calculation = calculated, Settlements = settlements.ToImmutableArray(), ContentRevision = "revision"
            }]));
            state = applied.State;
        }
        Assert.Equal(new[] { 0f, 3f, 4f }, requested); Assert.Equal(new[] { 4f, 1f, 0f }, consumed);
        Assert.Equal(0, state.GetActor("enemy")!.GetResource("shield")!.Current);
        Assert.Equal(3, state.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.NotEqual(initial, CanonicalJson.ComputeHash(state));
    }

    private static CalculationDistributionRequest Request(float value, int count, string unit = "points") => new()
    {
        DistributionId = "distribution", ContentRevision = "revision", UnitId = unit,
        InputQuantity = Capture(value, unit).Quantity,
        Recipients = Enumerable.Range(0, count).Select(index => new CalculationDistributionRecipient
            { RecipientId = "impact:" + index, Order = index }).ToArray()
    };

    private static CalculationResult Capture(float value, string unit = "points") => Success(new CalculationEngine().Calculate(
        Calculation("capture", value, ["origin"]) with { UnitId = unit, CaptureOnly = true }, CalculationProfileTests.Pipeline() with { UnitId = unit }));

    private static CalculationRequest Calculation(string id, float value, ImmutableArray<string> stages) => new()
    {
        CalculationId = id, ContentRevision = "revision", UnitId = "points", Channel = "magnitude", BaseValue = value,
        StageIds = stages, StageContextIds = new Dictionary<string, string> { ["origin"] = "hero", ["defense"] = "enemy" }.ToImmutableSortedDictionary(StringComparer.Ordinal)
    };

    private static T Success<T>(Result<T> result)
    { Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null); return result.Value; }
}
