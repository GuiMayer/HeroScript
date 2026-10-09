using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Resources;
using Core.Combat.Models;
using Xunit;

namespace Core.Tests.Effects;

public sealed class CondensationTests
{
    [Theory]
    [InlineData(EffectType.HEAL)]
    [InlineData(EffectType.MODIFY_RESOURCE)]
    [InlineData(EffectType.DAMAGE)]
    public void ResourceOperationsConsumeAllAndActivateOnceWithoutKnowingResourcePurpose(EffectType type)
    {
        var output = StackPayloadTests.Activate() with { Type = type, Parameters = [StackPayloadTests.Activate().Parameters[0] with
        { InputQuantityId = "condensation.power" }] };
        var recipe = Recipe(output) with { Aggregates = [new() { ParameterId = "power", Kind = CondensationAggregateKind.Payload, PayloadParameterId = "potency" }] };
        var fixture = StackPayloadTests.Fixture(recipe: recipe);
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2), StackPayloadTests.Apply(3))));
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, Condense()) with { Combat = applied.State }));
        var condensed = Assert.Single(result.Records, record => record.Condensation != null);
        Assert.Equal(5, Assert.Single(condensed.StackChanges).PreviousStacks);
        Assert.Equal(EffectStackChangeReason.Consume, condensed.StackChanges[0].Reason);
        Assert.Empty(result.State.StatusEffects.GetValueOrDefault("enemy", []));
        var resource = Assert.Single(result.Records, record => record.EffectType == type);
        Assert.Equal("focus", resource.ResourceId);
        Assert.Equal(65, result.Calculations.Last().Value);
        Assert.Single(result.Records.Select(record => record.Identity!.ProcId).Distinct());
        Assert.Equal(2, result.Records.Select(record => record.Identity!.ImpactId).Distinct().Count());
    }

    [Theory]
    [InlineData(EffectType.APPLY_STATUS)]
    [InlineData(EffectType.APPLY_MODIFIER)]
    [InlineData(EffectType.CARD_ZONE_FLOW)]
    public void CountCanFeedDiscreteEffectsThroughTheSamePipeline(EffectType type)
    {
        var field = type == EffectType.APPLY_STATUS ? EffectNumericParameter.StatusStacks :
            type == EffectType.APPLY_MODIFIER ? EffectNumericParameter.ModifierStacks : EffectNumericParameter.CardCount;
        var output = new EffectDefinition { Type = type, StatusId = type == EffectType.APPLY_STATUS ? "charges" : null,
            ModifierId = type == EffectType.APPLY_MODIFIER ? "charges" : null,
            CardZoneFlowId = type == EffectType.CARD_ZONE_FLOW ? "draw" : null, Parameters = [CountParameter(field)] };
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(output));
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2))));
        var oldId = applied.State.StatusEffects["enemy"][0].InstanceId;
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, Condense()) with { Combat = applied.State }));
        var root = Assert.Single(result.Records, record => record.Condensation != null);
        Assert.Equal(2, root.Condensation!.Inputs["condensation.count"].Value);
        Assert.Single(result.Records.Select(record => record.Identity!.ProcId).Distinct());
        var emitted = Assert.Single(result.Records, record => record.EffectType == type);
        if (type == EffectType.APPLY_STATUS)
        {
            var status = Assert.Single(result.State.StatusEffects["enemy"]);
            Assert.NotEqual(oldId, status.InstanceId);
            Assert.Equal(2, status.Stacks);
        }
        else if (type == EffectType.APPLY_MODIFIER) Assert.Equal(2, Assert.Single(result.Run!.Modifiers).Stacks);
        else
        {
            Assert.Equal(2, emitted.CardInstanceIds.Length);
            Assert.Equal(2, result.Run!.Deck.Topology.GetZone("active", "$run")!.InstanceIds.Count);
        }
    }

    [Fact]
    public void BothStoresAndDifferentInstancesFormOneActivation()
    {
        var fixture = StackPayloadTests.Fixture(stacking: StackReapplyPolicy.Independent, recipe: Recipe(CountResource()));
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2),
            StackPayloadTests.Apply(1), StackPayloadTests.ApplyModifier(3))));
        var result = Success(fixture.Executor.Execute(Request(prepared.Run!, Condense()) with { Combat = prepared.State }));
        var root = Assert.Single(result.Records, record => record.Condensation != null);
        Assert.Equal(3, root.Condensation!.Selection.Sources.Length);
        Assert.Equal(6, root.Condensation.Inputs["condensation.count"].Value);
        Assert.All(root.StackChanges, change => Assert.Equal(0, change.CurrentStacks));
        Assert.Empty(result.Run!.Modifiers);
        Assert.Empty(result.State.StatusEffects.GetValueOrDefault("enemy", []));
        Assert.Single(result.Records, record => record.ResourceId != null);
        Assert.Single(result.Records.Select(record => record.Identity!.ProcId).Distinct());
    }

    [Theory]
    [InlineData(CondensationAbsencePolicy.Skip, true)]
    [InlineData(CondensationAbsencePolicy.Fail, false)]
    public void EmptySelectionHasExplicitPolicy(CondensationAbsencePolicy policy, bool passes)
    {
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(CountResource()) with { EmptySelection = policy });
        var request = Request(fixture.Run, Condense());
        var result = fixture.Executor.Execute(request);
        Assert.Equal(passes, result.IsSuccess);
        if (passes)
        {
            Assert.Empty(result.Value.Records);
            Assert.Equal("empty_selection", Assert.Single(result.Value.Steps).SkipReason);
            Assert.Equal(CanonicalJson.ComputeHash(request.Combat), CanonicalJson.ComputeHash(result.Value.State));
        }
    }

    [Fact]
    public void ChanceFailureDoesNotConsumeOrRollPerStack()
    {
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(CountResource()));
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(8))));
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, Condense() with { Chance = 0 }) with { Combat = prepared.State }));
        Assert.Equal(8, result.State.StatusEffects["enemy"][0].Stacks);
        Assert.Equal("chance", result.Steps[0].SkipReason);
        Assert.Equal(prepared.State.Determinism, result.State.Determinism);
    }

    [Fact]
    public void NamedCriticalInputDoesNotMultiplyCondensedStacksOrRerollRecipeImplicitly()
    {
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(CountResource()));
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(8))));
        var effect = Condense() with { RandomInputs = [new() { InputId = "critical", Chance = .5f, Scope = EffectRandomScope.Impact }] };
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, effect) with { Combat = prepared.State }));
        var root = Assert.Single(result.Records, record => record.Condensation != null);
        Assert.Equal(8, root.Condensation!.Inputs["condensation.count"].Value);
        Assert.Equal(8, result.Calculations.Last().Value);
        Assert.Single(result.Steps.SelectMany(step => step.RandomInputs));
        Assert.Equal(prepared.State.Determinism.DrawDouble().Context.RandomState, result.State.Determinism.RandomState);
    }

    [Fact]
    public void RepeatedParentDoesNotConsumeNewStacksProducedByTheRecipe()
    {
        var output = StackPayloadTests.Apply(1) with { StatusStacks = null, Parameters = [CountParameter(EffectNumericParameter.StatusStacks)] };
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(output));
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2))));
        var repeated = CountResource() with { Parameters = [CountParameter(EffectNumericParameter.Amount) with
        { InputQuantityId = null, FlatValue = 0 }], Repeat = 3, ChainedEffects = [Condense()] };
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, repeated) with { Combat = prepared.State }));
        Assert.Single(result.Records, record => record.Condensation != null);
        Assert.Equal(2, result.State.StatusEffects["enemy"][0].Stacks);
        Assert.Equal(2, result.Steps.Count(step => step.SkipReason == "already_attempted"));
    }

    [Theory]
    [InlineData(CondensationConflictPolicy.Fail, false)]
    [InlineData(CondensationConflictPolicy.Skip, true)]
    public void ActionStartSnapshotDoesNotSilentlyConsumeChangedInstances(CondensationConflictPolicy conflict, bool passes)
    {
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(CountResource()) with { Conflict = conflict });
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2))));
        var request = Request(fixture.Run, StackPayloadTests.Apply(1), Condense()) with { Combat = prepared.State };
        var hash = CanonicalJson.ComputeHash(request);
        var result = fixture.Executor.Execute(request);
        Assert.Equal(passes, result.IsSuccess);
        Assert.Equal(hash, CanonicalJson.ComputeHash(request));
        if (passes)
        {
            Assert.Equal(3, result.Value.State.StatusEffects["enemy"][0].Stacks);
            Assert.Equal("selection_changed", result.Value.Steps.Last().SkipReason);
            Assert.DoesNotContain(result.Value.Records, record => record.Condensation != null);
        }
    }

    [Fact]
    public void CurrentSnapshotIncludesStacksAppliedEarlierInTheAction()
    {
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(CountResource()) with { SelectionTiming = CondensationSelectionTiming.Current });
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(3), Condense())));
        Assert.Equal(3, Assert.Single(result.Records, record => record.Condensation != null).Condensation!.Inputs["condensation.count"].Value);
        Assert.Empty(result.State.StatusEffects.GetValueOrDefault("enemy", []));
    }

    [Fact]
    public void ActionStartSelectionDoesNotExpandToNewIndependentInstances()
    {
        var fixture = StackPayloadTests.Fixture(stacking: StackReapplyPolicy.Independent, recipe: Recipe(CountResource()));
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(1))));
        var oldId = prepared.State.StatusEffects["enemy"][0].InstanceId;
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2), Condense()) with { Combat = prepared.State }));
        var retained = Assert.Single(result.State.StatusEffects["enemy"]);
        Assert.NotEqual(oldId, retained.InstanceId);
        Assert.Equal(2, retained.Stacks);
        var root = Assert.Single(result.Records, record => record.Condensation != null);
        Assert.Equal(1, root.Condensation!.Inputs["condensation.count"].Value);
    }

    [Fact]
    public void MultiTargetRecipeHasOneProcWithDistinctImpacts()
    {
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(CountResource() with { Target = EffectTarget.ALL_ENEMIES }));
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(1))));
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, Condense()) with { Combat = prepared.State }));
        Assert.Single(result.Records.Select(record => record.Identity!.ProcId).Distinct());
        Assert.Equal(3, result.Records.Count);
        Assert.Equal(3, result.Records.Select(record => record.Identity!.ImpactId).Distinct().Count());
        Assert.Equal(new[] { "enemy", "neutral" }, result.Records.Where(record => record.ResourceId != null).Select(record => record.TargetEntityId));
    }

    [Theory]
    [InlineData(CondensationEvaluationTiming.BeforeConsumption, 28)]
    [InlineData(CondensationEvaluationTiming.AfterConsumption, 26)]
    public void RemainingCalculationStagesUseConfiguredWorldSnapshot(CondensationEvaluationTiming timing, float expected)
    {
        var recipe = Recipe(StackPayloadTests.Activate() with { Parameters = [StackPayloadTests.Activate().Parameters[0] with
        { InputQuantityId = "condensation.power" }] }) with { EvaluationTiming = timing,
            Aggregates = [new() { ParameterId = "power", Kind = CondensationAggregateKind.Payload, PayloadParameterId = "potency" }] };
        var fixture = StackPayloadTests.Fixture(recipe: recipe, targetStackInfluence: true);
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2))));
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, Condense()) with { Combat = prepared.State }));
        Assert.Equal(expected, result.Calculations.Last().Value);
    }

    [Fact]
    public void DefeatedActivationTargetCanSkipWithoutConsuming()
    {
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(CountResource()));
        var state = GameplayOwnershipTests.State();
        var enemy = state.GetActor("enemy")!;
        var definition = enemy.GetResource("focus")!.Definition! with { ThresholdPolicies = [new()
        { PolicyId = "defeat", Comparison = ResourceThresholdComparison.LessThanOrEqual,
            ThresholdSource = ResourceThresholdSource.Minimum, Consequence = ResourceThresholdConsequence.DefeatOwner }] };
        state = state.ReplaceActor(enemy with { ResourceState = enemy.ResourceState with
        { Resources = new Dictionary<string, ResourcePool> { ["focus"] = ResourcePool.Materialize(definition, 1, 20) } } });
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2)) with { Combat = state }));
        var defeat = CountResource() with { Type = EffectType.DAMAGE, Parameters = [CountParameter(EffectNumericParameter.Amount) with
        { InputQuantityId = null, FlatValue = 1 }] };
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, defeat, Condense() with
        { TargetLoss = new() { Policy = EffectTargetLossPolicy.Skip } }) with { Combat = prepared.State }));
        Assert.False(result.State.GetActor("enemy")!.IsAlive);
        Assert.Equal(2, result.State.StatusEffects["enemy"][0].Stacks);
        Assert.DoesNotContain(result.Records, record => record.Condensation != null);
        Assert.Equal("target_defeated", result.Steps.Last().SkipReason);
    }

    [Fact]
    public void RecipeExpansionLimitIsValidatedBeforeConsumption()
    {
        var recipe = Recipe(CountResource() with { Repeat = 256, ChainedEffects = [CountResource() with { Repeat = 256 }] });
        var fixture = StackPayloadTests.Fixture(recipe: recipe);
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(1))));
        var result = fixture.Executor.Execute(Request(fixture.Run, Condense()) with { Combat = prepared.State });
        Assert.True(result.IsFailure);
        Assert.Contains("limit", result.Error);
        Assert.Equal(1, prepared.State.StatusEffects["enemy"][0].Stacks);
    }

    [Theory]
    [InlineData(CondensationEvaluationTiming.BeforeConsumption, 30)]
    [InlineData(CondensationEvaluationTiming.AfterConsumption, 26)]
    public void InfluenceEvaluationTimingIsExplicit(CondensationEvaluationTiming timing, float expected)
    {
        var output = StackPayloadTests.Activate() with { Parameters = [StackPayloadTests.Activate().Parameters[0] with
        { InputQuantityId = "condensation.power" }] };
        var recipe = Recipe(output) with { EvaluationTiming = timing,
            Aggregates = [new() { ParameterId = "power", Kind = CondensationAggregateKind.Payload, PayloadParameterId = "potency" }] };
        var fixture = StackPayloadTests.Fixture(StackParameterEvaluation.Dynamic, recipe: recipe, stackInfluence: true);
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2))));
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, Condense()) with { Combat = prepared.State }));
        Assert.Equal(expected, Assert.Single(result.Records, record => record.Condensation != null).Condensation!.Inputs["condensation.power"].Value);
    }

    [Theory]
    [InlineData(CondensationZeroPolicy.Consume, true)]
    [InlineData(CondensationZeroPolicy.Fail, false)]
    public void ZeroApplicationDoesNotInferWhetherToConsume(CondensationZeroPolicy policy, bool passes)
    {
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(CountResource() with { Operation = ResourceEffectOperation.SET,
            Parameters = [CountParameter(EffectNumericParameter.Amount) with { InputQuantityId = null, FlatValue = 10 }] }) with { ZeroApplication = policy });
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2))));
        var result = fixture.Executor.Execute(Request(fixture.Run, Condense()) with { Combat = prepared.State });
        Assert.Equal(passes, result.IsSuccess);
        Assert.Equal(2, prepared.State.StatusEffects["enemy"][0].Stacks);
        if (passes) Assert.Empty(result.Value.State.StatusEffects.GetValueOrDefault("enemy", []));
    }

    [Fact]
    public void FailureInLastActivationComponentRollsBackConsumptionZonesAndEarlierEffects()
    {
        var recipe = Recipe(new() { Type = EffectType.CARD_ZONE_FLOW, CardZoneFlowId = "draw",
            Parameters = [CountParameter(EffectNumericParameter.CardCount)] }, CountResource(), CountResource() with
        { Parameters = [CountParameter(EffectNumericParameter.Amount) with { InputQuantityId = null, FormulaValue = "missing" }] });
        var fixture = StackPayloadTests.Fixture(recipe: recipe);
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2))));
        var request = Request(fixture.Run, Condense()) with { Combat = prepared.State };
        var before = CanonicalJson.ComputeHash(request);
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
        Assert.Equal(2, request.Combat.StatusEffects["enemy"][0].Stacks);
        Assert.Empty(request.Run!.Deck.Topology.GetZone("active", "$run")!.InstanceIds);
    }

    [Fact]
    public void MixedRevisionAndRecursiveOrUnknownInputsFailExplicitly()
    {
        var fixture = StackPayloadTests.Fixture(recipe: Recipe(CountResource()));
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2))));
        var status = prepared.State.StatusEffects["enemy"][0];
        var mixed = prepared.State with { StatusEffects = prepared.State.StatusEffects.SetItem("enemy", [status with { ContentRevision = "other" }]) };
        Assert.True(fixture.Executor.Execute(Request(fixture.Run, Condense()) with { Combat = mixed }).IsFailure);
        Assert.True(CondensationRecipeValidator.Validate(Recipe(Condense())).IsFailure);
        Assert.True(CondensationRecipeValidator.Validate(Recipe(CountResource() with { Parameters = [CountParameter(EffectNumericParameter.Amount)
            with { InputQuantityId = "condensation.unknown" }] })).IsFailure);
        Assert.True(fixture.Executor.Execute(Request(fixture.Run, Condense() with { CondensationRecipeId = "unknown", Chance = 0 })).IsFailure);
    }

    [Fact]
    public void TenRunsAndSerializedRecipeProduceSameSelectionInputsProcAndFinalState()
    {
        var recipe = Recipe(CountResource()) with { Scope = CondensationScope.OncePerAction };
        var restored = JsonSerializer.Deserialize<CondensationRecipeDefinition>(JsonSerializer.Serialize(recipe))!;
        Assert.Equal(CanonicalJson.ComputeHash(recipe), CanonicalJson.ComputeHash(restored));
        var fixture = StackPayloadTests.Fixture(recipe: restored);
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, StackPayloadTests.Apply(2), StackPayloadTests.ApplyModifier(3))));
        var request = Request(prepared.Run!, Condense()) with { Combat = prepared.State };
        var results = Enumerable.Range(0, 10).Select(_ => Success(fixture.Executor.Execute(request))).ToArray();
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result)).Distinct());
        var root = Assert.Single(results[0].Records, record => record.Condensation != null);
        var restoredOutcome = JsonSerializer.Deserialize<CondensationOutcome>(JsonSerializer.Serialize(root.Condensation));
        Assert.Equal(CanonicalJson.ComputeHash(root.Condensation), CanonicalJson.ComputeHash(restoredOutcome));
    }

    internal static CondensationRecipeDefinition Recipe(params EffectDefinition[] effects) => new()
    { RecipeId = "test", Aggregates = [new() { ParameterId = "count", Kind = CondensationAggregateKind.StackCount }], Effects = effects.ToImmutableArray() };
    internal static EffectDefinition Condense() => new() { Type = EffectType.CONDENSE_STACKS, CondensationRecipeId = "test" };
    internal static EffectDefinition CountResource() => new()
    { Type = EffectType.MODIFY_RESOURCE, TargetResource = "focus", Operation = ResourceEffectOperation.ADD, Parameters = [CountParameter(EffectNumericParameter.Amount)] };
    internal static EffectNumericParameterDefinition CountParameter(EffectNumericParameter parameter) => new()
    { Parameter = parameter, InputQuantityId = "condensation.count", UnitId = "stacks", Channel = "counts", PipelineId = "counts",
        Conversion = new() { RequireInteger = true }, StageIds = ["count_application"] };
    private static EffectTriggerExecutionRequest Request(RunState run, params EffectDefinition[] effects) => EffectTransactionTests.Request(effects) with { Run = run };
    private static EffectBatchResult Success(Result<EffectBatchResult> result)
    { Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null); return result.Value; }
}
