using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Math;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResponseDispatchTests
{
    private static GameplayResponseDefinition Definition() => GameplayResponseCompilationTests.Definition() with
    { Effects = [new() { Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF,
        TargetResource = "focus", FlatValue = 2, Operation = ResourceEffectOperation.ADD }] };

    private static (EffectTriggerExecutionRequest Request, GameplayEffectObservation Observation) Input(params EffectDefinition[] effects)
    {
        var context = GameplayResponseCompilationTests.Context();
        var observation = new GameplayEffectObservation
        {
            Events = GameplayEventTransaction.Create(GameplayResponseMatcherTests.Fact().Scope, context.Catalog).Value,
            PublisherId = GameplayCoreCatalog.PublisherId, ReasonId = "effect.application", CostReasonId = "cost.payment", RefundReasonId = "cost.refund"
        };
        return (EffectTransactionTests.Request(effects.Length == 0 ? [EffectTransactionTests.Resource(EffectType.DAMAGE, 3)] : effects) with
            { Run = context.Run }, observation);
    }

    private static IGameplayObserverProvider[] Providers(GameplayResponseDefinition? definition = null) =>
        [new GameplayResponseCompilationTests.FixedProvider(GameplayResponseCompilationTests.Registration(definition: definition ?? Definition()))];

    [Fact]
    public void NoObserversIsExactlyTheExistingObservedExecution()
    {
        var input = Input(); var executor = EffectTransactionTests.Executor();
        var expected = executor.ExecuteObserved(input.Request, input.Observation).Value;
        var result = executor.ExecuteResponsiveObserved(input.Request, input.Observation, GameplayResponseCompilationTests.Catalog(), []);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(expected.Execution), CanonicalJson.ComputeHash(result.Value.Execution));
        Assert.Equal(CanonicalJson.ComputeHash(expected.Observation.Events.Occurrences), CanonicalJson.ComputeHash(result.Value.Observation.Events.Occurrences));
        Assert.Equal(0, result.Value.Quotas.Count);
    }

    [Fact]
    public void RecordOnlyUsesCanonicalEffectsAndRecordsCausalChildrenWithoutRecursing()
    {
        var input = Input(); var executor = EffectTransactionTests.Executor();
        var original = CanonicalJson.ComputeHash(input.Request);
        var outputs = Enumerable.Range(0, 5).Select(_ => executor.ExecuteResponsiveObserved(input.Request, input.Observation,
            GameplayResponseCompilationTests.Catalog(), Providers())).ToArray();
        Assert.All(outputs, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        var output = outputs[0].Value;
        Assert.Equal(7, output.Execution.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(12, output.Execution.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(2, output.Execution.Records.Count); Assert.Equal(2, output.Execution.Steps.Length);
        Assert.Equal(output.Execution.Steps[0].StateAfterHash, output.Execution.Steps[1].StateBeforeHash);
        var facts = output.Observation.Events.Occurrences;
        Assert.Equal(2, facts.Count); Assert.Equal(facts[0].EventId, facts[1].ParentEventId); Assert.Equal(1, facts[1].Depth);
        Assert.Equal(1, output.Quotas.Count); Assert.Empty(input.Observation.Events.Occurrences);
        Assert.Equal(original, CanonicalJson.ComputeHash(input.Request));
        Assert.Single(outputs.Select(result => CanonicalJson.ComputeHash(result.Value.Execution)).Distinct());
        Assert.Single(outputs.Select(result => CanonicalJson.ComputeHash(result.Value.Observation.Events.Occurrences)).Distinct());
    }

    [Fact]
    public void AllowedPropagationUsesSharedRootQuotaAndCannotPartiallyCommitOnOverflow()
    {
        var input = Input(); var executor = EffectTransactionTests.Executor();
        var definition = Definition() with { Propagation = ResponsePropagation.AllowResponses,
            Frequency = new() { Scope = ResponseFrequencyScope.RootAction, Maximum = 2 } };
        var result = executor.ExecuteResponsiveObserved(input.Request, input.Observation, GameplayResponseCompilationTests.Catalog(), Providers(definition));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.Quotas.Count); Assert.Equal(3, result.Value.Execution.Steps.Length);
        Assert.Equal(14, result.Value.Execution.State.GetActor("hero")!.GetResource("focus")!.Current);
        var original = CanonicalJson.ComputeHash(input.Request);
        var unbounded = definition with { Frequency = new() };
        var failed = executor.ExecuteResponsiveObserved(input.Request, input.Observation, GameplayResponseCompilationTests.Catalog(), Providers(unbounded), maximumInvocations: 3);
        Assert.True(failed.IsFailure); Assert.Contains("budget", failed.Error);
        Assert.Equal(original, CanonicalJson.ComputeHash(input.Request)); Assert.Empty(input.Observation.Events.Occurrences);
    }

    [Fact]
    public void AFailingResponseRollsBackAlreadyAppliedParent()
    {
        var input = Input(); var original = CanonicalJson.ComputeHash(input.Request);
        var definition = Definition() with { Effects = [Definition().Effects[0] with { TargetResource = "absent" }] };
        var result = EffectTransactionTests.Executor().ExecuteResponsiveObserved(input.Request, input.Observation,
            GameplayResponseCompilationTests.Catalog(), Providers(definition));
        Assert.True(result.IsFailure); Assert.Equal(original, CanonicalJson.ComputeHash(input.Request));
        Assert.Empty(input.Observation.Events.Occurrences);
    }

    [Fact]
    public void ConditionsAndTypedBodyValuesUseCapturedRevisionAndFrozenFact()
    {
        var formulas = new Mock<IRevisionedRuntimeFormulaEvaluator>();
        var revisions = new List<string>();
        formulas.Setup(item => item.EvaluateAtRevision(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, float>>(), It.IsAny<float>()))
            .Returns((string expression, string revision, Dictionary<string, float> values, float initial) =>
            { revisions.Add(revision); return Result<float>.Success(values[expression]); });
        var executor = new EffectTriggerExecutor(formulas.Object, new ImmutableEffectProcessor(), allowUnconfiguredCalculations: true);
        var input = Input();
        var definition = Definition() with { Condition = "owner.resources.focus.current",
            Effects = [Definition().Effects[0] with { FlatValue = null, FormulaValue = "event.change.operand" }] };
        var result = executor.ExecuteResponsiveObserved(input.Request, input.Observation, GameplayResponseCompilationTests.Catalog(), Providers(definition));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(13, result.Value.Execution.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.NotEmpty(revisions); Assert.All(revisions, revision => Assert.Equal("captured-definition", revision));
    }

    [Fact]
    public void WorkBudgetIsSharedAcrossAllResponseBatches()
    {
        var effects = Enumerable.Range(0, 6).Select(index => EffectTransactionTests.Resource(EffectType.DAMAGE, 0) with { EffectId = "root" + index, Repeat = 256 }).ToArray();
        var input = Input(effects); var executor = EffectTransactionTests.Executor();
        Assert.True(executor.Execute(input.Request).IsSuccess);
        var definition = Definition() with { Effects = [Definition().Effects[0] with { FlatValue = 0 }] };
        var original = CanonicalJson.ComputeHash(input.Request);
        var failed = executor.ExecuteResponsiveObserved(input.Request, input.Observation, GameplayResponseCompilationTests.Catalog(), Providers(definition));
        Assert.True(failed.IsFailure); Assert.Contains("Effect execution limit", failed.Error);
        Assert.Equal(original, CanonicalJson.ComputeHash(input.Request)); Assert.Empty(input.Observation.Events.Occurrences);
    }

    private sealed class ActiveWhileResourceIsPositive(params GameplayObserverRegistration[] registrations) : IGameplayObserverProvider
    {
        public string ProviderId => "test";
        public Result<ImmutableArray<GameplayObserverRegistration>> Collect(GameplayObserverCollectionContext context) =>
            Result<ImmutableArray<GameplayObserverRegistration>>.Success(context.Combat!.GetActor("hero")!.GetResource("focus")!.Current > 0
                ? registrations.ToImmutableArray() : []);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public void EligibilityIsFrozenButRegistrationRevalidationIsExplicit(bool revalidate, int expected)
    {
        var input = Input();
        var first = GameplayResponseCompilationTests.Registration("first", Definition() with
        { ResponseId = "first", Effects = [Definition().Effects[0] with { FlatValue = 10, Operation = ResourceEffectOperation.SUBTRACT }] });
        var second = GameplayResponseCompilationTests.Registration("second", Definition() with
        { ResponseId = "second", Order = new() { Priority = 1 }, Availability = new() { RequireRegistrationStillActive = revalidate } });
        var result = EffectTransactionTests.Executor().ExecuteResponsiveObserved(input.Request, input.Observation,
            GameplayResponseCompilationTests.Catalog(), [new ActiveWhileResourceIsPositive(first, second)]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(expected, result.Value.Execution.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(revalidate ? 1 : 2, result.Value.Quotas.Count);
    }

    [Fact]
    public void FalseConditionDoesNotConsumeQuotaAndInvalidConditionFailsAtomically()
    {
        var input = Input();
        var evaluator = new Mock<IRevisionedRuntimeFormulaEvaluator>();
        evaluator.Setup(item => item.EvaluateAtRevision("0", "captured-definition", It.IsAny<Dictionary<string, float>>(), It.IsAny<float>()))
            .Returns(Result<float>.Success(0));
        var executor = new EffectTriggerExecutor(evaluator.Object, new ImmutableEffectProcessor(), allowUnconfiguredCalculations: true);
        var result = executor.ExecuteResponsiveObserved(input.Request, input.Observation, GameplayResponseCompilationTests.Catalog(),
            Providers(Definition() with { Condition = "0" }));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(0, result.Value.Quotas.Count); Assert.Single(result.Value.Execution.Steps);
        var invalid = executor.ExecuteResponsiveObserved(input.Request, input.Observation, GameplayResponseCompilationTests.Catalog(),
            Providers(Definition() with { Condition = "event.absent" }));
        Assert.True(invalid.IsFailure); Assert.Empty(input.Observation.Events.Occurrences);
    }
}
