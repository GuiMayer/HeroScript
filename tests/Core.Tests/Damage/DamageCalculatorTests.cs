using System;
using System.Collections.Generic;
using Core.Combat.Models;
using Core.Damage;
using Core.Damage.Events;
using Core.Effects;
using Core.Events;
using Core.Logging;
using Core.Resources;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

[Trait("Category", "Unit")]
public class DamageCalculatorTests
{
    private readonly Mock<IPipelineManager> _mockPipelineManager;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IStatusEffectManager> _mockStatusEffectManager;

    public DamageCalculatorTests()
    {
        _mockPipelineManager = new Mock<IPipelineManager>();
        _mockEventBus = new Mock<IEventBus>();
        _mockLogger = DamageTestHelpers.CreateMockLogger();
        _mockStatusEffectManager = new Mock<IStatusEffectManager>();
    }

    // ==================== CONSTRUCTOR VALIDATION ====================

    [Fact]
    public void Constructor_WithNullPipelineManager_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DamageCalculator(null!, _mockEventBus.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullEventBus_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DamageCalculator(_mockPipelineManager.Object, null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, null!));
    }

    [Fact]
    public void Constructor_WithNullStatusEffectManager_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object, null));

        Assert.Null(exception);
    }

    // ==================== BASIC DAMAGE CALCULATION ====================

    [Fact]
    public void CalculateDamage_WithBasicAction_ReturnsExpectedDamage()
    {
        // Arrange
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = DamageTestHelpers.CreateActionWithDamage("basic_attack", 50f);
        var attacker = CreateEntity("attacker", isHero: true);
        var target = CreateEntity("target", isHero: false);

        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx with { CurrentDamage = 50f });

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(50f, result.FinalDamage);
        Assert.Equal(0, result.CritTier);
    }

    [Fact]
    public void CalculateDamage_WithMultipleDamageEffects_SumsBaseDamage()
    {
        // Arrange
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = new ActionDefinition
        {
            ActionId = "multi_hit",
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = 20f, Target = EffectTarget.TARGET },
                new() { Type = EffectType.DAMAGE, FlatValue = 30f, Target = EffectTarget.TARGET },
                new() { Type = EffectType.DAMAGE, FlatValue = 10f, Target = EffectTarget.TARGET }
            }
        };
        var attacker = CreateEntity("attacker", isHero: true);
        var target = CreateEntity("target", isHero: false);

        DamageContext? capturedContext = null;
        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Callback<DamageContext>(ctx => capturedContext = ctx)
            .Returns((DamageContext ctx) => ctx);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(60f, capturedContext.BaseDamage);
    }

    [Fact]
    public void CalculateDamage_WithNonDamageEffects_IgnoresThemInBaseDamage()
    {
        // Arrange
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = new ActionDefinition
        {
            ActionId = "complex_action",
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = 25f, Target = EffectTarget.TARGET },
                new() { Type = EffectType.HEAL, FlatValue = 15f, Target = EffectTarget.SELF },
                new() { Type = EffectType.APPLY_STATUS, Target = EffectTarget.TARGET }
            }
        };
        var attacker = CreateEntity("attacker", isHero: true);
        var target = CreateEntity("target", isHero: false);

        DamageContext? capturedContext = null;
        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Callback<DamageContext>(ctx => capturedContext = ctx)
            .Returns((DamageContext ctx) => ctx);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(25f, capturedContext.BaseDamage);
    }

    // ==================== NEGATIVE DAMAGE HANDLING ====================

    [Fact]
    public void CalculateDamage_WithNegativePipelineResult_ClampsFinalDamageToZero()
    {
        // Arrange
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = DamageTestHelpers.CreateActionWithDamage("weak_attack", 10f);
        var attacker = CreateEntity("attacker", isHero: true);
        var target = CreateEntity("target", isHero: false, armor: 50f);

        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx with { CurrentDamage = -15f });

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(0f, result.FinalDamage);
    }

    [Fact]
    public void CalculateDamage_WithZeroDamage_ReturnsZero()
    {
        // Arrange
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = DamageTestHelpers.CreateActionWithDamage("no_damage", 0f);
        var attacker = CreateEntity("attacker", isHero: true);
        var target = CreateEntity("target", isHero: false);

        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx);

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(0f, result.FinalDamage);
    }

    // ==================== CRITICAL HIT HANDLING ====================

    [Fact]
    public void CalculateDamage_WithCritTierInMetadata_ExtractsCritTierCorrectly()
    {
        // Arrange
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = DamageTestHelpers.CreateActionWithDamage("crit_attack", 40f);
        var attacker = CreateEntity("attacker", isHero: true);
        var target = CreateEntity("target", isHero: false);

        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx with
            {
                CurrentDamage = 80f,
                Metadata = new Dictionary<string, object>(ctx.Metadata)
                {
                    ["crit_tier"] = 2
                }
            });

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(80f, result.FinalDamage);
        Assert.Equal(2, result.CritTier);
    }

    [Fact]
    public void CalculateDamage_WithoutCritTierInMetadata_ReturnsCritTierZero()
    {
        // Arrange
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = DamageTestHelpers.CreateActionWithDamage("normal_attack", 30f);
        var attacker = CreateEntity("attacker", isHero: true);
        var target = CreateEntity("target", isHero: false);

        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx with { CurrentDamage = 30f });

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(0, result.CritTier);
    }

    // ==================== EVENT PUBLISHING ====================

    [Fact]
    public void CalculateDamage_PublishesDamageCalculatedEvent()
    {
        // Arrange
        DamageCalculatedEvent? publishedEvent = null;
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = DamageTestHelpers.CreateActionWithDamage("test_action", 45f, new List<string> { "physical", "melee" });
        var attacker = CreateEntity("hero", isHero: true);
        var target = CreateEntity("enemy", isHero: false);

        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx with { CurrentDamage = 55f });

        _mockEventBus
            .Setup(e => e.Publish(It.IsAny<DamageCalculatedEvent>()))
            .Callback<IEvent>(evt => publishedEvent = Assert.IsType<DamageCalculatedEvent>(evt));

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(publishedEvent);
        Assert.Equal("test_action", publishedEvent.ActionId);
        Assert.Equal("hero", publishedEvent.AttackerId);
        Assert.Equal("enemy", publishedEvent.TargetId);
        Assert.Equal(45f, publishedEvent.BaseDamage);
        Assert.Equal(55f, publishedEvent.FinalDamage);
        Assert.Contains("physical", publishedEvent.Tags);
        Assert.Contains("melee", publishedEvent.Tags);
    }

    [Fact]
    public void CalculateDamage_EventContainsMetadata()
    {
        // Arrange
        DamageCalculatedEvent? publishedEvent = null;
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = DamageTestHelpers.CreateActionWithDamage("spell", 100f);
        var attacker = CreateEntity("mage", isHero: true);
        var target = CreateEntity("boss", isHero: false);

        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx with
            {
                CurrentDamage = 150f,
                Metadata = new Dictionary<string, object>(ctx.Metadata)
                {
                    ["damage_type"] = "elemental",
                    ["source_skill"] = "fireball"
                }
            });

        _mockEventBus
            .Setup(e => e.Publish(It.IsAny<DamageCalculatedEvent>()))
            .Callback<IEvent>(evt => publishedEvent = Assert.IsType<DamageCalculatedEvent>(evt));

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(publishedEvent);
        Assert.Equal("elemental", publishedEvent.Metadata["damage_type"]);
        Assert.Equal("fireball", publishedEvent.Metadata["source_skill"]);
    }

    // ==================== CONTEXT BUILDING ====================

    [Fact]
    public void CalculateDamage_BuildsContextWithCorrectInitialValues()
    {
        // Arrange
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = DamageTestHelpers.CreateActionWithDamage("sword_slash", 35f, new List<string> { "physical" });
        var attacker = CreateEntity("warrior", isHero: true, critChance: 25f, critMultiplier: 1.5f);
        var target = CreateEntity("goblin", isHero: false, armor: 10f);

        DamageContext? capturedContext = null;
        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Callback<DamageContext>(ctx => capturedContext = ctx)
            .Returns((DamageContext ctx) => ctx);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(35f, capturedContext.BaseDamage);
        Assert.Equal(35f, capturedContext.CurrentDamage);
        Assert.Contains("physical", capturedContext.Tags);
        Assert.Equal(35f, capturedContext.Modifiers["base_damage"]);
        Assert.Equal(0f, capturedContext.Modifiers["added_damage"]);
        Assert.Equal(25f, capturedContext.Modifiers["source.resources.crit_chance.current"]);
        Assert.Equal(1.5f, capturedContext.Modifiers["source.resources.crit_multiplier.current"]);
        Assert.Equal(10f, capturedContext.Modifiers["target.resources.armor.current"]);
        Assert.Equal(
            10f / 999f * 100f,
            capturedContext.Modifiers["target.resources.armor.percent"],
            precision: 3);
        Assert.False(capturedContext.Modifiers.ContainsKey("target_armor"));
        Assert.Equal("warrior", capturedContext.Metadata["attacker_id"]);
        Assert.Equal("goblin", capturedContext.Metadata["target_id"]);
        Assert.Equal("sword_slash", capturedContext.Metadata["action_id"]);
    }

    [Fact]
    public void CalculateDamage_WithNullActionTags_InitializesEmptyTagSet()
    {
        // Arrange
        var calculator = new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
        var action = new ActionDefinition
        {
            ActionId = "untagged_action",
            Tags = null,
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = 20f, Target = EffectTarget.TARGET }
            }
        };
        var attacker = CreateEntity("attacker", isHero: true);
        var target = CreateEntity("target", isHero: false);

        DamageContext? capturedContext = null;
        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Callback<DamageContext>(ctx => capturedContext = ctx)
            .Returns((DamageContext ctx) => ctx);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Empty(capturedContext.Tags);
    }

    // ==================== STATUS EFFECT INTEGRATION ====================

    [Fact]
    public void CalculateDamage_WithStatusEffectManager_AppliesAttackerModifiers()
    {
        // Arrange
        var calculator = new DamageCalculator(
            _mockPipelineManager.Object,
            _mockEventBus.Object,
            _mockLogger.Object,
            _mockStatusEffectManager.Object);

        var action = DamageTestHelpers.CreateActionWithDamage("empowered_strike", 50f);
        var attackerId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        var attacker = CreateEntity(attackerId, isHero: true);
        var target = CreateEntity(targetId, isHero: false);

        // Setup status effect modifiers for attacker (e.g., Strength buff)
        _mockStatusEffectManager
            .Setup(m => m.GetPipelineModifiers(attackerId))
            .Returns(new Dictionary<string, float>
            {
                ["increased_damage_total"] = 20f
            });

        _mockStatusEffectManager
            .Setup(m => m.GetPipelineModifiers(targetId))
            .Returns(new Dictionary<string, float>());

        DamageContext? capturedContext = null;
        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Callback<DamageContext>(ctx => capturedContext = ctx)
            .Returns((DamageContext ctx) => ctx);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(20f, capturedContext.Modifiers["increased_damage_total"]);
    }

    [Fact]
    public void CalculateDamage_WithStatusEffectManager_AppliesTargetModifiers()
    {
        // Arrange
        var calculator = new DamageCalculator(
            _mockPipelineManager.Object,
            _mockEventBus.Object,
            _mockLogger.Object,
            _mockStatusEffectManager.Object);

        var action = DamageTestHelpers.CreateActionWithDamage("basic_attack", 40f);
        var attackerId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        var attacker = CreateEntity(attackerId, isHero: true);
        var target = CreateEntity(targetId, isHero: false);

        // Setup status effect modifiers for target (e.g., Vulnerable debuff)
        _mockStatusEffectManager
            .Setup(m => m.GetPipelineModifiers(attackerId))
            .Returns(new Dictionary<string, float>());

        _mockStatusEffectManager
            .Setup(m => m.GetPipelineModifiers(targetId))
            .Returns(new Dictionary<string, float>
            {
                ["damage_taken_multiplier"] = 1.5f
            });

        DamageContext? capturedContext = null;
        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Callback<DamageContext>(ctx => capturedContext = ctx)
            .Returns((DamageContext ctx) => ctx);

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(1.5f, capturedContext.Modifiers["damage_taken_multiplier"]);
    }

    [Fact]
    public void CalculateDamage_WithOpaqueEntityIds_QueriesStatusModifiers()
    {
        // Arrange
        var calculator = new DamageCalculator(
            _mockPipelineManager.Object,
            _mockEventBus.Object,
            _mockLogger.Object,
            _mockStatusEffectManager.Object);

        var action = DamageTestHelpers.CreateActionWithDamage("attack", 30f);
        var attacker = CreateEntity("not-a-guid-attacker", isHero: true);
        var target = CreateEntity("not-a-guid-target", isHero: false);

        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx);
        _mockStatusEffectManager
            .Setup(m => m.GetPipelineModifiers(It.IsAny<string>()))
            .Returns(new Dictionary<string, float>());

        // Act
        var exception = Record.Exception(() => calculator.CalculateDamage(action, attacker, target));

        // Assert
        Assert.Null(exception);
        _mockStatusEffectManager.Verify(m => m.GetPipelineModifiers("not-a-guid-attacker"), Times.Once);
        _mockStatusEffectManager.Verify(m => m.GetPipelineModifiers("not-a-guid-target"), Times.Once);
    }

    [Fact]
    public void CalculateDamage_WithNullStatusEffectModifiers_DoesNotThrow()
    {
        // Arrange
        var calculator = new DamageCalculator(
            _mockPipelineManager.Object,
            _mockEventBus.Object,
            _mockLogger.Object,
            _mockStatusEffectManager.Object);

        var action = DamageTestHelpers.CreateActionWithDamage("attack", 25f);
        var attackerId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        var attacker = CreateEntity(attackerId, isHero: true);
        var target = CreateEntity(targetId, isHero: false);

        _mockStatusEffectManager
            .Setup(m => m.GetPipelineModifiers(It.IsAny<string>()))
            .Returns((Dictionary<string, float>?)null);

        _mockPipelineManager
            .Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns((DamageContext ctx) => ctx);

        // Act
        var exception = Record.Exception(() => calculator.CalculateDamage(action, attacker, target));

        // Assert
        Assert.Null(exception);
    }

    // ==================== HELPER METHODS ====================

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
            ResourceState = new ResourceSet
            {
                OwnerId = id,
                Resources = resources
            }
        };
    }
}
