using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class EffectTransactionTests
{
    [Fact]
    public void LaterFormulaObservesEarlierResourceChange()
    {
        var request = Request(Resource(EffectType.DAMAGE, 3), Resource(EffectType.DAMAGE, null) with
        { FormulaValue = "target.resources.focus.current" });
        var result = Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(0, result.Value.State.GetEntity("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(10, request.Combat.GetEntity("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(2, result.Value.Steps.Length);
        Assert.Equal(result.Value.Steps[0].StateAfterHash, result.Value.Steps[1].StateBeforeHash);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SkippedParentNeverExecutesChildren(bool chance)
    {
        var effect = Resource(EffectType.DAMAGE, 3) with
        {
            Chance = chance ? 0 : 1, Condition = chance ? null : "false",
            ChainedEffects = [Resource(EffectType.DAMAGE, 10)]
        };
        var result = Executor().Execute(Request(effect));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Empty(result.Value.Records);
        Assert.False(Assert.Single(result.Value.Steps).Applied);
    }

    [Fact]
    public void LastFailureRollsBackResourcesAndRandomCursor()
    {
        var request = Request(Resource(EffectType.DAMAGE, 1) with { Chance = .8f },
            Resource(EffectType.DAMAGE, 1) with { TargetResource = "missing" });
        var before = CanonicalJson.ComputeHash(request.Combat);
        Assert.True(Executor().Execute(request).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(request.Combat));
    }

    [Fact]
    public void RecursiveExpansionHasHardBudget()
    {
        var effect = Resource(EffectType.DAMAGE, 0);
        for (var i = 0; i < 40; i++) effect = Resource(EffectType.DAMAGE, 0) with { ChainedEffects = [effect] };
        var result = Executor().Execute(Request(effect));
        Assert.True(result.IsFailure);
        Assert.Contains("limit", result.Error);
    }

    [Theory]
    [InlineData(EffectChanceScope.PerEffect)]
    [InlineData(EffectChanceScope.PerTarget)]
    public void TenExecutionsHaveIdenticalStateStepsRngAndFingerprint(EffectChanceScope scope)
    {
        var request = Request(Resource(EffectType.DAMAGE, 1) with
        { Target = EffectTarget.ALL_ENEMIES, Repeat = 5, Chance = .65f, ChanceScope = scope });
        var outputs = Enumerable.Range(0, 10).Select(_ => Executor().Execute(request)).ToArray();
        Assert.All(outputs, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(outputs.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct());
    }

    internal static EffectTriggerExecutor Executor()
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(item => item.Evaluate(It.IsAny<string>(), It.IsAny<Dictionary<string, float>>()))
            .Returns((string expression, Dictionary<string, float> variables, float initialValue) => expression == "false"
                ? Result<float>.Success(0) : variables.TryGetValue(expression, out var value)
                    ? Result<float>.Success(value) : Result<float>.Failure("Unknown variable"));
        return new(formulas.Object, new ImmutableEffectProcessor());
    }

    internal static EffectTriggerExecutionRequest Request(params EffectDefinition[] effects) => new()
    {
        Combat = GameplayOwnershipTests.State(), OwnerEntityId = "hero", SourceEntityId = "hero",
        ContentRevision = "revision", SelectedTargetEntityIds = ["enemy"],
        Provenance = new() { Kind = EffectProvenanceKind.Ability, SourceId = "test" },
        Trigger = new() { TriggerId = "resolve", Effects = effects }
    };

    internal static EffectDefinition Resource(EffectType type, float? amount) => new()
    { EffectId = "test", Type = type, Target = EffectTarget.TARGET, TargetResource = "focus", FlatValue = amount };
}
