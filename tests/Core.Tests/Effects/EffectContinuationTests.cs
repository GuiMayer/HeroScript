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

public sealed class EffectContinuationTests
{
    [Fact]
    public void FifteenOnTenTransportsFiveAndNewTargetConsumesItsOwnDefense()
    {
        var result = Execute(Hit(15), World(secondShield: 2));
        Assert.Equal(0, Resource(result, "enemy"));
        Assert.Equal(7, Resource(result, "next"));
        Assert.Equal(0, result.State.GetActor("next")!.GetResource("shield")!.Current);
        var trace = result.Steps[0].Continuation!;
        Assert.Equal(5, trace.Overflow!.Value);
        Assert.Equal("points", trace.Overflow.Quantity.UnitId);
        Assert.Equal("next", trace.ToEntityId);
        Assert.Equal("no_causal_defeat", result.Steps[1].Continuation!.StopReason);
        Assert.Equal(result.Steps[0].Identity!.ProcId, result.Steps[1].Identity!.ProcId);
        Assert.NotEqual(result.Steps[0].Identity!.ImpactId, result.Steps[1].Identity!.ImpactId);
        Assert.Equal(result.Steps[0].Identity!.ImpactId, result.Steps[1].Identity!.ParentImpactId);
        Assert.Equal(result.Steps[0].StateAfterHash, result.Steps[1].StateBeforeHash);
        Assert.Equal(new[] { "origin", "defense", "overflow_remainder", "defense" },
            result.Steps[1].Calculation!.Quantity.IncorporatedStages.Select(stage => stage.StageId));
    }

    [Fact]
    public void OriginBonusIsCapturedOnceAndNeverRescaledForNextTarget()
    {
        var card = new EffectiveCardDefinition { DefinitionId = "card", Components = [new CardInfluenceComponentDefinition
        { ComponentId = "bonus", Channel = "magnitude", Bucket = "flat", Value = 5 }] };
        var result = Execute(Hit(10), World(), card);
        Assert.Equal(5, Resource(result, "next"));
        Assert.Equal(5, result.Steps[0].Continuation!.Overflow!.Value);
        Assert.Single(result.Steps[1].Calculation!.Quantity.IncorporatedStages.Where(stage => stage.Scope == CalculationStageScope.Actor));
    }

    [Theory]
    [InlineData(10, "no_overflow")]
    [InlineData(9, "no_causal_defeat")]
    public void ZeroOrNonLethalImpactDoesNotSelectOrMutateNextTarget(int value, string reason)
    {
        var initial = World();
        var result = Execute(Hit(value), initial);
        Assert.Single(result.Steps);
        Assert.Equal(reason, result.Steps[0].Continuation!.StopReason);
        Assert.Equal(initial.Determinism.RandomState, result.State.Determinism.RandomState);
        Assert.Equal(10, Resource(result, "next"));
    }

