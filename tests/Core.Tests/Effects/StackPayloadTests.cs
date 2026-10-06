using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Run;
using Core.Run.Content;
using Core.Resources;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class StackPayloadTests
{
    [Theory]
    [InlineData(StackParameterEvaluation.Snapshot, 26)]
    [InlineData(StackParameterEvaluation.Dynamic, 10)]
    public void EvaluationPolicyControlsLaterSourceChanges(StackParameterEvaluation evaluation, float expected)
    {
        var fixture = Fixture(evaluation);
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(2))));
        var modified = Focus(applied.State, "hero", 2);
        var activated = Success(fixture.Executor.Execute(Request(fixture.Run, Activate()) with
        { Combat = modified, StackPayloadLots = Lots(applied.State) }));
        Assert.Equal(expected, activated.Calculations[0].Value);
        Assert.Single(activated.Records);
        Assert.All(activated.Steps[0].PayloadCalculations, trace => Assert.True(trace.CaptureOnly));
        Assert.Equal(10, applied.State.GetActor("hero")!.GetResource("focus")!.Current);
    }

    [Fact]
    public void ReapplicationPreservesDifferentIntensitiesWithoutMultiplyingDuration()
    {
        var fixture = Fixture();
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(2), Apply(1) with
        { StatusDuration = 9, PayloadBindings = [new() { ParameterId = "potency", FlatValue = 7 }] })));
        var status = Assert.Single(applied.State.StatusEffects["enemy"]);
        Assert.Equal(3, status.Stacks);
        Assert.Equal(-1, status.Duration); // Preserve duration policy, not a numeric multiplier.
        Assert.Equal(new[] { 2, 1 }, status.PayloadLots.Select(lot => lot.Stacks));
        Assert.Equal(new[] { 13f, 17f }, status.PayloadLots.Select(lot => lot.Parameters["potency"].Snapshot!.Value));
        var activation = Success(fixture.Executor.Execute(Request(fixture.Run, Activate()) with
        { Combat = applied.State, StackPayloadLots = status.PayloadLots }));
        Assert.Equal(43, activation.Calculations[0].Value);
        Assert.Single(activation.Records);
    }

    [Theory]
    [InlineData(StackReapplyPolicy.Add, StackPayloadReapplyPolicy.PreserveLots, 5, 2, 5)]
    [InlineData(StackReapplyPolicy.Highest, StackPayloadReapplyPolicy.PreserveLots, 4, 1, 4)]
    [InlineData(StackReapplyPolicy.Replace, StackPayloadReapplyPolicy.PreserveLots, 2, 1, 2)]
    [InlineData(StackReapplyPolicy.Add, StackPayloadReapplyPolicy.ReplaceAll, 5, 1, 5)]
    public void StackCapsAndReplacementKeepLotCountConsistent(StackReapplyPolicy stacking, StackPayloadReapplyPolicy payloadPolicy,
        int expectedStacks, int expectedLots, int expectedLotStacks)
    {
        var fixture = Fixture(stacking: stacking, payloadPolicy: payloadPolicy, maximum: 5);
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(4), Apply(2))));
        var status = Assert.Single(applied.State.StatusEffects["enemy"]);
        Assert.Equal(expectedStacks, status.Stacks);
        Assert.Equal(expectedLots, status.PayloadLots.Length);
        Assert.Equal(expectedLotStacks, status.PayloadLots.Sum(lot => lot.Stacks));
        if (stacking == StackReapplyPolicy.Add && payloadPolicy == StackPayloadReapplyPolicy.PreserveLots)
            Assert.Equal(new[] { 4, 1 }, status.PayloadLots.Select(lot => lot.Stacks));
    }

    [Theory]
    [InlineData(StackParameterEvaluation.Snapshot)]
    [InlineData(StackParameterEvaluation.Dynamic)]
    public void BindingUsesEffectiveUpgradedCardBaseNotTheMutableCardInLaterRequests(StackParameterEvaluation evaluation)
    {
        var fixture = Fixture(evaluation);
        var card = new EffectiveCardDefinition
        {
            CardInstanceId = Guid.Parse("22222222-2222-2222-2222-222222222222"), DefinitionId = "volatile",
            Fingerprint = "effective-upgraded-base", Components = [new CardEffectComponentDefinition
            { ComponentId = "primary", Effect = new() { Type = EffectType.DAMAGE, TargetResource = "focus", FlatValue = 8 } }]
        };
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(1) with
        { PayloadBindings = [new() { ParameterId = "potency", CardEffectComponentId = "primary" }] }) with { Card = card }));
        var lot = Assert.Single(Lots(applied.State));
        Assert.Equal(8, lot.Parameters["potency"].Definition.Numeric.FlatValue);
        Assert.Equal(card.Fingerprint, lot.CardFingerprint);
        var activated = Success(fixture.Executor.Execute(Request(fixture.Run, Activate()) with
        { Combat = applied.State, StackPayloadLots = [lot], Card = card with { Components = [], Fingerprint = "changed" } }));
        Assert.Equal(18, activated.Calculations[0].Value);
    }

    [Theory]
    [InlineData(StackParameterEvaluation.Snapshot, StackMissingSourcePolicy.Fail, true, 13)]
    [InlineData(StackParameterEvaluation.Dynamic, StackMissingSourcePolicy.Fail, false, 0)]
    [InlineData(StackParameterEvaluation.Dynamic, StackMissingSourcePolicy.SkipContribution, false, 0)]
    [InlineData(StackParameterEvaluation.Dynamic, StackMissingSourcePolicy.UseOwner, true, 7)]
    public void MissingSourceBehaviorIsExplicit(StackParameterEvaluation evaluation, StackMissingSourcePolicy policy, bool passes, float expected)
    {
        var fixture = Fixture(evaluation, policy);
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(1))));
        var noSource = applied.State with
        { Actors = applied.State.Actors.Where(pair => pair.Key != "hero").ToDictionary() };
        noSource = Focus(noSource, "enemy", 4);
        var result = fixture.Executor.Execute(Request(fixture.Run, Activate()) with
        { Combat = noSource, OwnerEntityId = "ally", StackPayloadLots = Lots(applied.State) });
        Assert.Equal(passes, result.IsSuccess);
        if (passes) Assert.Equal(expected, result.Value.Calculations[0].Value);
    }

    [Fact]
    public void SkipContributionSkipsOnlyMissingSourceLots()
    {
        var fixture = Fixture(StackParameterEvaluation.Dynamic, StackMissingSourcePolicy.SkipContribution);
        var first = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(2))));
        var second = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(1)) with
        { Combat = first.State, SourceEntityId = "ally" }));
        var missing = second.State with { Actors = second.State.Actors.Where(pair => pair.Key != "hero").ToDictionary() };
        var result = Success(fixture.Executor.Execute(Request(fixture.Run, Activate()) with
        { Combat = missing, OwnerEntityId = "ally", StackPayloadLots = Lots(second.State) }));
        Assert.Equal(13, result.Calculations[0].Value);
    }

    [Theory]
    [InlineData(StackParameterEvaluation.Snapshot, true)]
    [InlineData(StackParameterEvaluation.Dynamic, false)]
    public void DefeatedButPresentSourceFollowsEvaluationPolicy(StackParameterEvaluation evaluation, bool expectedSuccess)
    {
        var fixture = Fixture(evaluation);
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(1))));
        var hero = applied.State.GetActor("hero")!;
        var definition = hero.GetResource("focus")!.Definition! with { ThresholdPolicies = [new()
        { PolicyId = "defeat", Comparison = ResourceThresholdComparison.LessThanOrEqual,
            ThresholdSource = ResourceThresholdSource.Minimum, Consequence = ResourceThresholdConsequence.DefeatOwner }] };
        var defeated = applied.State.ReplaceActor(hero with { ResourceState = hero.ResourceState with
        { Resources = new Dictionary<string, ResourcePool> { ["focus"] = ResourcePool.Materialize(definition, 0, 20) } } });
        Assert.False(defeated.GetActor("hero")!.IsAlive);
        var result = fixture.Executor.Execute(Request(fixture.Run, Activate()) with
        { Combat = defeated, OwnerEntityId = "ally", StackPayloadLots = Lots(applied.State) });
        Assert.Equal(expectedSuccess, result.IsSuccess);
    }

    [Fact]
    public void LotLimitFailsWithoutTruncatingOrReplacingOldContributions()
    {
        var fixture = Fixture(maximum: 1000);
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(1) with { Repeat = 256 })));
        Assert.Equal(256, Lots(applied.State).Length);
        var hash = CanonicalJson.ComputeHash(applied.State);
        var result = fixture.Executor.Execute(Request(fixture.Run, Apply(1)) with { Combat = applied.State });
        Assert.True(result.IsFailure);
        Assert.Contains("lot limit", result.Error);
        Assert.Equal(hash, CanonicalJson.ComputeHash(applied.State));
    }

    [Fact]
    public void CardCompilerRejectsUnresolvableComponentBindingIncludingChildren()
    {
        var card = new CardContentDefinition { CardId = "payload-card", Components = [new CardEffectComponentDefinition
        { ComponentId = "apply", Effect = Apply(1) with { ChainedEffects = [Apply(1) with
        { PayloadBindings = [new() { ParameterId = "potency", CardEffectComponentId = "missing" }] }] } }] };
        var result = new CardContentCompiler().Compile(card);
        Assert.True(result.IsFailure);
        Assert.Contains("effective card component", result.Error);
    }

    [Fact]
    public void ModifierApplicationAndPartialRemovalUseSameCanonicalLots()
    {
        var fixture = Fixture();
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, ApplyModifier(2), ApplyModifier(3))));
        var modifier = Assert.Single(applied.Run!.Modifiers);
        Assert.Equal(5, modifier.PayloadLots.Sum(lot => lot.Stacks));
        var removed = Success(fixture.Executor.Execute(Request(applied.Run, new EffectDefinition
        { Type = EffectType.REMOVE_MODIFIER, ModifierId = "charges", ModifierStacks = 3 }) with { Combat = applied.State }));
        var remaining = Assert.Single(removed.Run!.Modifiers);
        var retainedLot = Assert.Single(remaining.PayloadLots);
        Assert.Equal(2, remaining.Stacks);
        Assert.Equal(modifier.PayloadLots[1].LotId, retainedLot.LotId);
        Assert.Equal(2, retainedLot.Stacks);
        var capture = AccumulatedStackTransitions.Capture(removed.State, removed.Run, "test", new());
        Assert.True(capture.IsSuccess);
        Assert.Equal(remaining.PayloadLots, Assert.Single(capture.Value.Sources).PayloadLots);
    }

    [Fact]
    public void LifecycleActivatesPayloadOnceAndExpiresAllItsLots()
    {
        var fixture = Fixture(withTrigger: true);
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(2) with { StatusDuration = 1 })));
        var result = new CombatStatusLifecycle(fixture.Executor).Process(fixture.Run, applied.State, StatusTriggerBoundary.EndActivation, "enemy");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var triggered = Assert.Single(result.Value.Events, item => item.Kind == CombatStatusLifecycleEventKind.Triggered);
        Assert.Single(triggered.Applications);
        Assert.Single(triggered.Steps);
        Assert.Equal(26, triggered.Steps[0].Calculation!.Value);
        Assert.Empty(result.Value.Combat.StatusEffects.GetValueOrDefault("enemy", []));
    }

    [Fact]
    public void PayloadCannotReapplySourceStagesOrCrossUnitAndRevisionBoundaries()
    {
        var fixture = Fixture();
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(1))));
        var request = Request(fixture.Run, Activate()) with { Combat = applied.State, StackPayloadLots = Lots(applied.State) };
        var parameter = Activate().Parameters[0];
        Assert.True(fixture.Executor.Execute(request with { Trigger = request.Trigger with
        { Effects = [Activate() with { Parameters = [parameter with { StageIds = ["source"] }] }] } }).IsFailure);
        Assert.True(fixture.Executor.Execute(request with { Trigger = request.Trigger with
        { Effects = [Activate() with { Parameters = [parameter with { UnitId = "cards" }] }] } }).IsFailure);
        Assert.True(fixture.Executor.Execute(request with
        { StackPayloadLots = [request.StackPayloadLots[0] with { ContentRevision = "other" }] }).IsFailure);
    }

    [Fact]
    public void InvalidBindingAndLaterFailureLeaveInputUnchanged()
    {
        var fixture = Fixture();
        var request = Request(fixture.Run, Apply(1), EffectTransactionTests.Resource(EffectType.DAMAGE, null) with { FormulaValue = "missing" });
        var hash = CanonicalJson.ComputeHash(request);
        Assert.True(fixture.Executor.Execute(request).IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(request));
        Assert.True(fixture.Executor.Execute(Request(fixture.Run, Apply(1) with
        { PayloadBindings = [new() { ParameterId = "unknown", FlatValue = 1 }] })).IsFailure);
        Assert.NotEmpty(EffectDefinitionValidator.Validate([Apply(1) with
        { PayloadBindings = [new() { ParameterId = "potency", CardEffectComponentId = "x", FlatValue = 1 }] }]));
        Assert.False(StackPayloadPolicies.SafeId(null));
    }

    [Fact]
    public void TenEquivalentExecutionsAndSerializedLotActivationHaveIdenticalResults()
    {
        var fixture = Fixture(StackParameterEvaluation.Dynamic);
        var request = Request(fixture.Run, Apply(2), Apply(1) with { PayloadBindings = [new() { ParameterId = "potency", FlatValue = 7 }] });
        var results = Enumerable.Range(0, 10).Select(_ => Success(fixture.Executor.Execute(request))).ToArray();
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result)).Distinct());
        var lots = Lots(results[0].State);
        var restored = JsonSerializer.Deserialize<ImmutableArray<StackPayloadLot>>(JsonSerializer.Serialize(lots));
        Assert.Equal(CanonicalJson.ComputeHash(lots), CanonicalJson.ComputeHash(restored));
        var activation = Request(fixture.Run, Activate()) with { Combat = results[0].State, StackPayloadLots = lots };
        var before = Success(fixture.Executor.Execute(activation));
        var after = Success(fixture.Executor.Execute(activation with { StackPayloadLots = restored }));
        Assert.Equal(CanonicalJson.ComputeHash(before), CanonicalJson.ComputeHash(after));
    }

    [Fact]
    public void AggregationRejectsIncompatibleUnitsDefinitionsAndOverflow()
    {
        var fixture = Fixture();
        var applied = Success(fixture.Executor.Execute(Request(fixture.Run, Apply(1))));
        var quantity = Lots(applied.State)[0].Parameters["potency"].Snapshot!;
        var engine = new CalculationEngine();
        Assert.True(CalculationQuantityAggregation.Sum(engine, "sum", [new("a", quantity, 2), new("b", quantity, 1)]).IsSuccess);
        Assert.True(CalculationQuantityAggregation.Sum(engine, "sum", [new("a", quantity, 1), new("b", quantity with { UnitId = "other" }, 1)]).IsFailure);
        Assert.True(CalculationQuantityAggregation.Sum(engine, "sum", [new("a", quantity, 1), new("b", quantity with
        { IncorporatedStages = [quantity.IncorporatedStages[0] with { PipelineFingerprint = "different" }] }, 1)]).IsFailure);
        Assert.True(CalculationQuantityAggregation.Sum(engine, "sum", [new("a", quantity with { Value = float.MaxValue }, 2)]).IsFailure);
        Assert.True(CalculationQuantityAggregation.Sum(engine, "sum", []).IsFailure);
    }

    private static EffectBatchResult Success(Result<EffectBatchResult> result)
    { Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null); return result.Value; }
    private static EffectTriggerExecutionRequest Request(RunState run, params EffectDefinition[] effects) =>
        EffectTransactionTests.Request(effects) with { Run = run };
    private static ImmutableArray<StackPayloadLot> Lots(CombatState state) => state.StatusEffects["enemy"][0].PayloadLots;
    private static CombatState Focus(CombatState state, string id, float value)
    {
        var actor = state.GetActor(id)!;
        return state.ReplaceActor(actor with { ResourceState = actor.ResourceState with
        { Resources = actor.ResourceState.Resources.ToDictionary(pair => pair.Key, pair => pair.Value with { Current = value }) } });
    }
    internal static EffectDefinition Apply(int stacks) => new() { Type = EffectType.APPLY_STATUS, StatusId = "charges", StatusStacks = stacks };
    internal static EffectDefinition ApplyModifier(int stacks) => new() { Type = EffectType.APPLY_MODIFIER, ModifierId = "charges", ModifierStacks = stacks };
    internal static EffectDefinition Activate() => new()
    {
        Type = EffectType.MODIFY_RESOURCE, TargetResource = "focus", Parameters = [new()
        { Parameter = EffectNumericParameter.Amount, InputQuantityId = "payload.potency", PipelineId = "payload",
            Channel = "magnitude", UnitId = "scalar", StageIds = ["target"] }]
    };

    internal static (EffectTriggerExecutor Executor, RunState Run) Fixture(StackParameterEvaluation evaluation = StackParameterEvaluation.Snapshot,
        StackMissingSourcePolicy missing = StackMissingSourcePolicy.Fail, StackReapplyPolicy stacking = StackReapplyPolicy.Add,
        StackPayloadReapplyPolicy payloadPolicy = StackPayloadReapplyPolicy.PreserveLots, int maximum = 99, bool withTrigger = false)
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(item => item.Evaluate(It.IsAny<string>(), It.IsAny<Dictionary<string, float>>(), It.IsAny<float>()))
            .Returns((string expression, Dictionary<string, float> variables, float initial) =>
                variables.TryGetValue(expression, out var value) ? Result<float>.Success(value) : Result<float>.Failure("Unknown variable"));
        var pipeline = new CalculationPipelineDefinition
        {
            PipelineId = "payload", Channel = "magnitude", Stages = [new() { StageId = "source", Scope = CalculationStageScope.Actor },
                new() { StageId = "target", Scope = CalculationStageScope.Target }], Buckets = [new() { BucketId = "boost", StageId = "source" },
                new() { BucketId = "defense", StageId = "target", Order = 1 }]
        };
        var parameters = ImmutableArray.Create(new StackPayloadParameterDefinition
        {
            ParameterId = "potency", Evaluation = evaluation, MissingSource = missing, Numeric = new()
            { Parameter = EffectNumericParameter.Amount, FlatValue = 3, Channel = "magnitude", PipelineId = "payload", UnitId = "scalar", StageIds = ["source"] }
        });
        var artifacts = new Dictionary<string, JsonElement>
        {
            ["calculation-pipelines/test.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, CalculationPipelineDefinition> { ["payload"] = pipeline }),
            ["status-effects/test.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, StatusEffectDefinition> { ["charges"] = new()
            { StatusId = "charges", Stacking = stacking, MaxStacks = maximum, PayloadParameters = parameters, PayloadReapply = payloadPolicy,
                DurationTickBoundary = StatusTriggerBoundary.EndActivation, Consumption = new() { AllowedRecipeIds = ["test"] },
                Triggers = withTrigger ? [new() { TriggerId = "pulse", Boundary = "EndActivation", Effects = [Activate() with { Target = EffectTarget.SELF }] }] : [] } }),
            ["modifiers/test.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, ScriptModifierDefinition> { ["charges"] = new()
            { ModifierId = "charges", PayloadParameters = parameters, Consumption = new() { AllowedRecipeIds = ["test"] } } })
        };
        var runtime = ContentRuntime.Create(new()
        {
            Manifest = new() { Revision = "revision", ConfigName = "default", Artifacts = artifacts.Select(item => new ContentArtifactManifest
                { Kind = item.Key.Split('/')[0], Path = item.Key, DefinitionCount = 1 }).ToArray() }, Artifacts = artifacts.ToImmutableDictionary()
        });
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(item => item.Resolve("revision", It.IsAny<string?>())).Returns(Result<ContentRuntime>.Success(runtime.Value));
        var influences = new Mock<ICalculationInfluenceProvider>();
        influences.Setup(item => item.Collect(It.IsAny<CalculationSourceContext>())).Returns((CalculationSourceContext context) =>
            Result<IReadOnlyList<CalculationInfluence>>.Success([new()
            { InfluenceId = "source", SourceId = context.Actor!.InstanceId, Channel = "magnitude", Bucket = "boost", Value = context.Actor.GetResource("focus")!.Current }]));
        var run = new RunState
        { PlayerEntityId = "hero", Determinism = DeterministicContext.Create(1, "revision"),
            ResolvedMode = new() { Definition = new() { CalculationPipelineIds = ["payload"] } } };
        return (new(formulas.Object, new ImmutableEffectProcessor(), runtimes.Object, new CalculationEngine(formulas.Object), influences.Object), run);
    }
}
