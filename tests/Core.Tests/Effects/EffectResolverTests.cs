using Core.Combat.Models;
using Core.Common;
using Core.Damage;
using Core.Determinism;
using Core.Effects;
using Core.Events;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public class EffectResolverTests
{
    private readonly Mock<IDamageCalculator> _damageCalculator = new();
    private readonly Mock<IResourceManager> _resourceManager = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<ILogger> _logger = new();
    private readonly Mock<IRuntimeFormulaEvaluator> _formulaEvaluator = new();
    private readonly Mock<IStatusEffectManager> _statusEffectManager = new();
    private readonly Mock<IRunManager> _runManager = new();

    [Fact]
    public void EffectResult_DefensivelyCopiesOutputCollections()
    {
        var entities = new List<string> { "enemy" };
        var metadata = new Dictionary<string, object> { ["damage"] = 5 };
        var result = new EffectResult
        {
            Success = true,
            AffectedEntityIds = entities,
            Metadata = metadata
        };

        entities.Clear();
        metadata["damage"] = 99;

        Assert.Equal(new[] { "enemy" }, result.AffectedEntityIds);
        Assert.Equal(5, result.Metadata["damage"]);
    }

    [Fact]
    public void ApplyEffect_WithDeterministicProvider_ReproducesRandomTargetAndContext()
    {
        var state = CreateCombatState("hero", "enemy") with
        {
            Determinism = DeterministicContext.Create(123UL, "test-content")
        };
        _damageCalculator
            .Setup(m => m.CalculateDamage(
                It.IsAny<ActionDefinition>(),
                It.IsAny<CombatEntity>(),
                It.IsAny<CombatEntity>(),
                It.IsAny<IRandomProvider>()))
            .Returns(new DamageResult { FinalDamage = 5f });
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            Definition = new EffectDefinition
            {
                EffectId = "random-hit",
                Type = EffectType.DAMAGE,
                Target = EffectTarget.RANDOM_ENEMY,
                FlatValue = 5f
            },
            SourceEntityId = "hero",
            TargetEntityId = "enemy"
        };
        var context = CombatEffectContext.FromEffect(effect, state);
        var firstProvider = new DeterministicRandomProvider(state.Determinism);
        var secondProvider = new DeterministicRandomProvider(state.Determinism);

        var first = resolver.ApplyEffect(effect, context, firstProvider);
        var second = resolver.ApplyEffect(effect, context, secondProvider);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.EffectResult.ValueApplied, second.Value.EffectResult.ValueApplied);
        Assert.Equal(
            first.Value.EffectResult.AffectedEntityIds,
            second.Value.EffectResult.AffectedEntityIds);
        Assert.Equal(firstProvider.Context, secondProvider.Context);
        Assert.Equal(1UL, firstProvider.Context.IdSequence);
        Assert.Equal(1UL, firstProvider.Context.RandomState.DrawCount);
    }

    [Fact]
    public void ResolveEffect_DamageWithFormula_UsesCalculatedValue()
    {
        var sourceId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        var state = CreateCombatState(sourceId, targetId, targetHealth: 40);
        _damageCalculator
            .Setup(m => m.CalculateDamage(It.IsAny<ActionDefinition>(), state.Hero, state.Enemies[0]))
            .Returns<ActionDefinition, CombatEntity, CombatEntity>((action, _, _) => new DamageResult
            {
                FinalDamage = action.Effects.Single().FlatValue!.Value
            });
        _formulaEvaluator
            .Setup(m => m.Evaluate("target_max_hp - target_hp", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Result<float>.Success(60f));
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Definition = new EffectDefinition
            {
                EffectId = "execute",
                Type = EffectType.DAMAGE,
                FormulaValue = "target_max_hp - target_hp",
                TargetResource = "health"
            }
        };

        var result = resolver.ResolveEffect(effect, state);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value!.Success);
        Assert.Equal(60f, result.Value.ValueApplied);
        _damageCalculator.Verify(m => m.CalculateDamage(
            It.Is<ActionDefinition>(a => a.Effects.Single().FlatValue == 60f),
            state.Hero,
            state.Enemies[0]), Times.Once);
        _formulaEvaluator.Verify(m => m.Evaluate(
            "target_max_hp - target_hp",
            It.Is<Dictionary<string, float>>(vars => vars["target_hp"] == 40f && vars["target_max_hp"] == 100f),
            0f), Times.Once);
    }

    [Fact]
    public void ResolveEffect_DamageFormulaFailure_FallsBackToFlatValue()
    {
        var sourceId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        var state = CreateCombatState(sourceId, targetId, targetHealth: 40);
        _formulaEvaluator
            .Setup(m => m.Evaluate("bad_formula", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Result<float>.Failure("bad formula"));
        _damageCalculator
            .Setup(m => m.CalculateDamage(It.IsAny<ActionDefinition>(), state.Hero, state.Enemies[0]))
            .Returns<ActionDefinition, CombatEntity, CombatEntity>((action, _, _) => new DamageResult
            {
                FinalDamage = action.Effects.Single().FlatValue!.Value
            });
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Definition = new EffectDefinition
            {
                Type = EffectType.DAMAGE,
                FormulaValue = "bad_formula",
                FlatValue = 7,
                TargetResource = "health"
            }
        };

        var result = resolver.ResolveEffect(effect, state);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(7f, result.Value!.ValueApplied);
        _damageCalculator.Verify(m => m.CalculateDamage(
            It.Is<ActionDefinition>(a => a.Effects.Single().FlatValue == 7f),
            state.Hero,
            state.Enemies[0]), Times.Once);
    }

    [Fact]
    public void ApplyEffect_WithFalseCondition_DoesNotExecuteEffect()
    {
        var sourceId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        var state = CreateCombatState(sourceId, targetId, targetHealth: 40);
        _formulaEvaluator
            .Setup(m => m.Evaluate("target_hp - 50", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Result<float>.Success(-10f));
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Definition = new EffectDefinition
            {
                Type = EffectType.HEAL,
                Condition = "target_hp - 50",
                FlatValue = 10,
                TargetResource = "health"
            }
        };

        var result = resolver.ApplyEffect(effect, CombatEffectContext.FromEffect(effect, state));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.False(result.Value!.EffectResult.Success);
        Assert.Equal("Condition not met", result.Value.EffectResult.ErrorMessage);
    }

    [Fact]
    public void ApplyEffect_WithTrueCondition_ExecutesEffect()
    {
        var sourceId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        var state = CreateCombatState(sourceId, targetId, targetHealth: 40);
        _formulaEvaluator
            .Setup(m => m.Evaluate("50 - target_hp", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Result<float>.Success(10f));
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Definition = new EffectDefinition
            {
                Type = EffectType.HEAL,
                Condition = "50 - target_hp",
                FlatValue = 10,
                TargetResource = "health"
            }
        };

        var result = resolver.ApplyEffect(effect, CombatEffectContext.FromEffect(effect, state));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value!.EffectResult.Success);
        Assert.Equal(10f, result.Value.EffectResult.ValueApplied);
        Assert.Equal(new[] { targetId }, result.Value.EffectResult.AffectedEntityIds);
    }

    [Fact]
    public void ResolveEffect_ApplyStatus_WithStatusManager_AppliesStatus()
    {
        const string sourceId = "hero_1";
        const string targetId = "enemy_1";
        var state = CreateCombatState(sourceId, targetId);
        _statusEffectManager
            .Setup(m => m.ApplyStatus(targetId, "burning", 2, 3, sourceId))
            .Returns(Result<StatusEffectInstance>.Success(new StatusEffectInstance { StatusId = "burning" }));
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Definition = new EffectDefinition
            {
                Type = EffectType.APPLY_STATUS,
                StatusId = "burning",
                StatusStacks = 2,
                StatusDuration = 3
            }
        };

        var result = resolver.ResolveEffect(effect, state);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value!.Success);
        Assert.Equal(new[] { "burning" }, result.Value.StatusApplied);
        _statusEffectManager.Verify(m => m.ApplyStatus(targetId, "burning", 2, 3, sourceId), Times.Once);
    }

    [Fact]
    public void ApplyEffect_ApplyStatus_UsesDeterministicIdentityAndLogicalTime()
    {
        const string sourceId = "hero_1";
        const string targetId = "enemy_1";
        var state = CreateCombatState(sourceId, targetId);
        var initial = DeterministicContext.Create(77UL, "test-content").AdvanceStep();
        var effectAllocation = initial.AllocateId("effect");
        var statusAllocation = effectAllocation.Context.AllocateId("status-effect");
        var provider = new DeterministicRandomProvider(initial);
        _statusEffectManager
            .Setup(manager => manager.ApplyStatus(
                targetId,
                "burning",
                statusAllocation.Value,
                statusAllocation.Context.LogicalTimestamp.UtcDateTime,
                2,
                3,
                sourceId))
            .Returns(Result<StatusEffectInstance>.Success(new StatusEffectInstance
            {
                InstanceId = statusAllocation.Value,
                AppliedAt = statusAllocation.Context.LogicalTimestamp.UtcDateTime,
                StatusId = "burning"
            }));
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Definition = new EffectDefinition
            {
                Type = EffectType.APPLY_STATUS,
                StatusId = "burning",
                StatusStacks = 2,
                StatusDuration = 3
            }
        };

        var result = resolver.ApplyEffect(
            effect,
            CombatEffectContext.FromEffect(effect, state),
            provider);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(statusAllocation.Context, provider.Context);
        _statusEffectManager.VerifyAll();
    }

    [Fact]
    public void ResolveEffect_RemoveStatus_WithStatusManager_RemovesStatus()
    {
        var sourceId = Guid.NewGuid().ToString();
        const string targetId = "enemy_1";
        var state = CreateCombatState(sourceId, targetId);
        _statusEffectManager
            .Setup(m => m.RemoveStatusByStatusId(targetId, "burning"))
            .Returns(Result.Success());
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Definition = new EffectDefinition
            {
                Type = EffectType.REMOVE_STATUS,
                StatusId = "burning"
            }
        };

        var result = resolver.ResolveEffect(effect, state);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value!.Success);
        Assert.Equal(new[] { "burning" }, result.Value.StatusRemoved);
        _statusEffectManager.Verify(m => m.RemoveStatusByStatusId(targetId, "burning"), Times.Once);
    }

    [Fact]
    public void ApplyEffect_RunResourceEffect_DoesNotRequireCombatState()
    {
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = "reward-node",
            TargetEntityId = "player",
            Definition = new EffectDefinition
            {
                Type = EffectType.MODIFY_RESOURCE,
                TargetResource = "credits",
                Operation = ResourceEffectOperation.ADD,
                FlatValue = 25
            }
        };
        var context = new RunEffectContext
        {
            RunId = "run-1",
            SourceEntityId = "reward-node",
            TargetEntityId = "player"
        };

        var result = resolver.ApplyEffect(effect, context);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value!.Success);
        Assert.Equal(EffectScope.RUN, result.Value.Scope);
        Assert.Equal(25f, result.Value.EffectResult.ValueApplied);
        Assert.Equal("credits", result.Value.EffectResult.ResourceAffected);
        Assert.Null(result.Value.UpdatedCombatState);
    }

    [Fact]
    public void ApplyEffect_RunDamageEffect_FailsWithoutCombatState()
    {
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = "reward-node",
            TargetEntityId = "player",
            Definition = new EffectDefinition
            {
                Type = EffectType.DAMAGE,
                FlatValue = 10
            }
        };
        var context = new RunEffectContext
        {
            RunId = "run-1",
            SourceEntityId = "reward-node",
            TargetEntityId = "player"
        };

        var result = resolver.ApplyEffect(effect, context);

        Assert.True(result.IsFailure);
        Assert.Contains("requires combat context", result.Error);
    }

    [Fact]
    public void ApplyEffect_RunResourceSupportsAnyConfiguredResource()
    {
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            InstanceId = Guid.NewGuid().ToString(),
            Definition = new EffectDefinition
            {
                EffectId = "insight1",
                Type = EffectType.MODIFY_RESOURCE,
                TargetResource = "insight",
                Operation = ResourceEffectOperation.ADD,
                Target = EffectTarget.SELF,
                FlatValue = 5
            },
            SourceEntityId = "player",
            TargetEntityId = "player"
        };

        var context = new RunEffectContext
        {
            RunId = "run-1",
            SourceEntityId = "player",
            TargetEntityId = "player"
        };

        var result = resolver.ApplyEffect(effect, context);

        Assert.True(result.IsSuccess);
        Assert.Equal(EffectScope.RUN, result.Value!.Scope);
        Assert.Equal(5f, result.Value.EffectResult.ValueApplied);
        Assert.Equal("insight", result.Value.EffectResult.ResourceAffected);
    }

    [Fact]
    public void ApplyEffect_DrawCard_ReturnsMetadataWithoutState()
    {
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            InstanceId = Guid.NewGuid().ToString(),
            Definition = new EffectDefinition
            {
                EffectId = "draw1",
                Type = EffectType.DRAW_CARD,
                Target = EffectTarget.SELF,
                FlatValue = 2
            },
            SourceEntityId = "player",
            TargetEntityId = "player"
        };

        var context = new RunEffectContext
        {
            RunId = "run-1",
            SourceEntityId = "player",
            TargetEntityId = "player"
        };

        var result = resolver.ApplyEffect(effect, context);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.UpdatedCombatState);
        Assert.Equal(2f, result.Value.EffectResult.ValueApplied);
        Assert.Equal("DRAW_CARD", result.Value.EffectResult.Metadata["deckOperation"]);
        Assert.Equal(false, result.Value.EffectResult.Metadata["stateApplied"]);
    }

    [Fact]
    public void ApplyEffect_RunResourceEffect_WithRunState_AppliesResourceState()
    {
        var runState = new RunState
        {
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "gold", Amount = 10 })
        };
        _runManager
            .Setup(m => m.ApplyRunResource(
                runState.RunId,
                "gold",
                25,
                ResourceEffectOperation.ADD,
                ResourceValueField.Current))
            .Returns(Result<RunState>.Success(runState with
            {
                ResourceState = TestDataBuilders.RunResources(
                    new ResourceAmount { ResourceId = "gold", Amount = 35 })
            }));

        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = "reward-node",
            TargetEntityId = "player",
            Definition = new EffectDefinition
            {
                Type = EffectType.MODIFY_RESOURCE,
                TargetResource = "gold",
                Operation = ResourceEffectOperation.ADD,
                FlatValue = 25
            }
        };
        var context = new RunEffectContext
        {
            RunId = runState.RunId.ToString(),
            RunState = runState,
            SourceEntityId = "reward-node",
            TargetEntityId = "player"
        };

        var result = resolver.ApplyEffect(effect, context);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True((bool)result.Value!.EffectResult.Metadata["stateApplied"]);
        var resources = Assert.IsAssignableFrom<IReadOnlyDictionary<string, float>>(
            result.Value.EffectResult.Metadata["resources"]);
        Assert.Equal(35f, resources["gold"]);
        _runManager.Verify(m => m.ApplyRunResource(
            runState.RunId,
            "gold",
            25,
            ResourceEffectOperation.ADD,
            ResourceValueField.Current), Times.Once);
    }

    [Fact]
    public void ApplyEffect_DrawCard_WithRunState_AppliesDeckState()
    {
        var runState = new RunState
        {
            Deck = new DeckState
            {
                DrawPile = new List<string> { "a", "b", "c" }
            }
        };
        var updatedState = runState with
        {
            Deck = new DeckState
            {
                DrawPile = new List<string> { "c" },
                Hand = new List<string> { "a", "b" }
            }
        };
        _runManager.Setup(m => m.DrawCards(runState.RunId, 2)).Returns(Result<IReadOnlyList<string>>.Success(new[] { "a", "b" }));
        _runManager.Setup(m => m.GetRun(runState.RunId)).Returns(Result<RunState>.Success(updatedState));

        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            InstanceId = Guid.NewGuid().ToString(),
            Definition = new EffectDefinition
            {
                EffectId = "draw1",
                Type = EffectType.DRAW_CARD,
                Target = EffectTarget.SELF,
                FlatValue = 2
            },
            SourceEntityId = "player",
            TargetEntityId = "player"
        };

        var context = new RunEffectContext
        {
            RunId = runState.RunId.ToString(),
            RunState = runState,
            SourceEntityId = "player",
            TargetEntityId = "player"
        };

        var result = resolver.ApplyEffect(effect, context);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True((bool)result.Value!.EffectResult.Metadata["stateApplied"]);
        Assert.Equal(2, result.Value.EffectResult.Metadata["handCount"]);
        Assert.Equal(new[] { "a", "b" }, (IReadOnlyList<string>)result.Value.EffectResult.Metadata["cards"]);
        _runManager.Verify(m => m.DrawCards(runState.RunId, 2), Times.Once);
    }

    [Fact]
    public void ApplyEffect_DispelStatus_RemovesAllStatus()
    {
        const string targetId = "enemy_1";
        _statusEffectManager
            .Setup(m => m.RemoveAllStatus(targetId, null))
            .Returns(Result.Success());

        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            InstanceId = Guid.NewGuid().ToString(),
            Definition = new EffectDefinition
            {
                EffectId = "dispel1",
                Type = EffectType.DISPEL_STATUS,
                Target = EffectTarget.TARGET
            },
            SourceEntityId = Guid.NewGuid().ToString(),
            TargetEntityId = targetId
        };

        var state = CreateCombatState(effect.SourceEntityId, effect.TargetEntityId);
        var context = CombatEffectContext.FromEffect(effect, state);

        var result = resolver.ApplyEffect(effect, context);

        Assert.True(result.IsSuccess);
        Assert.Contains("*", result.Value!.EffectResult.StatusRemoved);
        _statusEffectManager.Verify(m => m.RemoveAllStatus(targetId, null), Times.Once);
    }

    [Fact]
    public void ApplyEffect_ModifierEffect_ReturnsKeyAndValue()
    {
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            InstanceId = Guid.NewGuid().ToString(),
            Definition = new EffectDefinition
            {
                EffectId = "mod1",
                Type = EffectType.MODIFY_DAMAGE_DEALT,
                Target = EffectTarget.SELF,
                FlatValue = 0.25f
            },
            SourceEntityId = "player",
            TargetEntityId = "player"
        };

        var context = new RunEffectContext
        {
            RunId = "run-1",
            SourceEntityId = "player",
            TargetEntityId = "player"
        };

        var result = resolver.ApplyEffect(effect, context);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.25f, result.Value!.EffectResult.ValueApplied);
        Assert.Equal("damage_dealt", result.Value.EffectResult.ResourceAffected);
        Assert.Equal("damage_dealt", result.Value.EffectResult.Metadata["modifierKey"]);
    }

    [Fact]
    public void ApplyEffect_ControlEffect_FailsWithoutCombatContext()
    {
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            InstanceId = Guid.NewGuid().ToString(),
            Definition = new EffectDefinition
            {
                EffectId = "ctrl1",
                Type = EffectType.SKIP_TURN,
                Target = EffectTarget.TARGET
            },
            SourceEntityId = "player",
            TargetEntityId = "enemy"
        };

        var context = new RunEffectContext
        {
            RunId = "run-1",
            SourceEntityId = "player",
            TargetEntityId = "enemy"
        };

        var result = resolver.ApplyEffect(effect, context);

        Assert.True(result.IsFailure);
        Assert.Contains("requires combat context", result.Error);
    }

    private EffectResolver CreateResolver()
    {
        return new EffectResolver(
            _damageCalculator.Object,
            _resourceManager.Object,
            _eventBus.Object,
            _logger.Object,
            _formulaEvaluator.Object,
            statusEffectManager: _statusEffectManager.Object,
            runManager: _runManager.Object);
    }

    private static CombatState CreateCombatState(string sourceId, string targetId, float targetHealth = 100)
    {
        return new CombatState
        {
            Hero = CreateEntity(sourceId, isHero: true, health: 100),
            Enemies = new[] { CreateEntity(targetId, isHero: false, health: targetHealth) }
        };
    }

    private static CombatEntity CreateEntity(string entityId, bool isHero, float health)
    {
        var healthDefinition = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultMin = 0,
            DefaultMax = 100,
            DefaultCurrent = 100
        };

        return new CombatEntity
        {
            EntityId = entityId,
            Name = entityId,
            IsHero = isHero,
            ResourceState = new ResourceSet
            {
                OwnerId = entityId,
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = new ResourcePool
                    {
                        ResourceId = "health",
                        Current = health,
                        Minimum = 0,
                        Maximum = 100,
                        Definition = healthDefinition
                    }
                }
            }
        };
    }
}