    [Fact]
    public void AlreadyDefeatedSelectionAndCallerForgedFactsFailWithoutMutation()
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Hit(15), World());
        var original = CanonicalJson.ComputeHash(request);
        Assert.True(fixture.Executor.Execute(request with { Variables = new Dictionary<string, float> { ["continuation.applied_change"] = 0 } }).IsFailure);
        Assert.True(fixture.Executor.Execute(request with { Quantities = ImmutableSortedDictionary<string, CalculationQuantity>.Empty.Add("continuation.budget", new()) }).IsFailure);
        Assert.Equal(original, CanonicalJson.ComputeHash(request));
        Assert.True(fixture.Executor.Execute(request with { Combat = EffectSequenceBudgetTests.DefeatAtZero(request.Combat, "enemy", 0) }).IsFailure);
    }

    [Theory]
    [InlineData(1, "hop_limit", 2)]
    [InlineData(8, "no_next_target", 3)]
    public void ChainStopsAtExplicitLimitOrExhaustedCandidates(int limit, string reason, int impacts)
    {
        var world = World();
        var third = world.GetActor("next")! with { InstanceId = "third", ResourceState = world.GetActor("next")!.ResourceState with { OwnerId = "third" } };
        world = world with { Actors = world.Actors.Append(new KeyValuePair<string, CombatActorState>("third", third)).ToDictionary() };
        var effect = Hit(40) with { Continuation = Policy() with { MaximumHops = limit } };
        var result = Execute(effect, world);
        Assert.Equal(impacts, result.Steps.Length);
        Assert.Equal(reason, result.Steps[^1].Continuation!.StopReason);
        Assert.Equal(impacts, result.Steps.Select(step => step.TargetEntityId).Distinct().Count());
    }

    [Fact]
    public void NoRemainingTargetIsAValidTerminationAndDoesNotConsumeRng()
    {
        var world = World();
        world = world with { Actors = world.Actors.Where(pair => pair.Key != "next").ToDictionary() };
        var result = Execute(Hit(15), world);
        Assert.Equal("no_next_target", Assert.Single(result.Steps).Continuation!.StopReason);
        Assert.Equal(world.Determinism.RandomState, result.State.Determinism.RandomState);
    }

    [Fact]
    public void ChildDefeatCannotAuthorizeParentContinuation()
    {
        var effect = Hit(1) with { ChainedEffects = [EffectTransactionTests.Resource(EffectType.DAMAGE, 30)] };
        // Child uses the ordinary configured Amount contract, but is not itself authorized to continue.
        effect = effect with { ChainedEffects = [Hit(30) with { Continuation = null }] };
        var result = Execute(effect, World());
        Assert.Equal(2, result.Steps.Length);
        Assert.Equal("no_causal_defeat", result.Steps[0].Continuation!.StopReason);
        Assert.Equal(10, Resource(result, "next"));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ChildrenOnlyFollowTheContinuationWhenExplicitlyAuthorized(bool carry, int childCount)
    {
        var child = Hit(1) with { Target = EffectTarget.SELF, Continuation = null };
        var result = Execute(Hit(15) with { Continuation = Policy() with { CarryChildren = carry }, ChainedEffects = [child] }, World());
        Assert.Equal(childCount, result.Steps.Count(step => step.TargetEntityId == "hero"));
    }

    [Fact]
    public void MultiHitCarriesOnlyCurrentShareAndRemainingSharesUseTheirOwnLossPolicy()
    {
        var effect = EffectSequenceBudgetTests.Damage(30, 2) with { Continuation = Policy(),
            TargetLoss = new() { Policy = EffectTargetLossPolicy.Retarget, Retarget = EffectTarget.LOWEST_RESOURCE_ENEMY }, SelectionResourceId = "focus" };
        var result = Execute(effect, World());
        var impacts = result.Steps.Where(step => step.Applied).ToArray();
        Assert.Equal(new[] { 15f, 5f, 15f }, impacts.Select(step => step.Calculation!.Value));
        Assert.Equal(new[] { "enemy", "next", "next" }, impacts.Select(step => step.TargetEntityId));
        Assert.Equal(2, result.Steps[0].SequenceBudgets[0].Allocation.Shares.Length);
    }

    [Fact]
    public void CondensationConsumesOnceAndOnlyItsAuthorizedResourceOutputContinues()
    {
        var policy = Policy() with { ImpactStageIds = ["count_impact"] };
        var emitted = Hit(0) with { Continuation = policy, Parameters = [new() { Parameter = EffectNumericParameter.Amount,
            InputQuantityId = "condensation.count", UnitId = "stacks", Channel = "counts", PipelineId = "counts", StageIds = ["count_source", "count_impact"] }] };
        var recipe = CondensationTests.Recipe(emitted);
        var fixture = EffectSequenceBudgetTests.Fixture(recipe: recipe, extraProfile: Overflow() with { UnitId = "stacks" },
            evaluator: new RuntimeFormulaEvaluator(Mock.Of<IMathEngine>(), new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance));
        var stacked = Success(fixture.Executor.Execute(Request(fixture.Run,
            EffectSequenceBudgetTests.Stacks(EffectNumericParameter.StatusStacks, 15, 1), World())));
        var result = Success(fixture.Executor.Execute(Request(stacked.Run!, CondensationTests.Condense(), stacked.State)));
        var condensation = Assert.Single(result.Records, record => record.Condensation != null);
        Assert.Equal(5, Resource(result, "next"));
        Assert.Empty(result.State.StatusEffects.GetValueOrDefault("enemy", []));
        Assert.All(result.Steps, step => Assert.Equal(condensation.Identity!.ProcId, step.Identity!.ProcId));
        Assert.Equal("stacks", result.Steps[1].Continuation!.Overflow!.Quantity.UnitId);
    }

    [Fact]
    public void ChanceAndConditionAreOnlyRetestedWhenContentOptsIn()
    {
        var fixture = Fixture();
        var world = World();
        var policy = Policy() with { Selector = EffectTarget.RANDOM_ENEMY, RetestCondition = true };
        var effect = Hit(15) with { Condition = "target.resources.focus.current - 9", Continuation = policy };
        world = EffectSequenceBudgetTests.DefeatAtZero(world, "next", 5);
        var blocked = Success(fixture.Executor.Execute(Request(fixture.Run, effect, world)));
        Assert.Equal("condition", blocked.Steps[^1].SkipReason);
        Assert.Equal(5, Resource(blocked, "next"));
        var carried = Success(fixture.Executor.Execute(Request(fixture.Run, effect with { Continuation = policy with { RetestCondition = false } }, world)));
        Assert.Equal(0, Resource(carried, "next"));
    }

    [Fact]
    public void LastFailureRollsBackDefeatsSettlementsHopsAndRng()
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Hit(15), World());
        request = request with { Trigger = request.Trigger with { Effects = [Hit(15), Hit(1) with { TargetResource = "missing" }] } };
        var original = CanonicalJson.ComputeHash(request);
        Assert.True(fixture.Executor.Execute(request).IsFailure);
        Assert.Equal(original, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void TenExecutionsAndTraceRoundTripAreIdentical()
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Hit(15), World(2));
        var results = Enumerable.Range(0, 10).Select(_ => Success(fixture.Executor.Execute(request))).ToArray();
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result)).Distinct());
        var restored = JsonSerializer.Deserialize<EffectExecutionStep[]>(JsonSerializer.Serialize(results[0].Steps))!;
        Assert.Equal(CanonicalJson.ComputeHash(results[0].Steps), CanonicalJson.ComputeHash(restored));
        Assert.Equal(CanonicalJson.ComputeHash(Hit(15)), CanonicalJson.ComputeHash(JsonSerializer.Deserialize<EffectDefinition>(JsonSerializer.Serialize(Hit(15)))!));
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("unit")]
    [InlineData("stage")]
    [InlineData("selector")]
    [InlineData("hop")]
    public void PublicationRejectsInvalidContinuationEvenInDormantChildren(string mutation)
    {
        var effect = Hit(15);
        var profiles = EffectSequenceBudgetTests.Profiles();
        profiles.Add("overflow", Overflow());
        effect = mutation switch {
            "origin" => effect with { Continuation = Policy() with { ImpactStageIds = ["origin"] } },
            "stage" => effect with { Continuation = Policy() with { OverflowStageIds = ["missing"] } },
            "selector" => effect with { Continuation = Policy() with { Selector = EffectTarget.ALL_ENEMIES } },
            "hop" => effect with { Continuation = Policy() with { MaximumHops = 100 } }, _ => effect };
        if (mutation == "unit") profiles["overflow"] = Overflow() with { UnitId = "foreign" };
        var graph = new ContentGraphValidator().Validate(EffectSequenceBudgetTests.Bundle(profiles,
            Hit(0) with { Chance = 0, Continuation = null, ChainedEffects = [effect] }));
        Assert.False(graph.IsValid);
        Assert.Contains(graph.Errors, error => error.Contains("continuation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuthoredOverflowAndCriticalInputsAreAcceptedByPipelinePublication()
    {
        var profiles = EffectSequenceBudgetTests.Profiles();
        profiles.Add("overflow", Overflow());
        Assert.True(new ContentGraphValidator().Validate(EffectSequenceBudgetTests.Bundle(profiles, Hit(15))).IsValid);
        profiles["overflow"] = Overflow() with { Buckets = [new() { BucketId = "remainder", StageId = "overflow_remainder",
            Operation = CalculationBucketOperation.Formula, Formula = "rolls.critical.success + 1 * bucket.input" }] };
        Assert.True(new ContentGraphValidator().Validate(EffectSequenceBudgetTests.Bundle(profiles)).IsValid);
    }

    internal static EffectContinuationDefinition Policy() => new() { MaximumHops = 8,
        Selector = EffectTarget.LOWEST_RESOURCE_ENEMY, SelectionResourceId = "focus",
        OverflowPipelineId = "overflow", OverflowChannel = "overflow", OverflowStageIds = ["overflow_remainder"], ImpactStageIds = ["defense"] };
    internal static CalculationPipelineDefinition Overflow() => new() { PipelineId = "overflow", Channel = "overflow", UnitId = "points",
        Stages = [new() { StageId = "overflow_remainder", Scope = CalculationStageScope.Target }], Buckets = [new()
        { BucketId = "remainder", StageId = "overflow_remainder", Operation = CalculationBucketOperation.Formula,
            Formula = "continuation.requested_change - continuation.applied_change * -1" }] };
    internal static EffectDefinition Hit(float value) => new() { Type = EffectType.DAMAGE, TargetResource = "focus", Continuation = Policy(),
        Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = value, Channel = "magnitude", PipelineId = "scoped", UnitId = "points",
            StageIds = ["origin", "defense"] }] };
    private static (EffectTriggerExecutor Executor, RunState Run) Fixture() => EffectSequenceBudgetTests.Fixture(extraProfile: Overflow(),
        evaluator: new RuntimeFormulaEvaluator(Mock.Of<IMathEngine>(), new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance));
    private static CombatState World(float secondShield = 0)
    {
        var world = EffectSequenceBudgetTests.DefeatAtZero(GameplayOwnershipTests.State(), "enemy", 10);
        var next = world.GetActor("enemy")! with { InstanceId = "next", ResourceState = world.GetActor("enemy")!.ResourceState with { OwnerId = "next" } };
        next = next with { ResourceState = next.ResourceState with { Resources = next.ResourceState.Resources.Append(new KeyValuePair<string, ResourcePool>("shield",
            ResourcePool.Materialize(new ResourceDefinition { ResourceId = "shield", DisplayName = "Shield", DefaultMax = secondShield }, secondShield, secondShield))).ToDictionary() } };
        return world with { Actors = world.Actors.Where(pair => pair.Key != "neutral")
            .Append(new KeyValuePair<string, CombatActorState>("next", next)).ToDictionary() };
    }
    private static EffectTriggerExecutionRequest Request(RunState run, EffectDefinition effect, CombatState state) =>
        EffectTransactionTests.Request(effect) with { Run = run, Combat = state };
    private static EffectBatchResult Execute(EffectDefinition effect, CombatState state, EffectiveCardDefinition? card = null)
    { var fixture = Fixture(); return Success(fixture.Executor.Execute(Request(fixture.Run, effect, state) with { Card = card })); }
    private static float Resource(EffectBatchResult result, string id) => result.State.GetActor(id)!.GetResource("focus")!.Current;
    private static EffectBatchResult Success(Result<EffectBatchResult> result)
    { Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null); return result.Value; }
}
