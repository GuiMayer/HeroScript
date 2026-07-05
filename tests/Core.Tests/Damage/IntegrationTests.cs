using System.Collections.Generic;
using Core.Combat.Models;
using Core.Damage;
using Core.Damage.Events;
using Core.Effects;
using Core.Events;
using Core.Logging;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

/// <summary>
/// Testes de integração end-to-end do sistema de dano
/// </summary>
[Trait("Category", "Integration")]
public class IntegrationTests
{
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<ILogger> _mockLogger;

    public IntegrationTests()
    {
        _mockEventBus = new Mock<IEventBus>();
        _mockLogger = DamageTestHelpers.CreateMockLogger();
    }

    // ==================== COMPLETE DAMAGE FLOW ====================

    [Fact]
    public void DamageFlow_WithBasicAttack_ProcessesCorrectly()
    {
        // Arrange
        var pipelineManager = CreateSimplePipelineManager();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("basic_attack", 50f, new List<string> { "physical" });
        var attacker = CreateWarrior("hero");
        var target = CreateGoblin("enemy");

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.InRange(result.FinalDamage, 40f, 60f); // After armor reduction
        _mockEventBus.Verify(e => e.Publish(It.IsAny<DamageCalculatedEvent>()), Times.Once);
    }

    [Fact]
    public void DamageFlow_WithMultipleEffects_AccumulatesDamage()
    {
        // Arrange
        var pipelineManager = CreateSimplePipelineManager();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = new ActionDefinition
        {
            ActionId = "combo_attack",
            DisplayName = "Combo Attack",
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = 20f, Target = EffectTarget.TARGET },
                new() { Type = EffectType.DAMAGE, FlatValue = 15f, Target = EffectTarget.TARGET },
                new() { Type = EffectType.DAMAGE, FlatValue = 10f, Target = EffectTarget.TARGET }
            }
        };
        
        var attacker = CreateWarrior("hero");
        var target = CreateGoblin("enemy");

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.InRange(result.FinalDamage, 35f, 55f); // Total 45 damage after armor
    }

    [Fact]
    public void DamageFlow_WithArmorReduction_ReducesDamage()
    {
        // Arrange
        var pipelineManager = CreatePipelineWithArmorReduction();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("sword_strike", 100f);
        var attacker = CreateWarrior("hero");
        var target = CreateHeavyKnight("tank", armor: 50f);

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.True(result.FinalDamage < 100f); // Armor reduced damage
        Assert.True(result.FinalDamage > 0f);   // But not completely negated
    }

    [Fact]
    public void DamageFlow_WithCriticalHit_IncreasesDamage()
    {
        // Arrange
        var pipelineManager = CreatePipelineWithGuaranteedCrit();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("crit_strike", 50f);
        var attacker = CreateWarrior("hero", critChance: 100f, critMultiplier: 2.0f);
        var target = CreateGoblin("enemy");

        DamageCalculatedEvent? publishedEvent = null;
        _mockEventBus
            .Setup(e => e.Publish(It.IsAny<DamageCalculatedEvent>()))
            .Callback<IEvent>(evt => publishedEvent = evt as DamageCalculatedEvent);

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(publishedEvent);
        Assert.True(publishedEvent.CritTier > 0);
        Assert.True(result.FinalDamage > 50f); // Critical hit increases damage
    }

    // ==================== EDGE CASE FLOWS ====================

    [Fact]
    public void DamageFlow_WithZeroDamage_PublishesEventCorrectly()
    {
        // Arrange
        var pipelineManager = CreateSimplePipelineManager();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("weak_poke", 0f);
        var attacker = CreateWarrior("hero");
        var target = CreateGoblin("enemy");

        DamageCalculatedEvent? publishedEvent = null;
        _mockEventBus
            .Setup(e => e.Publish(It.IsAny<DamageCalculatedEvent>()))
            .Callback<IEvent>(evt => publishedEvent = evt as DamageCalculatedEvent);

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(0f, result.FinalDamage);
        Assert.NotNull(publishedEvent);
        Assert.Equal(0f, publishedEvent.FinalDamage);
    }

    [Fact]
    public void DamageFlow_WithNegativeDamage_ClampsToZero()
    {
        // Arrange
        var pipelineManager = CreatePipelineWithMassiveArmorReduction();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("tiny_hit", 5f);
        var attacker = CreateWarrior("hero");
        var target = CreateHeavyKnight("fortress", armor: 100f);

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(0f, result.FinalDamage);
    }

    [Fact]
    public void DamageFlow_WithMultipleTags_PassesThroughPipeline()
    {
        // Arrange
        var pipelineManager = CreateTagAwarePipelineManager();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage(
            "elemental_strike", 
            60f, 
            new List<string> { "fire", "magical", "aoe" });
        
        var attacker = CreateMage("mage");
        var target = CreateGoblin("enemy");

        DamageCalculatedEvent? publishedEvent = null;
        _mockEventBus
            .Setup(e => e.Publish(It.IsAny<DamageCalculatedEvent>()))
            .Callback<IEvent>(evt => publishedEvent = evt as DamageCalculatedEvent);

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(publishedEvent);
        Assert.Contains("fire", publishedEvent.Tags);
        Assert.Contains("magical", publishedEvent.Tags);
        Assert.Contains("aoe", publishedEvent.Tags);
    }

    // ==================== MULTIPLE ENTITY SCENARIOS ====================

    [Fact]
    public void DamageFlow_MultipleAttackers_ProducesIndependentResults()
    {
        // Arrange
        var pipelineManager = CreateSimplePipelineManager();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("attack", 30f);
        var attacker1 = CreateWarrior("warrior1");
        var attacker2 = CreateWarrior("warrior2");
        var target = CreateGoblin("enemy");

        // Act
        var result1 = calculator.CalculateDamage(action, attacker1, target);
        var result2 = calculator.CalculateDamage(action, attacker2, target);

        // Assert
        Assert.True(result1.FinalDamage > 0);
        Assert.True(result2.FinalDamage > 0);
        // Results should be similar since attackers are identical
        Assert.InRange(result2.FinalDamage, result1.FinalDamage - 5f, result1.FinalDamage + 5f);
    }

    [Fact]
    public void DamageFlow_MultipleTargets_ProducesIndependentResults()
    {
        // Arrange
        var pipelineManager = CreatePipelineWithArmorReduction();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("aoe_blast", 40f);
        var attacker = CreateMage("mage");
        var target1 = CreateGoblin("enemy1");
        var target2 = CreateHeavyKnight("enemy2", armor: 30f);

        // Act
        var result1 = calculator.CalculateDamage(action, attacker, target1);
        var result2 = calculator.CalculateDamage(action, attacker, target2);

        // Assert
        Assert.True(result1.FinalDamage > 0);
        Assert.True(result2.FinalDamage > 0);
        // Knight with armor should take less damage
        Assert.True(result2.FinalDamage < result1.FinalDamage);
    }

    // ==================== EVENT VERIFICATION ====================

    [Fact]
    public void DamageFlow_PublishesCompleteEventData()
    {
        // Arrange
        var pipelineManager = CreateSimplePipelineManager();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("test_action", 75f, new List<string> { "test" });
        var attacker = CreateWarrior("attacker_id");
        var target = CreateGoblin("target_id");

        DamageCalculatedEvent? publishedEvent = null;
        _mockEventBus
            .Setup(e => e.Publish(It.IsAny<DamageCalculatedEvent>()))
            .Callback<IEvent>(evt => publishedEvent = evt as DamageCalculatedEvent);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(publishedEvent);
        Assert.Equal("test_action", publishedEvent.ActionId);
        Assert.Equal("attacker_id", publishedEvent.AttackerId);
        Assert.Equal("target_id", publishedEvent.TargetId);
        Assert.Equal(75f, publishedEvent.BaseDamage);
        Assert.Contains("test", publishedEvent.Tags);
        Assert.NotNull(publishedEvent.Metadata);
    }

    [Fact]
    public void DamageFlow_MultipleDamageCalculations_PublishesMultipleEvents()
    {
        // Arrange
        var pipelineManager = CreateSimplePipelineManager();
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("attack", 30f);
        var attacker = CreateWarrior("hero");
        var target = CreateGoblin("enemy");

        var eventCount = 0;
        _mockEventBus
            .Setup(e => e.Publish(It.IsAny<DamageCalculatedEvent>()))
            .Callback<IEvent>(evt => eventCount++);

        // Act
        calculator.CalculateDamage(action, attacker, target);
        calculator.CalculateDamage(action, attacker, target);
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(3, eventCount);
    }

    // ==================== PIPELINE INTEGRATION ====================

    [Fact]
    public void DamageFlow_PipelineModifiesContext_ReflectsInResult()
    {
        // Arrange
        var pipelineManager = CreatePipelineWithFixedOutput(123.45f);
        var calculator = new DamageCalculator(pipelineManager, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("any_attack", 999f);
        var attacker = CreateWarrior("hero");
        var target = CreateGoblin("enemy");

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(123.45f, result.FinalDamage); // Pipeline forced this value
    }

    [Fact]
    public void DamageFlow_PipelineThrowsException_IsHandledGracefully()
    {
        // Arrange
        var mockPipelineManager = new Mock<IPipelineManager>();
        mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Throws(new System.InvalidOperationException("Pipeline error"));

        var calculator = new DamageCalculator(mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        
        var action = DamageTestHelpers.CreateActionWithDamage("attack", 50f);
        var attacker = CreateWarrior("hero");
        var target = CreateGoblin("enemy");

        // Act & Assert
        Assert.Throws<System.InvalidOperationException>(() => 
            calculator.CalculateDamage(action, attacker, target));
    }

    // ==================== HELPER METHODS ====================

    private static IPipelineManager CreateSimplePipelineManager()
    {
        var mock = new Mock<IPipelineManager>();
        mock.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx with 
            { 
                CurrentDamage = ctx.BaseDamage * 0.9f // Simple 10% reduction
            });
        return mock.Object;
    }

    private static IPipelineManager CreatePipelineWithArmorReduction()
    {
        var mock = new Mock<IPipelineManager>();
        mock.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) =>
            {
                var armor = ctx.Modifiers.GetValueOrDefault("target_armor", 0f);
                var reduction = armor * 0.5f; // 50% of armor value
                return ctx with { CurrentDamage = System.Math.Max(0f, ctx.BaseDamage - reduction) };
            });
        return mock.Object;
    }

    private static IPipelineManager CreatePipelineWithGuaranteedCrit()
    {
        var mock = new Mock<IPipelineManager>();
        mock.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) =>
            {
                var critMult = ctx.Modifiers.GetValueOrDefault("crit_multiplier", 1.5f);
                var newMetadata = new Dictionary<string, object>(ctx.Metadata) { ["crit_tier"] = 1 };
                return ctx with 
                { 
                    CurrentDamage = ctx.BaseDamage * critMult,
                    Metadata = newMetadata
                };
            });
        return mock.Object;
    }

    private static IPipelineManager CreatePipelineWithMassiveArmorReduction()
    {
        var mock = new Mock<IPipelineManager>();
        mock.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx with { CurrentDamage = -50f });
        return mock.Object;
    }

    private static IPipelineManager CreateTagAwarePipelineManager()
    {
        var mock = new Mock<IPipelineManager>();
        mock.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) =>
            {
                var bonus = ctx.Tags.Contains("fire") ? 1.2f : 1.0f;
                return ctx with { CurrentDamage = ctx.BaseDamage * bonus };
            });
        return mock.Object;
    }

    private static IPipelineManager CreatePipelineWithFixedOutput(float fixedValue)
    {
        var mock = new Mock<IPipelineManager>();
        mock.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx with { CurrentDamage = fixedValue });
        return mock.Object;
    }

    private static CombatEntity CreateWarrior(string id, float critChance = 10f, float critMultiplier = 1.5f)
    {
        return CreateEntity(id, isHero: true, armor: 5f, critChance: critChance, critMultiplier: critMultiplier);
    }

    private static CombatEntity CreateMage(string id)
    {
        return CreateEntity(id, isHero: true, armor: 0f, critChance: 15f, critMultiplier: 2.0f);
    }

    private static CombatEntity CreateGoblin(string id)
    {
        return CreateEntity(id, isHero: false, armor: 2f, critChance: 5f, critMultiplier: 1.5f);
    }

    private static CombatEntity CreateHeavyKnight(string id, float armor)
    {
        return CreateEntity(id, isHero: false, armor: armor, critChance: 0f, critMultiplier: 1.0f);
    }

    private static CombatEntity CreateEntity(
        string id, 
        bool isHero, 
        float armor = 0f, 
        float critChance = 5f, 
        float critMultiplier = 1.5f)
    {
        var resources = new Dictionary<string, ResourcePool>
        {
            ["health"] = new()
            {
                ResourceId = "health",
                Current = 100f,
                Maximum = 100f,
                Minimum = 0f,
                Definition = new ResourceDefinition
                {
                    ResourceId = "health",
                    DisplayName = "Health",
                    Category = ResourceCategory.VITAL
                }
            },
            ["armor"] = new()
            {
                ResourceId = "armor",
                Current = armor,
                Maximum = 999f,
                Minimum = 0f,
                Definition = new ResourceDefinition
                {
                    ResourceId = "armor",
                    DisplayName = "Armor",
                    Category = ResourceCategory.TEMPORARY
                }
            },
            ["crit_chance"] = new()
            {
                ResourceId = "crit_chance",
                Current = critChance,
                Maximum = 100f,
                Minimum = 0f,
                Definition = new ResourceDefinition
                {
                    ResourceId = "crit_chance",
                    DisplayName = "Crit Chance",
                    Category = ResourceCategory.TEMPORARY
                }
            },
            ["crit_multiplier"] = new()
            {
                ResourceId = "crit_multiplier",
                Current = critMultiplier,
                Maximum = 10f,
                Minimum = 1f,
                Definition = new ResourceDefinition
                {
                    ResourceId = "crit_multiplier",
                    DisplayName = "Crit Multiplier",
                    Category = ResourceCategory.TEMPORARY
                }
            }
        };

        return new CombatEntity
        {
            EntityId = id,
            Name = id,
            IsHero = isHero,
            ResourceState = new EntityResourceState
            {
                EntityId = id,
                Resources = resources
            }
        };
    }
}
