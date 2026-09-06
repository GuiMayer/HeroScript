using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class CombatResourceLifecycleTests
{
    [Fact]
    public void Process_UsesImmutableEffectPipelineForMatchingActorBoundary()
    {
        var lifecycle = new CombatResourceLifecycle(new EffectTriggerExecutor(
            new StubFormulaEvaluator(), new ImmutableEffectProcessor()));
        var combat = CombatTransitions.Create(
            Entity(
                Pool("energy", 1, 3, new RegenerationConfig
                {
                    Enabled = true,
                    AmountPerTurn = 2,
                    Timing = RegenerationTiming.START_TURN
                }),
                Pool("block", 8, 999, new RegenerationConfig
                {
                    Enabled = true,
                    AmountPerTurn = -999,
                    Timing = RegenerationTiming.END_TURN
                })),
            [],
            DeterministicContext.Create(42, "revision"));
        var run = Run();

        var first = lifecycle.Process(run, combat, "hero", RegenerationTiming.START_TURN);
        var repeated = lifecycle.Process(run, combat, "hero", RegenerationTiming.START_TURN);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(repeated.IsSuccess, repeated.IsFailure ? repeated.Error : null);
        Assert.Equal(3, first.Value.Combat.Hero.GetResource("energy")!.Current);
        Assert.Equal(8, first.Value.Combat.Hero.GetResource("block")!.Current);
        Assert.Equal(1, combat.Hero.GetResource("energy")!.Current);
        Assert.Single(first.Value.Records);
        Assert.Equal(EffectProvenanceKind.Rule, first.Value.Records[0].Provenance.Kind);
        Assert.Equal("resource:energy:regeneration", first.Value.Records[0].Provenance.SourceId);
        Assert.Equal(first.Value.Fingerprint, repeated.Value.Fingerprint);
    }

    [Fact]
    public void Process_AppliesNegativeLifecycleAmountAtEndBoundary()
    {
        var lifecycle = new CombatResourceLifecycle(new EffectTriggerExecutor(
            new StubFormulaEvaluator(), new ImmutableEffectProcessor()));
        var combat = CombatTransitions.Create(
            Entity(Pool("block", 8, 999, new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = -999,
                Timing = RegenerationTiming.END_TURN
            })),
            [],
            DeterministicContext.Create(42, "revision"));

        var result = lifecycle.Process(Run(), combat, "hero", RegenerationTiming.END_TURN);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(0, result.Value.Combat.Hero.GetResource("block")!.Current);
        Assert.Equal(8, combat.Hero.GetResource("block")!.Current);
    }

    [Fact]
    public void Process_ExcludesModeOwnedResourceCycleFromIntrinsicRegeneration()
    {
        var lifecycle = new CombatResourceLifecycle(new EffectTriggerExecutor(
            new StubFormulaEvaluator(), new ImmutableEffectProcessor()));
        var combat = CombatTransitions.Create(
            Entity(Pool("energy", 1, 3, new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = 2,
                Timing = RegenerationTiming.START_TURN
            })),
            [],
            DeterministicContext.Create(42, "revision"));

        var result = lifecycle.Process(
            Run(),
            combat,
            "hero",
            RegenerationTiming.START_TURN,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ENERGY" });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Same(combat, result.Value.Combat);
        Assert.Empty(result.Value.Records);
    }

    [Fact]
    public void Process_DoesNotFallbackWhenFormulaFails()
    {
        var lifecycle = new CombatResourceLifecycle(new EffectTriggerExecutor(
            new StubFormulaEvaluator(Result<float>.Failure("invalid formula")), new ImmutableEffectProcessor()));
        var combat = CombatTransitions.Create(
            Entity(Pool("mana", 1, 10, new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = 5,
                Formula = "missing_formula",
                Timing = RegenerationTiming.START_TURN
            })),
            [],
            DeterministicContext.Create(42, "revision"));

        var result = lifecycle.Process(Run(), combat, "hero", RegenerationTiming.START_TURN);

        Assert.True(result.IsFailure);
        Assert.Contains("invalid formula", result.Error);
        Assert.Equal(1, combat.Hero.GetResource("mana")!.Current);
    }

    [Fact]
    public void Process_IsSequentialAndReturnsUniversalExecutionSteps()
    {
        var formulas = new StubFormulaEvaluator(evaluate: variables =>
            Result<float>.Success(variables!["source.resources.a.current"]));
        var lifecycle = new CombatResourceLifecycle(new EffectTriggerExecutor(formulas, new ImmutableEffectProcessor()));
        var combat = CombatTransitions.Create(Entity(
            Pool("a", 1, 10, new() { Enabled = true, AmountPerTurn = 2, Timing = RegenerationTiming.START_TURN }),
            Pool("b", 1, 10, new() { Enabled = true, Formula = "source.resources.a.current", Timing = RegenerationTiming.START_TURN })),
            [], DeterministicContext.Create(42, "revision"));

        var result = lifecycle.Process(Run(), combat, "hero", RegenerationTiming.START_TURN);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(3, result.Value.Combat.Hero.GetResource("a")!.Current);
        Assert.Equal(4, result.Value.Combat.Hero.GetResource("b")!.Current);
        Assert.Equal(2, result.Value.Steps.Length);
        Assert.All(result.Value.Steps, step => Assert.Equal(EffectProvenanceKind.Rule, step.Provenance.Kind));
        Assert.Equal(1, combat.Hero.GetResource("a")!.Current);
        Assert.Equal(1, combat.Hero.GetResource("b")!.Current);
    }

    [Fact]
    public void Process_FailureAfterEarlierRuleDiscardsTheWholeBoundary()
    {
        var lifecycle = new CombatResourceLifecycle(new EffectTriggerExecutor(
            new StubFormulaEvaluator(Result<float>.Failure("broken")), new ImmutableEffectProcessor()));
        var combat = CombatTransitions.Create(Entity(
            Pool("a", 1, 10, new() { Enabled = true, AmountPerTurn = 2, Timing = RegenerationTiming.START_TURN }),
            Pool("b", 1, 10, new() { Enabled = true, Formula = "broken", Timing = RegenerationTiming.START_TURN })),
            [], DeterministicContext.Create(42, "revision"));

        Assert.True(lifecycle.Process(Run(), combat, "hero", RegenerationTiming.START_TURN).IsFailure);
        Assert.Equal(1, combat.Hero.GetResource("a")!.Current);
        Assert.Equal(1, combat.Hero.GetResource("b")!.Current);
    }

    private static RunState Run() => new()
    {
        RunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        PlayerEntityId = "hero",
        Determinism = DeterministicContext.Create(42, "revision")
    };

    private static CombatEntity Entity(params ResourcePool[] pools) => new()
    {
        EntityId = "hero",
        Name = "Hero",
        IsHero = true,
        ResourceState = new ResourceSet
        {
            OwnerId = "hero",
            Resources = pools.ToDictionary(pool => pool.ResourceId, StringComparer.OrdinalIgnoreCase)
        }
    };

    private static ResourcePool Pool(
        string id,
        float current,
        float maximum,
        RegenerationConfig regeneration) => new()
        {
            ResourceId = id,
            Current = current,
            Minimum = 0,
            Maximum = maximum,
            Definition = new ResourceDefinition
            {
                ResourceId = id,
                DisplayName = id,
                DefaultMin = 0,
                DefaultMax = maximum,
                DefaultCurrent = current,
                Regeneration = regeneration
            }
        };

    private sealed class StubFormulaEvaluator : IRevisionedRuntimeFormulaEvaluator
    {
        private readonly Result<float> _result;
        private readonly Func<Dictionary<string, float>?, Result<float>>? _evaluate;

        public StubFormulaEvaluator(Result<float>? result = null,
            Func<Dictionary<string, float>?, Result<float>>? evaluate = null)
        {
            _result = result ?? Result<float>.Success(1);
            _evaluate = evaluate;
        }

        public Result<float> Evaluate(
            string expressionOrFormulaId,
            Dictionary<string, float>? variables = null,
            float initialValue = 0) => _evaluate?.Invoke(variables) ?? _result;

        public Result<float> EvaluateAtRevision(
            string expressionOrFormulaId,
            string contentRevision,
            Dictionary<string, float>? variables = null,
            float initialValue = 0) => _evaluate?.Invoke(variables) ?? _result;
    }
}
