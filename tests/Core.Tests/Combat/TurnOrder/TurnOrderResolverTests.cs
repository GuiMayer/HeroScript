using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Common;
using Core.Determinism;
using Core.Math;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Combat.TurnOrder;

public sealed class TurnOrderResolverTests
{
    [Fact]
    public void FixedPolicy_PreservesAuthoredActorOrderAndSerializableState()
    {
        var resolver = Resolver();
        var combat = TurnOrderTestHelper.CreateTestCombatState("hero", ["enemy_b", "enemy_a"]) with
        {
            Determinism = DeterministicContext.Create(10, "revision")
        };

        var result = resolver.Initialize(combat, FixedPolicy());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["hero", "enemy_b", "enemy_a"], result.Value.Order);
        Assert.Equal(TurnOrderStrategy.Fixed, result.Value.State.TurnOrderState.Strategy);
        Assert.Equal(TurnOrderRecalculationBoundary.CombatStart,
            result.Value.State.TurnOrderState.LastBoundary);
        Assert.Equal(64, result.Value.State.TurnOrderState.PolicyFingerprint.Length);
        Assert.Equal(result.Value.Order, result.Value.State.TurnOrderState.Order);
    }

    [Fact]
    public void SameResolver_CanEvaluateDifferentPinnedModesWithoutInterference()
    {
        var resolver = Resolver();
        var combat = TurnOrderTestHelper.CreateTestCombatStateWithSpeed(
            ("hero", 2), [("enemy", 8)]) with
        {
            Determinism = DeterministicContext.Create(11, "revision")
        };

        var fixedResult = resolver.Initialize(combat, FixedPolicy());
        var resourceResult = resolver.Initialize(combat, ResourcePolicy());
        var fixedAgain = resolver.Initialize(combat, FixedPolicy());

        Assert.Equal(["hero", "enemy"], fixedResult.Value.Order);
        Assert.Equal(["enemy", "hero"], resourceResult.Value.Order);
        Assert.Equal(fixedResult.Value.Order, fixedAgain.Value.Order);
        Assert.Equal(
            CanonicalJson.ComputeHash(fixedResult.Value.State),
            CanonicalJson.ComputeHash(fixedAgain.Value.State));
    }

    [Fact]
    public void ResourcePolicy_RecalculatesAtConfiguredBoundaryAfterResourceChange()
    {
        var resolver = Resolver();
        var policy = ResourcePolicy() with
        {
            RecalculateAt = TurnOrderRecalculationBoundary.RoundStart
        };
        var combat = TurnOrderTestHelper.CreateTestCombatStateWithSpeed(
            ("hero", 10), [("enemy", 5)]) with
        {
            Determinism = DeterministicContext.Create(12, "revision")
        };
        var initialized = resolver.Initialize(combat, policy).Value.State;
        var enemy = initialized.GetActor("enemy")!;
        var speed = enemy.GetResource("speed")! with { Current = 20 };
        var changed = initialized.ReplaceActor(enemy with
        {
            ResourceState = enemy.ResourceState with
            {
                Resources = enemy.ResourceState.Resources
                    .ToDictionary(pair => pair.Key, pair => pair.Key == "speed" ? speed : pair.Value,
                        StringComparer.Ordinal)
            }
        });

        var ignored = resolver.Recalculate(
            changed,
            policy,
            TurnOrderRecalculationBoundary.ActivationEnd);
        var recalculated = resolver.Recalculate(
            changed,
            policy,
            TurnOrderRecalculationBoundary.RoundStart);

        Assert.Equal(["hero", "enemy"], ignored.Value.Order);
        Assert.Equal(["enemy", "hero"], recalculated.Value.Order);
        Assert.Equal(2UL, recalculated.Value.State.TurnOrderState.Epoch);
    }

    [Fact]
    public void InitiativePolicy_PersistsRollsAndReturnsSuccessorRandomContext()
    {
        var policy = new TurnOrderPolicyDefinition
        {
            Strategy = TurnOrderStrategy.Initiative,
            RecalculateAt = TurnOrderRecalculationBoundary.CombatStart,
            TieBreak = StableTieBreak(),
            Initiative = new()
            {
                ModifierResourceId = "speed",
                ResourcePerModifier = 2,
                DieSides = 20
            }
        };
        var combat = TurnOrderTestHelper.CreateTestCombatStateWithSpeed(
            ("hero", 10), [("enemy", 4)]) with
        {
            Determinism = DeterministicContext.Create(99, "revision")
        };

        var first = Resolver().Initialize(combat, policy);
        var replay = Resolver().Initialize(combat, policy);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(2, first.Value.State.TurnOrderState.InitiativeRolls.Count);
        Assert.NotEqual(combat.Determinism.RandomState, first.Value.State.Determinism.RandomState);
        Assert.Equal(
            CanonicalJson.ComputeHash(first.Value.State),
            CanonicalJson.ComputeHash(replay.Value.State));
    }

    [Fact]
    public void AtbPolicy_PersistsGaugesAndReplaysActivationCompletion()
    {
        var policy = new TurnOrderPolicyDefinition
        {
            Strategy = TurnOrderStrategy.Atb,
            RecalculateAt = TurnOrderRecalculationBoundary.ContinuousTick,
            TieBreak = StableTieBreak(),
            Atb = new()
            {
                RateResourceId = "speed",
                FillRate = 10,
                ReferenceResourceValue = 10,
                ReadyThreshold = 100
            }
        };
        var combat = TurnOrderTestHelper.CreateTestCombatStateWithSpeed(
            ("hero", 20), [("enemy", 5)]) with
        {
            Determinism = DeterministicContext.Create(123, "revision")
        };
        var initialized = Resolver().Initialize(combat, policy);
        var actor = initialized.Value.Order[0];

        var first = Resolver().CompleteActivation(initialized.Value.State, policy, actor, true);
        var branch = Resolver().CompleteActivation(initialized.Value.State, policy, actor, true);

        Assert.Equal("hero", actor);
        Assert.True(initialized.Value.State.TurnOrderState.ContinuousTick > 0);
        Assert.True(first.Value.State.TurnOrderState.ContinuousTick >
            initialized.Value.State.TurnOrderState.ContinuousTick);
        Assert.Contains(actor, first.Value.State.TurnOrderState.AtbGauges.Keys);
        Assert.Equal(
            CanonicalJson.ComputeHash(first.Value.State),
            CanonicalJson.ComputeHash(branch.Value.State));
    }

    [Fact]
    public void ConditionalPolicy_UsesSharedFormulaRuntimeInsteadOfCallback()
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(item => item.Evaluate(
                "actor_resource_speed_current",
                It.IsAny<Dictionary<string, float>>(),
                It.IsAny<float>()))
            .Returns((string _, Dictionary<string, float> variables, float _) =>
                Result<float>.Success(variables["actor_resource_speed_current"]));
        var resolver = new TurnOrderResolver(formulas.Object);
        var policy = new TurnOrderPolicyDefinition
        {
            Strategy = TurnOrderStrategy.Conditional,
            RecalculateAt = TurnOrderRecalculationBoundary.ActivationEnd,
            TieBreak = StableTieBreak(),
            Conditional = new()
            {
                ScoreExpression = "actor_resource_speed_current",
                Direction = TurnOrderDirection.Ascending
            }
        };
        var combat = TurnOrderTestHelper.CreateTestCombatStateWithSpeed(
            ("hero", 8), [("enemy", 3)]) with
        {
            Determinism = DeterministicContext.Create(13, "revision")
        };

        var result = resolver.Initialize(combat, policy);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["enemy", "hero"], result.Value.Order);
    }

    [Fact]
    public void TiePolicies_ApplyControllerBiasAndConsumeSeededEpochDeterministically()
    {
        var combat = TurnOrderTestHelper.CreateTestCombatStateWithSpeed(
            ("z_player", 5), [("a_ai", 5)]) with
        {
            Determinism = DeterministicContext.Create(77, "revision")
        };
        var biased = ResourcePolicy() with
        {
            TieBreak = new() { Strategy = TurnOrderTieBreakStrategy.PlayerControlledFirst }
        };
        var seeded = ResourcePolicy() with
        {
            RecalculateAt = TurnOrderRecalculationBoundary.RoundStart,
            TieBreak = new()
            {
                Strategy = TurnOrderTieBreakStrategy.SeededRandom,
                Epoch = TurnOrderTieBreakEpoch.RoundStart
            }
        };

        var biasResult = Resolver().Initialize(combat, biased);
        var initialSeeded = Resolver().Initialize(combat, seeded);
        var nextSeeded = Resolver().Recalculate(
            initialSeeded.Value.State,
            seeded,
            TurnOrderRecalculationBoundary.RoundStart);
        var replay = Resolver().Initialize(combat, seeded);

        Assert.Equal("z_player", biasResult.Value.Order[0]);
        Assert.Equal(1UL, initialSeeded.Value.State.TurnOrderState.TieBreakGeneration);
        Assert.Equal(2UL, nextSeeded.Value.State.TurnOrderState.TieBreakGeneration);
        Assert.NotEqual(
            initialSeeded.Value.State.Determinism.RandomState,
            nextSeeded.Value.State.Determinism.RandomState);
        Assert.Equal(
            CanonicalJson.ComputeHash(initialSeeded.Value.State),
            CanonicalJson.ComputeHash(replay.Value.State));
    }

    [Fact]
    public void PolicyValidator_RejectsMixedOrInvalidTaggedDefinitions()
    {
        var mixed = FixedPolicy() with
        {
            Resource = new()
            {
                ResourceId = "speed",
                Direction = TurnOrderDirection.Descending
            }
        };
        var invalidAtb = new TurnOrderPolicyDefinition
        {
            Strategy = TurnOrderStrategy.Atb,
            RecalculateAt = TurnOrderRecalculationBoundary.RoundStart,
            TieBreak = StableTieBreak(),
            Atb = new()
        };

        Assert.True(TurnOrderPolicyValidator.Validate(mixed).IsFailure);
        Assert.True(TurnOrderPolicyValidator.Validate(invalidAtb).IsFailure);
    }

    private static TurnOrderResolver Resolver() => new(Mock.Of<IRuntimeFormulaEvaluator>());

    public static TurnOrderPolicyDefinition FixedPolicy() => new()
    {
        Strategy = TurnOrderStrategy.Fixed,
        RecalculateAt = TurnOrderRecalculationBoundary.CombatStart,
        TieBreak = StableTieBreak()
    };

    public static TurnOrderPolicyDefinition ResourcePolicy() => new()
    {
        Strategy = TurnOrderStrategy.Resource,
        RecalculateAt = TurnOrderRecalculationBoundary.ActivationEnd,
        TieBreak = StableTieBreak(),
        Resource = new()
        {
            ResourceId = "speed",
            Direction = TurnOrderDirection.Descending
        }
    };

    private static TurnOrderTieBreakDefinition StableTieBreak() => new()
    {
        Strategy = TurnOrderTieBreakStrategy.StableActorId
    };
}
