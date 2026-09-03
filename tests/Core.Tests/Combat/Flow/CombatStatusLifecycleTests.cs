using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class CombatStatusLifecycleTests
{
    [Fact]
    public void EndActivation_ProcessesOnlyActiveActorInPriorityOrderAndExpiresDeterministically()
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(item => item.Evaluate("stacks * 2", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Core.Common.Result<float>.Success(4));
        var lifecycle = new CombatStatusLifecycle(new EffectTriggerExecutor(
            formulas.Object,
            new ImmutableEffectProcessor()));
        var combat = CombatTransitions.Create(
            Entity("hero", true, 50),
            [Entity("enemy", false, 50)],
            DeterministicContext.Create(1, "revision")) with
        {
            ActivationState = new()
            {
                ActiveActorId = "enemy",
                ActivationOrder = ["hero", "enemy"]
            },
            StatusEffects = new Dictionary<string, System.Collections.Immutable.ImmutableArray<StatusEffectInstance>>
            {
                ["hero"] = [Status("hero-status", "hero", 9, 1, formula: null)],
                ["enemy"] =
                [
                    Status("low", "enemy", 1, 2, formula: null),
                    Status("high", "enemy", 10, 1, formula: "stacks * 2")
                ]
            }.ToImmutableDictionary(StringComparer.Ordinal)
        };

        var result = lifecycle.Process(combat, StatusTriggerBoundary.EndActivation, "enemy");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(45, result.Value.Combat.Enemies.Single().GetResource("health")!.Current);
        Assert.Equal(50, result.Value.Combat.Hero.GetResource("health")!.Current);
        Assert.Equal(["high", "low"], result.Value.Events
            .Where(item => item.Kind == CombatStatusLifecycleEventKind.Triggered)
            .Select(item => item.StatusId));
        var firstApplication = Assert.Single(result.Value.Events
            .First(item => item.StatusId == "high" && item.Kind == CombatStatusLifecycleEventKind.Triggered)
            .Applications);
        Assert.Equal(EffectType.DAMAGE, firstApplication.EffectType);
        Assert.Equal("health", firstApplication.ResourceId);
        Assert.Equal(50, firstApplication.PreviousValue);
        Assert.Equal(46, firstApplication.CurrentValue);
        Assert.Equal(["high"], result.Value.Events
            .Where(item => item.Kind == CombatStatusLifecycleEventKind.Expired)
            .Select(item => item.StatusId));
        Assert.Equal(1, result.Value.Combat.StatusEffects["enemy"].Single().Duration);
    }

    private static StatusEffectInstance Status(
        string statusId,
        string targetId,
        int priority,
        int duration,
        string? formula) => new()
        {
            InstanceId = Core.Determinism.DeterministicId.Create(3, (ulong)priority, statusId),
            StatusId = statusId,
            TargetId = targetId,
            Duration = duration,
            IsActive = true,
            Definition = new StatusEffectDefinition
            {
                StatusId = statusId,
                DurationTickBoundary = StatusTriggerBoundary.EndActivation,
                Priority = priority,
                Triggers =
                [
                    new EffectTriggerDefinition
                    {
                        TriggerId = $"{statusId}.tick",
                        Boundary = "EndActivation",
                        Effects =
                        [
                            new EffectDefinition
                            {
                                EffectId = $"{statusId}.damage",
                                Type = EffectType.DAMAGE,
                                Target = EffectTarget.SELF,
                                FlatValue = formula == null ? 1 : null,
                                FormulaValue = formula,
                                TargetResource = "health"
                            }
                        ]
                    }
                ]
            },
            Stacks = 2
        };

    private static CombatEntity Entity(string id, bool hero, float health) => new()
    {
        EntityId = id,
        Name = id,
        IsHero = hero,
        ResourceState = new EntityResourceState
        {
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new()
                {
                    ResourceId = "health",
                    Current = health,
                    Maximum = health,
                    Minimum = 0,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = "health",
                        DisplayName = "Health",
                        Category = ResourceCategory.VITAL,
                        DefaultMax = health
                    }
                }
            }
        }
    };
}
