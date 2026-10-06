using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.CardZones;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Run;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class EffectNumericParameterTests
{
    [Theory]
    [InlineData(EffectProvenanceKind.Card)]
    [InlineData(EffectProvenanceKind.Ability)]
    [InlineData(EffectProvenanceKind.Status)]
    public void CalculatedStacksAndDurationUseSameExecutorForEveryOrigin(EffectProvenanceKind origin)
    {
        var (executor, run) = Fixture();
        var effect = new EffectDefinition
        {
            Type = EffectType.APPLY_STATUS, StatusId = "charges", Target = EffectTarget.SELF,
            Parameters = [Parameter(EffectNumericParameter.StatusStacks, "stacks", "stack_input"),
                Parameter(EffectNumericParameter.StatusDuration, "turns", "duration_input")]
        };
        var request = EffectTransactionTests.Request(effect) with
        {
            Run = run, Provenance = new() { Kind = origin, SourceId = "origin" },
            Variables = new Dictionary<string, float> { ["stack_input"] = 3.7f, ["duration_input"] = 2.9f }
        };
        var before = CanonicalJson.ComputeHash(request);
        var result = executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var status = Assert.Single(result.Value.State.StatusEffects["hero"]);
        Assert.Equal(3, status.Stacks);
        Assert.Equal(2, status.Duration);
        Assert.Equal(2, result.Value.Calculations.Count);
        Assert.All(result.Value.Calculations, calculation => Assert.True(calculation.CaptureOnly));
        Assert.Equal(2, result.Value.Steps[0].Parameters.Length);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void ModifierApplicationAndPartialRemovalBindCalculatedStackCounts()
    {
        var (executor, run) = Fixture();
        var result = executor.Execute(EffectTransactionTests.Request(new()
        {
            Type = EffectType.APPLY_MODIFIER, Target = EffectTarget.SELF, ModifierId = "charges",
            Parameters = [Parameter(EffectNumericParameter.ModifierStacks, "stacks", "stack_input")]
        }, new()
        {
            Type = EffectType.REMOVE_MODIFIER, Target = EffectTarget.SELF, ModifierId = "charges",
            Parameters = [Parameter(EffectNumericParameter.ModifierStacks, "stacks", "remove_input")]
        }) with { Run = run, Variables = new Dictionary<string, float> { ["stack_input"] = 5.8f, ["remove_input"] = 2.9f } });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(3, Assert.Single(result.Value.Run!.Modifiers).Stacks);
        Assert.Equal(5, result.Value.Records[1].StackChanges[0].PreviousStacks);
        Assert.Equal(3, result.Value.Records[1].StackChanges[0].CurrentStacks);
        Assert.Empty(run.Modifiers);
    }

    [Fact]
    public void CardFlowReceivesOneCalculatedCountNotRepeatedCommands()
    {
        var (executor, run) = Fixture();
        var result = executor.Execute(EffectTransactionTests.Request(new EffectDefinition
        {
            Type = EffectType.CARD_ZONE_FLOW, Target = EffectTarget.SELF, CardZoneFlowId = "draw",
            Parameters = [Parameter(EffectNumericParameter.CardCount, "cards", "count_input")]
        }) with { Run = run, Variables = new Dictionary<string, float> { ["count_input"] = 3.8f } });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(3, result.Value.Run!.Deck.Topology.GetZone("active", "$run")!.InstanceIds.Count);
        Assert.Single(result.Value.Records);
        Assert.Single(result.Value.Steps);
        Assert.Equal(3, result.Value.Calculations[0].Value);
        Assert.Empty(run.Deck.Topology.GetZone("active", "$run")!.InstanceIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(float.MaxValue)]
    public void InvalidCalculatedCountsFailTheWholeTransaction(float count)
    {
        var (executor, run) = Fixture();
        var request = EffectTransactionTests.Request(EffectTransactionTests.Resource(EffectType.DAMAGE, 1), new()
        {
            Type = EffectType.APPLY_MODIFIER, Target = EffectTarget.SELF, ModifierId = "charges",
            Parameters = [Parameter(EffectNumericParameter.ModifierStacks, "stacks", "count")]
        }) with { Run = run, Variables = new Dictionary<string, float> { ["count"] = count } };
        var before = CanonicalJson.ComputeHash(request);
        Assert.True(executor.Execute(request).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void AliasesForAmountRemainGenericAndCalculatedAmountUsesItsDeclaredUnit()
    {
        var (executor, run) = Fixture();
        var request = EffectTransactionTests.Request(EffectTransactionTests.Resource(EffectType.HEAL, null) with
        { Parameters = [Parameter(EffectNumericParameter.Amount, "magnitude", "amount") with { UnitId = "scalar", Conversion = new() }] }) with
        { Run = run, Variables = new Dictionary<string, float> { ["amount"] = 2.5f } };
        var result = executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(12.5f, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Single(result.Value.Calculations);
        Assert.Single(result.Value.Steps[0].Parameters);
        Assert.False(result.Value.Calculations[0].CaptureOnly);
        Assert.Equal("Amount.FormulaValue", result.Value.Calculations[0].BaseTrace[0].Attribute);
    }

    [Fact]
    public void DuplicateIncompatibleAndAmbiguousNumericFieldsAreRejected()
    {
        var parameter = Parameter(EffectNumericParameter.StatusStacks, "stacks", "input");
        var effect = new EffectDefinition { Type = EffectType.APPLY_STATUS, StatusId = "charges", Parameters = [parameter] };
        Assert.Empty(EffectDefinitionValidator.Validate([effect]));
        Assert.NotEmpty(EffectDefinitionValidator.Validate([effect with { Parameters = [parameter, parameter] }]));
        Assert.NotEmpty(EffectDefinitionValidator.Validate([effect with { StatusStacks = 2 }]));
        Assert.NotEmpty(EffectDefinitionValidator.Validate([effect with { Parameters = [parameter with { Parameter = EffectNumericParameter.CardCount }] }]));
        Assert.NotEmpty(EffectDefinitionValidator.Validate([effect with { Parameters = [parameter with { Conversion = new() }] }]));
        Assert.NotEmpty(EffectDefinitionValidator.Validate([effect with { Parameters = [parameter with { UnitId = "" }] }]));
    }

    [Fact]
    public void CalculatedCountMustMatchExplicitCardInstanceSelection()
    {
        var (executor, run) = Fixture();
        var request = EffectTransactionTests.Request(new EffectDefinition
        {
            Type = EffectType.CARD_ZONE_FLOW, Target = EffectTarget.SELF, CardZoneFlowId = "draw",
            CardInstanceIds = run.Deck.Topology.GetZone("reserve", "$run")!.InstanceIds.Take(3).ToImmutableArray(),
            Parameters = [Parameter(EffectNumericParameter.CardCount, "cards", "count")]
        }) with { Run = run, Variables = new Dictionary<string, float> { ["count"] = 2 } };
        var before = CanonicalJson.ComputeHash(request);
        var result = executor.Execute(request);
        Assert.True(result.IsFailure);
        Assert.Contains("match calculated cardCount", result.Error);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    internal static EffectNumericParameterDefinition Parameter(EffectNumericParameter field, string pipeline, string formula) => new()
    {
        Parameter = field, Channel = pipeline == "magnitude" ? "effect_amount" : pipeline,
        PipelineId = pipeline, UnitId = pipeline, FormulaValue = formula,
        Conversion = new() { RequireInteger = true, Rounding = CalculationRounding.Floor }
    };

    private static (EffectTriggerExecutor Executor, RunState Run) Fixture()
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(item => item.Evaluate(It.IsAny<string>(), It.IsAny<Dictionary<string, float>>(), It.IsAny<float>()))
            .Returns((string expression, Dictionary<string, float> variables, float initial) =>
                variables.TryGetValue(expression, out var number) ? Result<float>.Success(number) : Result<float>.Failure("Unknown variable"));
        var pipelines = new[] { "magnitude", "stacks", "cards", "turns" }.ToDictionary(id => id, id => new CalculationPipelineDefinition
        { PipelineId = id, Channel = id == "magnitude" ? "effect_amount" : id, UnitId = id == "magnitude" ? "scalar" : id,
            Buckets = [new() { BucketId = "identity" }] });
        var artifacts = new Dictionary<string, JsonElement>
        {
            ["calculation-pipelines/numeric.json"] = JsonSerializer.SerializeToElement(pipelines),
            ["status-effects/numeric.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, StatusEffectDefinition>
            { ["charges"] = new() { StatusId = "charges", DefaultDuration = -1, DurationTickBoundary = StatusTriggerBoundary.EndActivation } }),
            ["modifiers/numeric.json"] = JsonSerializer.SerializeToElement(new Dictionary<string, ScriptModifierDefinition>
            { ["charges"] = new() { ModifierId = "charges" } })
        };
        var runtime = ContentRuntime.Create(new()
        {
            Manifest = new() { Revision = "revision", ConfigName = "default", Artifacts = artifacts.Select(item => new ContentArtifactManifest
                { Kind = item.Key.Split('/')[0], Path = item.Key, DefinitionCount = item.Key.StartsWith("calculation-") ? 4 : 1 }).ToArray() },
            Artifacts = artifacts.ToImmutableDictionary()
        });
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(item => item.Resolve("revision", It.IsAny<string?>())).Returns(Result<ContentRuntime>.Success(runtime.Value));
        var zones = new CardZoneSystemDefinition
        {
            CardZoneSystemId = "numeric-zones", Zones = [new() { ZoneId = "reserve", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered },
                new() { ZoneId = "active", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }],
            Flows = [new() { FlowId = "draw", AllowedInvocations = [CardZoneFlowInvocation.Effect], Steps = [new()
            {
                StepId = "move", Operation = CardZoneOperation.Move, SourceZoneId = "reserve", TargetZoneId = "active",
                SourceOwner = CardZoneOwnerBinding.RunOwner, TargetOwner = CardZoneOwnerBinding.RunOwner,
                Selection = new() { Strategy = CardZoneSelectionStrategy.First, CountFormula = "requestedCount" }
            }] }]
        };
        var topology = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(zones).Value, new()
        { RunOwnerId = "$run", Batches = [new() { ZoneId = "reserve", OwnerId = "$run", DefinitionIds = ["a", "b", "c", "d"] }] },
            DeterministicContext.Create(1, "revision")).Value;
        var run = new RunState
        {
            PlayerEntityId = "hero", Determinism = topology.Context, Deck = new() { Topology = topology.State },
            ResolvedMode = new() { Definition = new() { CalculationPipelineIds = pipelines.Keys.ToArray() }, CardZoneSystem = zones }
        };
        return (new EffectTriggerExecutor(formulas.Object, new ImmutableEffectProcessor(), runtimes.Object,
            new CalculationEngine(formulas.Object), new CompositeCalculationInfluenceProvider([]), cardZoneFlows:
                new CardZoneFlowExecutor(new CardZoneRuntimeRuleEvaluator(formulas.Object, Mock.Of<ICardZoneCardMetadataResolver>()))), run);
    }
}
