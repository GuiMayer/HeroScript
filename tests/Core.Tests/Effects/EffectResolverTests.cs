using Core.Combat.Models;
using Core.Common;
using Core.Damage;
using Core.Effects;
using Core.Events;
using Core.Logging;
using Core.Resources;
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
    private readonly Mock<IStatusEffectManager> _statusEffectManager = new();

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
    }

    [Fact]
    public void ResolveEffect_ApplyStatus_WithStatusManager_AppliesStatus()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var state = CreateCombatState(sourceId.ToString(), targetId.ToString());
        _statusEffectManager
            .Setup(m => m.ApplyStatus(targetId, "burning", 2, 3, sourceId))
            .Returns(Result<StatusEffectInstance>.Success(new StatusEffectInstance { StatusId = "burning" }));
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = sourceId.ToString(),
            TargetEntityId = targetId.ToString(),
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
    public void ResolveEffect_RemoveStatus_WithStatusManager_RemovesStatus()
    {
        var sourceId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid();
        var state = CreateCombatState(sourceId, targetId.ToString());
        _statusEffectManager
            .Setup(m => m.RemoveStatusByStatusId(targetId, "burning"))
            .Returns(Result.Success());
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = sourceId,
            TargetEntityId = targetId.ToString(),
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
    public void ApplyEffect_RunGoldEffect_DoesNotRequireCombatState()
    {
        var resolver = CreateResolver();
        var effect = new EffectInstance
        {
            SourceEntityId = "reward-node",
            TargetEntityId = "player",
            Definition = new EffectDefinition
            {
                Type = EffectType.GAIN_GOLD,
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
        Assert.Equal("gold", result.Value.EffectResult.ResourceAffected);
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

    private EffectResolver CreateResolver()
    {
        return new EffectResolver(
            _damageCalculator.Object,
            _resourceManager.Object,
            _eventBus.Object,
            _logger.Object,
            statusEffectManager: _statusEffectManager.Object);
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
            ResourceState = new EntityResourceState
            {
                EntityId = entityId,
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
