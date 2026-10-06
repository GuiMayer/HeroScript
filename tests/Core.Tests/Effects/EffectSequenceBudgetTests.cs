using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.StatusEffects;
using Core.Tests.Calculations;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class EffectSequenceBudgetTests
{
    [Fact]
    public void SourceBonusIsCapturedOnceAndDefenseSettlesAgainstEachLiveImpact()
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Damage(10, 3)) with
        {
            Combat = Shield(GameplayOwnershipTests.State(), 5),
            Card = new() { Components = [new CardInfluenceComponentDefinition
                { ComponentId = "bonus", Channel = "magnitude", Bucket = "flat", Value = 2 }] }
        };
        var before = CanonicalJson.ComputeHash(request);
        var result = Success(fixture.Executor.Execute(request));
        var budget = Assert.Single(result.Steps.SelectMany(step => step.SequenceBudgets));
        Assert.Equal(12, budget.Capture.Value);
        Assert.True(budget.Capture.CaptureOnly);
        Assert.Equal(new[] { 4f, 4f, 4f }, budget.Allocation.Shares.Select(share => share.Quantity.Value));
        Assert.Equal(new[] { 0f, 3f, 4f }, result.Steps.Select(step => step.Calculation!.Value));
        Assert.Equal(3, result.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(0, result.State.GetActor("enemy")!.GetResource("shield")!.Current);
        Assert.Equal(4, result.Calculations.Count);
        Assert.All(result.Steps, step => Assert.Equal(2, step.Calculation!.Quantity.IncorporatedStages.Length));
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void TraditionalRepeatsStillRecalculateTheCompleteEffect()
    {
        var fixture = Fixture();
        var distributed = Damage(2, 3);
        var regular = distributed with { Parameters = [distributed.Parameters[0] with { Distribution = null, StageIds = [] }] };
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, regular) with
        { Card = new() { Components = [new CardInfluenceComponentDefinition
            { ComponentId = "bonus", Channel = "magnitude", Bucket = "flat", Value = 1 }] } }));
        Assert.Equal(new[] { 3f, 3f, 3f }, result.Calculations.Select(calculation => calculation.Value));
        Assert.Equal(1, result.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.All(result.Steps, step => { Assert.Empty(step.SequenceBudgets); Assert.Empty(step.ImpactShares); });
    }

    [Fact]
    public void SourceFormulaUsesSequenceEntryStateEvenWhenChildrenChangeTheSource()
    {
        var fixture = Fixture();
        var damage = Damage(0, 3);
        var child = Damage(1, 1) with { Target = EffectTarget.SELF,
            Parameters = [Damage(1, 1).Parameters[0] with { Distribution = null, StageIds = [] }] };
        damage = damage with { Parameters = [damage.Parameters[0] with
            { FlatValue = null, FormulaValue = "source.resources.focus.current" }], ChainedEffects = [child] };
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, damage)));
        Assert.Equal(new[] { 4f, 3f, 3f }, result.Steps.Where(step => !step.ImpactShares.IsEmpty)
            .Select(step => step.ImpactShares[0].Share.Quantity.Value));
        Assert.Equal(7, result.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Single(result.Steps.SelectMany(step => step.SequenceBudgets));
    }

    [Theory]
    [InlineData(EffectNumericParameter.StatusStacks, 10, 3, CalculationRemainderAllocation.Earliest, "4,3,3")]
    [InlineData(EffectNumericParameter.StatusStacks, 1, 4, CalculationRemainderAllocation.Earliest, "1,0,0,0")]
    [InlineData(EffectNumericParameter.StatusStacks, 1, 4, CalculationRemainderAllocation.Latest, "0,0,0,1")]
    [InlineData(EffectNumericParameter.StatusStacks, 0, 3, CalculationRemainderAllocation.Earliest, "0,0,0")]
    [InlineData(EffectNumericParameter.ModifierStacks, 10, 3, CalculationRemainderAllocation.Latest, "3,3,4")]
    [InlineData(EffectNumericParameter.ModifierStacks, 1, 4, CalculationRemainderAllocation.Latest, "0,0,0,1")]
    [InlineData(EffectNumericParameter.ModifierStacks, 0, 3, CalculationRemainderAllocation.Earliest, "0,0,0")]
    public void StackBudgetsConserveCountsAndZeroSharesNeverApplyOrTriggerChildren(
        EffectNumericParameter parameter, int total, int repeat, CalculationRemainderAllocation bias, string expected)
    {
        var fixture = Fixture();
        var effect = Stacks(parameter, total, repeat, bias) with { ChainedEffects = [Damage(0, 1)] };
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, effect)));
        var roots = result.Steps.Where(step => step.ImpactShares.Any(share => share.Parameter == parameter)).ToArray();
        Assert.Equal(expected, string.Join(",", roots.Select(step => step.ImpactShares[0].Share.Quantity.Value)));
        foreach (var step in roots.Where(step => step.ImpactShares[0].Share.Quantity.Value == 0))
        { Assert.False(step.Applied); Assert.Equal("zero_contribution", step.SkipReason); Assert.Empty(step.Applications); }
        Assert.Equal(repeat + roots.Count(step => step.Applied), result.Steps.Length);
        if (parameter == EffectNumericParameter.StatusStacks)
            Assert.Equal(total, result.State.StatusEffects.GetValueOrDefault("enemy", []).Sum(status => status.Stacks));
        else Assert.Equal(total, result.Run!.Modifiers.Sum(modifier => modifier.Stacks));
    }

    [Fact]
    public void StackCountReducedToZeroByTargetStageAlsoSkipsWithoutAnInvalidApplication()
    {
        var profile = Counts() with { ResourceInfluenceBindings = [new()
        { BindingId = "reduction", Channel = "counts", Bucket = "count_target", Scope = CalculationEntityScope.Target,
            ResourceId = "focus", Scale = 0, Offset = -1 }] };
        var fixture = Fixture(counts: profile);
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, Stacks(EffectNumericParameter.StatusStacks, 3, 3))));
        Assert.All(result.Steps, step => { Assert.Equal("zero_contribution", step.SkipReason); Assert.False(step.Applied);
            Assert.Equal(0, Assert.Single(step.Parameters).Calculation.Value); });
        Assert.Empty(result.Records);
        Assert.Empty(result.State.StatusEffects.GetValueOrDefault("enemy", []));
        Assert.Equal(4, result.Calculations.Count);
    }

    [Theory]
    [InlineData(EffectChanceScope.PerEffect, 3)]
    [InlineData(EffectChanceScope.PerTarget, 3)]
    [InlineData(EffectChanceScope.PerSequence, 1)]
    public void ChanceScopesConsumeTheDocumentedNumberOfDraws(EffectChanceScope scope, int draws)
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Damage(3, 3) with { Chance = .5f, ChanceScope = scope });
        var result = Success(fixture.Executor.Execute(request));
        var context = request.Combat.Determinism;
        for (var i = 0; i < draws; i++) context = context.DrawDouble().Context;
        Assert.Equal(context.RandomState, result.State.Determinism.RandomState);
        if (scope == EffectChanceScope.PerSequence)
        { Assert.Single(result.Steps.Select(step => step.ChanceRoll).Distinct()); Assert.Single(result.Steps.Select(step => step.Applied).Distinct()); }
    }

    [Theory]
    [InlineData(EffectTargetLossPolicy.Skip, 3)]
    [InlineData(EffectTargetLossPolicy.StopRepeat, 2)]
    public void LostTargetsLeaveUnusedSharesUnallocatedRatherThanBoostingAnEarlierHit(EffectTargetLossPolicy policy, int steps)
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Damage(12, 3) with { TargetLoss = new() { Policy = policy } });
        request = request with { Combat = DefeatAtZero(request.Combat, "enemy", 2) };
        var result = Success(fixture.Executor.Execute(request));
        Assert.Equal(steps, result.Steps.Length);
        Assert.True(result.Steps[0].Applied);
        Assert.All(result.Steps.Skip(1), step => Assert.False(step.Applied));
        Assert.Equal(4, result.Steps[0].Calculation!.Value);
        Assert.Equal(12, result.Steps[0].SequenceBudgets[0].Allocation.AllocatedTotal);
        Assert.Equal(4, result.Steps[1].ImpactShares[0].Share.Quantity.Value);
    }

    [Fact]
    public void RetargetWithoutAnotherCandidateStopsRatherThanInventingATarget()
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Damage(12, 3) with
        { TargetLoss = new() { Policy = EffectTargetLossPolicy.Retarget, Retarget = EffectTarget.RANDOM_ENEMY } });
        var initial = DefeatAtZero(request.Combat, "enemy", 2);
        initial = initial with { Actors = initial.Actors.Where(pair => pair.Key != "neutral").ToDictionary() };
        var result = Success(fixture.Executor.Execute(request with { Combat = initial }));
        var hits = result.Steps.Where(step => step.Applied).ToArray();
        Assert.Equal("enemy", Assert.Single(hits).TargetEntityId);
        // With no replacement enemy, retarget stops rather than inventing a target.
        Assert.Single(hits);
        Assert.Equal("repeat_stopped", result.Steps[1].SkipReason);
    }

    [Fact]
    public void AvailableRetargetUsesTheNextFixedShareAgainstItsOwnDefense()
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Damage(12, 3) with
        { TargetLoss = new() { Policy = EffectTargetLossPolicy.Retarget, Retarget = EffectTarget.RANDOM_ENEMY } });
        var result = Success(fixture.Executor.Execute(request with { Combat = DefeatAtZero(request.Combat, "enemy", 2) }));
        var hits = result.Steps.Where(step => step.Applied).ToArray();
        Assert.Equal(new[] { "enemy", "neutral", "neutral" }, hits.Select(step => step.TargetEntityId));
        Assert.Equal(new[] { 0, 1, 2 }, hits.Select(step => step.ImpactShares[0].Share.Index));
        Assert.All(hits, step => Assert.Equal(4, step.Calculation!.Value));
        Assert.Single(result.Steps.SelectMany(step => step.SequenceBudgets));
    }

    [Fact]
    public void LaterFailureRollsBackResourcesDefenseModifiersAndRandomness()
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Stacks(EffectNumericParameter.ModifierStacks, 3, 3),
            Damage(10, 3) with { Chance = .99f }, Damage(1, 1) with { TargetResource = "missing" }) with
            { Combat = Shield(GameplayOwnershipTests.State(), 5) };
        var before = CanonicalJson.ComputeHash(request);
        Assert.True(fixture.Executor.Execute(request).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
        Assert.Empty(request.Run!.Modifiers);
    }

    [Fact]
    public void CondensationEmitsDistributedImpactsButStillConsumesOnlyOncePerAction()
    {
        var numeric = Stacks(EffectNumericParameter.StatusStacks, 0, 3).Parameters[0] with
            { Parameter = EffectNumericParameter.Amount, FlatValue = null, InputQuantityId = "condensation.count" };
        var recipe = CondensationTests.Recipe(new EffectDefinition
        { Type = EffectType.MODIFY_RESOURCE, TargetResource = "focus", Repeat = 3, Parameters = [numeric] });
        var fixture = Fixture(recipe: recipe);
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, Stacks(EffectNumericParameter.StatusStacks, 6, 1))));
        var result = Success(fixture.Executor.Execute(Request(prepared.Run!, CondensationTests.Condense(), CondensationTests.Condense())
            with { Combat = prepared.State }));
        Assert.Single(result.Records, record => record.Condensation != null);
        Assert.Single(result.Steps.Where(step => step.SkipReason == "already_attempted"));
        var hits = result.Steps.Where(step => !step.ImpactShares.IsEmpty).ToArray();
        Assert.Equal(3, hits.Length);
        Assert.Single(hits.Select(step => step.Identity!.ProcId).Distinct());
        Assert.Equal(3, hits.Select(step => step.Identity!.ImpactId).Distinct().Count());
        Assert.Equal(16, result.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Empty(result.State.StatusEffects.GetValueOrDefault("enemy", []));
    }

    [Fact]
    public void TenExecutionsAndSerializedDefinitionsProduceIdenticalStateStepsAndBudgetTraces()
    {
        var fixture = Fixture();
        var effects = new[] { Damage(10, 3) with { Chance = .65f, ChanceScope = EffectChanceScope.PerSequence },
            Stacks(EffectNumericParameter.StatusStacks, 1, 4, CalculationRemainderAllocation.Latest) };
        var restored = JsonSerializer.Deserialize<EffectDefinition[]>(JsonSerializer.Serialize(effects))!;
        Assert.Equal(CanonicalJson.ComputeHash(effects), CanonicalJson.ComputeHash(restored));
        var request = Request(fixture.Run, restored);
        var results = Enumerable.Range(0, 10).Select(_ => Success(fixture.Executor.Execute(request))).ToArray();
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result)).Distinct());
        var steps = JsonSerializer.Deserialize<EffectExecutionStep[]>(JsonSerializer.Serialize(results[0].Steps))!;
        Assert.Equal(CanonicalJson.ComputeHash(results[0].Steps), CanonicalJson.ComputeHash(steps));
    }

    [Fact]
    public void CondensationBeforeConsumptionFreezesBudgetButDistributedDefenseIsLivePerImpact()
    {
        var counts = Counts() with { Buckets = [new() { BucketId = "count_base", StageId = "count_source" }, new()
        { BucketId = "count_target", StageId = "count_impact", Order = 1, Operation = CalculationBucketOperation.ConsumeCapacity }],
            ResourceInfluenceBindings = [new() { BindingId = "shield", Channel = "counts", Bucket = "count_target",
                Scope = CalculationEntityScope.Target, ResourceId = "shield", MissingResource = MissingResourcePolicy.Ignore, Settlement = new() }] };
        var numeric = Stacks(EffectNumericParameter.StatusStacks, 0, 3).Parameters[0] with
            { Parameter = EffectNumericParameter.Amount, FlatValue = null, InputQuantityId = "condensation.count" };
        var recipe = CondensationTests.Recipe(new EffectDefinition
        { Type = EffectType.DAMAGE, TargetResource = "focus", Repeat = 3, Parameters = [numeric] }) with
            { EvaluationTiming = CondensationEvaluationTiming.BeforeConsumption };
        var fixture = Fixture(counts, recipe);
        var prepared = Success(fixture.Executor.Execute(Request(fixture.Run, Stacks(EffectNumericParameter.StatusStacks, 6, 1))));
        var result = Success(fixture.Executor.Execute(Request(prepared.Run!, CondensationTests.Condense()) with
            { Combat = Shield(prepared.State, 5) }));
        var hits = result.Steps.Where(step => !step.ImpactShares.IsEmpty).ToArray();
        Assert.Equal(new[] { 0f, 0f, 1f }, hits.Select(step => step.Calculation!.Value));
        Assert.Equal(9, result.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(0, result.State.GetActor("enemy")!.GetResource("shield")!.Current);
    }

    [Fact]
    public void MultipleExplicitTargetsAndUnconfiguredExecutorsRejectDistribution()
    {
        var fixture = Fixture();
        Assert.True(fixture.Executor.Execute(Request(fixture.Run, Damage(3, 3)) with { SelectedTargetEntityIds = ["enemy", "neutral"] }).IsFailure);
        Assert.True(EffectTransactionTests.Executor().Execute(EffectTransactionTests.Request(Damage(3, 3))).IsFailure);
    }

    [Fact]
    public void ContinuousResourceBudgetKeepsExactAllocationWithoutForcingIntegerDamage()
    {
        var fixture = Fixture();
        var effect = Damage(10, 3);
        effect = effect with { Parameters = [effect.Parameters[0] with { Distribution = effect.Parameters[0].Distribution! with
            { Allocation = new() { Mode = CalculationDistributionMode.Continuous } } }] };
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, effect)));
        var allocation = Assert.Single(result.Steps[0].SequenceBudgets).Allocation;
        Assert.Equal(10d, allocation.Shares.Sum(share => (double)share.Quantity.Value));
        Assert.All(result.Steps, step => Assert.Equal(step.ImpactShares[0].Share.Quantity.Value, step.Calculation!.Value));
        Assert.Equal(0, allocation.ConservationRemainder);
    }

    [Fact]
    public void DirectNumericResolutionAndPayloadSchemasCannotSilentlyIgnoreSequenceScheduling()
    {
        var parameter = Damage(3, 3).Parameters[0];
        Assert.True(new CalculationResolver(Mock.Of<IRuntimeFormulaEvaluator>(), allowUnconfiguredCalculations: true)
            .ResolveParameter(Damage(3, 3), parameter, "unplanned", new()).IsFailure);
        Assert.True(StackPayloadPolicies.ValidateDefinitions([new()
            { ParameterId = "potency", Numeric = parameter }], StackPayloadReapplyPolicy.PreserveLots).IsFailure);
    }

    [Fact]
    public void TransportedTargetInfluencedBudgetIsRejectedInsteadOfLosingItsReceipts()
    {
        var fixture = Fixture();
        var quantity = new CalculationEngine().Calculate(new()
        { CalculationId = "already-targeted", ContentRevision = "revision", Channel = "magnitude", UnitId = "points",
            BaseValue = 9, StageIds = ["defense"], StageContextIds = ImmutableSortedDictionary<string, string>.Empty.Add("defense", "enemy") },
            Profiles()["scoped"]).Value.Quantity;
        var effect = Damage(0, 3);
        effect = effect with { Parameters = [effect.Parameters[0] with { FlatValue = null, InputQuantityId = "transported" }] };
        var request = Request(fixture.Run, effect) with { Quantities = ImmutableSortedDictionary<string, CalculationQuantity>.Empty.Add("transported", quantity) };
        var before = CanonicalJson.ComputeHash(request);
        Assert.True(fixture.Executor.Execute(request).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Theory]
    [InlineData("overlap")]
    [InlineData("target-source")]
    [InlineData("missing-stage")]
    [InlineData("continuous-stacks")]
    [InlineData("multiple-targets")]
    [InlineData("target-formula")]
    [InlineData("duration")]
    [InlineData("target-receipt")]
    public void InvalidPoliciesFailAtPublicationIncludingDormantChildren(string mutation)
    {
        var effect = Stacks(EffectNumericParameter.StatusStacks, 3, 3);
        var parameter = effect.Parameters[0];
        var distribution = parameter.Distribution!;
        effect = mutation switch
        {
            "overlap" => effect with { Parameters = [parameter with { StageIds = ["count_source"] }] },
            "target-source" => effect with { Parameters = [parameter with
                { StageIds = ["count_source"], Distribution = distribution with { SourceStageIds = ["count_impact"] } }] },
            "missing-stage" => effect with { Parameters = [parameter with { Distribution = distribution with { SourceStageIds = ["missing"] } }] },
            "continuous-stacks" => effect with { Parameters = [parameter with { Distribution = distribution with { Allocation = new() } }] },
            "multiple-targets" => effect with { Target = EffectTarget.ALL_ENEMIES },
            "target-formula" => effect with { Parameters = [parameter with { FlatValue = null, FormulaValue = "target.resources.focus.current" }] },
            "duration" => effect with { Parameters = [parameter with { Parameter = EffectNumericParameter.StatusDuration }] },
            _ => effect with { Parameters = [parameter with { Distribution = distribution with
                { Allocation = distribution.Allocation with { AllowTargetScopedInput = true } } }] }
        };
        var fixture = Fixture();
        var dormant = Damage(1, 1) with { Chance = 0, ChainedEffects = [effect] };
        Assert.True(fixture.Executor.Execute(Request(fixture.Run, dormant)).IsFailure);
        var graph = new ContentGraphValidator().Validate(Bundle(Profiles(), dormant));
        Assert.False(graph.IsValid);
        Assert.Contains(graph.Errors, error => error.Contains("sequence", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("source budget", StringComparison.OrdinalIgnoreCase) || error.Contains("distributed stacks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidDistributionPublishesAndNonIntegralQuantizedBudgetFailsWithoutMutation()
    {
        var graph = new ContentGraphValidator().Validate(Bundle(Profiles(), Damage(10, 3)));
        Assert.True(graph.IsValid, string.Join("; ", graph.Errors));
        var fixture = Fixture();
        var request = Request(fixture.Run, Damage(10.5f, 3));
        var hash = CanonicalJson.ComputeHash(request);
        Assert.True(fixture.Executor.Execute(request).IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(request));
    }

    internal static EffectDefinition Damage(float value, int repeat) => new()
    {
        Type = EffectType.DAMAGE, TargetResource = "focus", Repeat = repeat,
        Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = value, Channel = "magnitude",
            PipelineId = "scoped", UnitId = "points", StageIds = ["defense"], Distribution = new()
            { SourceStageIds = ["origin"], Allocation = new() { Mode = CalculationDistributionMode.Quantized, Quantum = 1 } } }]
    };

    internal static EffectDefinition Stacks(EffectNumericParameter parameter, int value, int repeat,
        CalculationRemainderAllocation bias = CalculationRemainderAllocation.Earliest) => new()
    {
        Type = parameter == EffectNumericParameter.StatusStacks ? EffectType.APPLY_STATUS : EffectType.APPLY_MODIFIER,
        StatusId = "charges", ModifierId = "charges", Repeat = repeat,
        Parameters = [new() { Parameter = parameter, FlatValue = value, Channel = "counts", PipelineId = "counts",
            UnitId = "stacks", StageIds = ["count_impact"], Conversion = new() { RequireInteger = true, Minimum = 0 },
            Distribution = new() { SourceStageIds = ["count_source"], Allocation = new()
                { Mode = CalculationDistributionMode.Quantized, Quantum = 1, RemainderAllocation = bias } } }]
    };

    private static CalculationPipelineDefinition Counts() => new()
    { PipelineId = "counts", Channel = "counts", UnitId = "stacks", Stages = [new() { StageId = "count_source" },
        new() { StageId = "count_impact", Scope = CalculationStageScope.Target }], Buckets = [new()
        { BucketId = "count_base", StageId = "count_source" }, new() { BucketId = "count_target", StageId = "count_impact", Order = 1 }] };

    internal static Dictionary<string, CalculationPipelineDefinition> Profiles(CalculationPipelineDefinition? counts = null) => new()
    { ["scoped"] = CalculationProfileTests.Pipeline() with { ResourceInfluenceBindings = [new()
        { BindingId = "capacity", Channel = "magnitude", Bucket = "capacity", Scope = CalculationEntityScope.Target,
            ResourceId = "shield", MissingResource = MissingResourcePolicy.Ignore, Settlement = new() }] }, ["counts"] = counts ?? Counts() };

    internal static ContentBundle Bundle(Dictionary<string, CalculationPipelineDefinition> profiles, EffectDefinition? action = null,
        CondensationRecipeDefinition? recipe = null)
    {
        var artifacts = new Dictionary<string, JsonElement>
        {
            ["calculation-pipelines/catalog.json"] = JsonSerializer.SerializeToElement(profiles),
            ["status-effects/catalog.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, StatusEffectDefinition>
                { ["charges"] = new() { StatusId = "charges", MaxStacks = 99, Consumption = new() { AllowedRecipeIds = recipe == null ? [] : ["test"] } } }),
            ["modifiers/catalog.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, ScriptModifierDefinition>
                { ["charges"] = new() { ModifierId = "charges", MaxStacks = 99 } }),
            ["resources/catalog.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, ResourceDefinition>
                { ["focus"] = new() { ResourceId = "focus", DisplayName = "Focus" }, ["shield"] = new() { ResourceId = "shield", DisplayName = "Shield" } })
        };
        if (action != null) artifacts["actions/catalog.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, ActionDefinition>
            { ["test"] = new() { ActionId = "test", Effects = [action] } });
        if (recipe != null) artifacts["condensation-recipes/catalog.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, CondensationRecipeDefinition>
            { [recipe.RecipeId] = recipe });
        return new() { Manifest = new() { Revision = "revision", ConfigName = "default", Artifacts = artifacts.Select(item => new ContentArtifactManifest
            { Path = item.Key, Kind = item.Key.Split('/')[0], DefinitionCount = item.Value.EnumerateObject().Count() }).ToArray() }, Artifacts = artifacts.ToImmutableDictionary() };
    }

    internal static (EffectTriggerExecutor Executor, RunState Run) Fixture(CalculationPipelineDefinition? counts = null,
        CondensationRecipeDefinition? recipe = null, CalculationPipelineDefinition? extraProfile = null,
        IRuntimeFormulaEvaluator? evaluator = null)
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(item => item.Evaluate(It.IsAny<string>(), It.IsAny<Dictionary<string, float>>(), It.IsAny<float>()))
            .Returns((string expression, Dictionary<string, float> variables, float initial) => variables.TryGetValue(expression, out var value)
                ? Result<float>.Success(value) : Result<float>.Failure("Unknown variable"));
        var profiles = Profiles(counts);
        if (extraProfile != null) profiles.Add(extraProfile.PipelineId, extraProfile);
        var runtime = ContentRuntime.Create(Bundle(profiles, recipe: recipe));
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(item => item.Resolve("revision", It.IsAny<string?>())).Returns(Result<ContentRuntime>.Success(runtime.Value));
        var actualFormulas = evaluator ?? formulas.Object;
        var influences = new CompositeCalculationInfluenceProvider([new CardComponentInfluenceProvider(actualFormulas), new EntityResourceInfluenceProvider()]);
        return (new(actualFormulas, new ImmutableEffectProcessor(), runtimes.Object, new CalculationEngine(actualFormulas), influences),
            new() { PlayerEntityId = "hero", Determinism = DeterministicContext.Create(123, "revision"),
                ResolvedMode = new() { Definition = new() { CalculationPipelineIds = profiles.Keys.ToArray() } } });
    }

    private static CombatState Shield(CombatState state, float amount)
    {
        var actor = state.GetActor("enemy")!;
        return state.ReplaceActor(actor with { ResourceState = actor.ResourceState with { Resources = actor.ResourceState.Resources
            .ToDictionary().Append(new KeyValuePair<string, ResourcePool>("shield", ResourcePool.Materialize(new ResourceDefinition
            { ResourceId = "shield", DisplayName = "Shield", DefaultMax = amount }, amount, amount))).ToDictionary() } });
    }

    internal static CombatState DefeatAtZero(CombatState state, string id, float value)
    {
        var actor = state.GetActor(id)!;
        var definition = actor.GetResource("focus")!.Definition! with { ThresholdPolicies = [new()
            { PolicyId = "defeat", Comparison = ResourceThresholdComparison.LessThanOrEqual, ThresholdSource = ResourceThresholdSource.Minimum,
                Consequence = ResourceThresholdConsequence.DefeatOwner }] };
        return state.ReplaceActor(actor with { ResourceState = actor.ResourceState with { Resources = new Dictionary<string, ResourcePool>
            { ["focus"] = ResourcePool.Materialize(definition, value, 20) } } });
    }

    private static EffectTriggerExecutionRequest Request(RunState run, params EffectDefinition[] effects)
        => EffectTransactionTests.Request(effects) with { Run = run };
    private static EffectBatchResult Success(Result<EffectBatchResult> result)
    { Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null); return result.Value; }
}
