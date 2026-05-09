using System;
using System.Collections.Generic;
using Core.Combat;
using Core.Damage;
using Core.Damage.Events;
using Core.Events;
using Core.Logging;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

public class DamageCalculatorTests
{
    private readonly Mock<IPipelineManager> _mockPipelineManager;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<ILogger> _mockLogger;

    public DamageCalculatorTests()
    {
        _mockPipelineManager = new Mock<IPipelineManager>();
        _mockEventBus = new Mock<IEventBus>();
        _mockLogger = new Mock<ILogger>();
    }

    private DamageCalculator CreateCalculator()
    {
        return new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, _mockLogger.Object);
    }

    private CombatEntity CreateTestEntity(
        string entityId,
        float critChance = 0f,
        float critMultiplier = 2.0f,
        float armor = 0f)
    {
        var resources = new Dictionary<string, ResourcePool>
        {
            ["health"] = new ResourcePool
            {
                ResourceId = "health",
                Current = 100f,
                Maximum = 100f,
                Minimum = 0f,
                Definition = new ResourceDefinition { ResourceId = "health", DisplayName = "Health" }
            },
            ["crit_chance"] = new ResourcePool
            {
                ResourceId = "crit_chance",
                Current = critChance,
                Maximum = 100f,
                Minimum = 0f,
                Definition = new ResourceDefinition { ResourceId = "crit_chance", DisplayName = "Crit Chance" }
            },
            ["crit_multiplier"] = new ResourcePool
            {
                ResourceId = "crit_multiplier",
                Current = critMultiplier,
                Maximum = 10f,
                Minimum = 1f,
                Definition = new ResourceDefinition { ResourceId = "crit_multiplier", DisplayName = "Crit Multiplier" }
            },
            ["armor"] = new ResourcePool
            {
                ResourceId = "armor",
                Current = armor,
                Maximum = 1000f,
                Minimum = 0f,
                Definition = new ResourceDefinition { ResourceId = "armor", DisplayName = "Armor" }
            }
        };

        return new CombatEntity
        {
            EntityId = entityId,
            Name = entityId,
            ResourceState = new EntityResourceState
            {
                EntityId = entityId,
                Resources = resources
            }
        };
    }

    // ==================== CONSTRUCTOR VALIDATION ====================

    [Fact]
    public void Constructor_WithNullPipelineManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new DamageCalculator(null!, _mockEventBus.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullEventBus_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new DamageCalculator(_mockPipelineManager.Object, null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new DamageCalculator(_mockPipelineManager.Object, _mockEventBus.Object, null!));
    }

    // ==================== CALCULATE DAMAGE ====================

    [Fact]
    public void CalculateDamage_WithBasicAction_ReturnsCorrectDamage()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "basic_attack",
            BaseDamage = 100f,
            Tags = new List<string> { "physical", "melee" }
        };

        var attacker = CreateTestEntity("player1", critChance: 5f, critMultiplier: 1.5f);
        var target = CreateTestEntity("enemy1", armor: 10f);

        // Mock pipeline para retornar dano processado
        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => new DamageContext
            {
                BaseDamage = ctx.BaseDamage,
                CurrentDamage = 150f, // Dano após pipeline
                Tags = ctx.Tags,
                Modifiers = ctx.Modifiers,
                Metadata = ctx.Metadata
            });

        var calculator = CreateCalculator();

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(150f, result.FinalDamage);
        Assert.Equal(0, result.CritTier);
    }

    [Fact]
    public void CalculateDamage_WithNegativeDamage_ReturnsZero()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "weak_attack",
            BaseDamage = 10f
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        // Mock pipeline para retornar dano negativo
        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => new DamageContext
            {
                BaseDamage = ctx.BaseDamage,
                CurrentDamage = -50f, // Dano negativo após mitigação
                Tags = ctx.Tags,
                Modifiers = ctx.Modifiers,
                Metadata = ctx.Metadata
            });

        var calculator = CreateCalculator();

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(0f, result.FinalDamage);
    }

    [Fact]
    public void CalculateDamage_WithCritTier_ExtractsCritTierFromMetadata()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "crit_attack",
            BaseDamage = 100f
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        // Mock pipeline para retornar com crit tier
        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => new DamageContext
            {
                BaseDamage = ctx.BaseDamage,
                CurrentDamage = 250f,
                Tags = ctx.Tags,
                Modifiers = ctx.Modifiers,
                Metadata = new Dictionary<string, object>
                {
                    ["crit_tier"] = 2,
                    ["attacker_id"] = "player1",
                    ["target_id"] = "enemy1",
                    ["action_id"] = "crit_attack"
                }
            });

        var calculator = CreateCalculator();

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(250f, result.FinalDamage);
        Assert.Equal(2, result.CritTier);
    }

    [Fact]
    public void CalculateDamage_WithoutCritTier_ReturnsCritTierZero()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "normal_attack",
            BaseDamage = 100f
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        // Mock pipeline sem crit tier
        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => new DamageContext
            {
                BaseDamage = ctx.BaseDamage,
                CurrentDamage = 100f,
                Tags = ctx.Tags,
                Modifiers = ctx.Modifiers,
                Metadata = new Dictionary<string, object>
                {
                    ["attacker_id"] = "player1",
                    ["target_id"] = "enemy1"
                }
            });

        var calculator = CreateCalculator();

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(0, result.CritTier);
    }

    // ==================== EVENT EMISSION ====================

    [Fact]
    public void CalculateDamage_EmitsDamageCalculatedEvent()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "test_action",
            BaseDamage = 100f,
            Tags = new List<string> { "fire" }
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => new DamageContext
            {
                BaseDamage = ctx.BaseDamage,
                CurrentDamage = 150f,
                Tags = ctx.Tags,
                Modifiers = ctx.Modifiers,
                Metadata = new Dictionary<string, object>
                {
                    ["crit_tier"] = 1
                }
            });

        var calculator = CreateCalculator();

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        _mockEventBus.Verify(e => e.Publish(It.Is<DamageCalculatedEvent>(evt =>
            evt.ActionId == "test_action" &&
            evt.AttackerId == "player1" &&
            evt.TargetId == "enemy1" &&
            evt.BaseDamage == 100f &&
            evt.FinalDamage == 150f &&
            evt.CritTier == 1 &&
            evt.Tags.Contains("fire")
        )), Times.Once);
    }

    // ==================== INITIAL CONTEXT BUILDING ====================

    [Fact]
    public void CalculateDamage_BuildsInitialContextWithCorrectModifiers()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "test_action",
            BaseDamage = 100f,
            Tags = new List<string> { "physical" }
        };

        var attacker = CreateTestEntity("player1", critChance: 25f, critMultiplier: 2.0f);
        var target = CreateTestEntity("enemy1", armor: 50f);

        DamageContext? capturedContext = null;
        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Callback<DamageContext>(ctx => capturedContext = ctx)
            .Returns<DamageContext>(ctx => ctx);

        var calculator = CreateCalculator();

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(100f, capturedContext.BaseDamage);
        Assert.Equal(100f, capturedContext.CurrentDamage);
        Assert.Contains("physical", capturedContext.Tags);
        Assert.Equal(100f, capturedContext.Modifiers["base_damage"]);
        Assert.Equal(25f, capturedContext.Modifiers["crit_chance"]);
        Assert.Equal(2.0f, capturedContext.Modifiers["crit_multiplier"]);
        Assert.Equal(50f, capturedContext.Modifiers["target_armor"]);
    }

    [Fact]
    public void CalculateDamage_WithNullBaseDamage_UsesZero()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "no_damage_action",
            BaseDamage = null
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        DamageContext? capturedContext = null;
        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Callback<DamageContext>(ctx => capturedContext = ctx)
            .Returns<DamageContext>(ctx => ctx);

        var calculator = CreateCalculator();

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(0f, capturedContext.BaseDamage);
        Assert.Equal(0f, capturedContext.CurrentDamage);
    }

    [Fact]
    public void CalculateDamage_WithNullTags_UsesEmptySet()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "no_tags_action",
            BaseDamage = 50f,
            Tags = null
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        DamageContext? capturedContext = null;
        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Callback<DamageContext>(ctx => capturedContext = ctx)
            .Returns<DamageContext>(ctx => ctx);

        var calculator = CreateCalculator();

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Empty(capturedContext.Tags);
    }

    // ==================== LOGGING ====================

    [Fact]
    public void CalculateDamage_LogsCalculationStartAndEnd()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "logged_action",
            BaseDamage = 100f
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => ctx);

        var calculator = CreateCalculator();

        // Act
        calculator.CalculateDamage(action, attacker, target);

        // Assert
        _mockLogger.Verify(l => l.LogDebug(It.Is<string>(s => 
            s.Contains("Calculating damage") && 
            s.Contains("logged_action") && 
            s.Contains("player1") && 
            s.Contains("enemy1"))), Times.Once);

        _mockLogger.Verify(l => l.LogDebug(It.Is<string>(s => 
            s.Contains("Damage calculated"))), Times.Once);

        _mockLogger.Verify(l => l.LogDebug(It.Is<string>(s => 
            s.Contains("Initial context"))), Times.Once);
    }

    // ==================== METADATA PRESERVATION ====================

    [Fact]
    public void CalculateDamage_PreservesMetadataInResult()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "metadata_action",
            BaseDamage = 100f
        };

        var attacker = CreateTestEntity("player1");
        var target = CreateTestEntity("enemy1");

        var customMetadata = new Dictionary<string, object>
        {
            ["custom_key"] = "custom_value",
            ["damage_type"] = "fire",
            ["crit_tier"] = 3
        };

        _mockPipelineManager.Setup(p => p.ExecutePipeline(It.IsAny<DamageContext>()))
            .Returns<DamageContext>(ctx => new DamageContext
            {
                BaseDamage = ctx.BaseDamage,
                CurrentDamage = 200f,
                Tags = ctx.Tags,
                Modifiers = ctx.Modifiers,
                Metadata = customMetadata
            });

        var calculator = CreateCalculator();

        // Act
        var result = calculator.CalculateDamage(action, attacker, target);

        // Assert
        Assert.Equal(customMetadata, result.Metadata);
        Assert.Equal("custom_value", result.Metadata["custom_key"]);
        Assert.Equal("fire", result.Metadata["damage_type"]);
    }
}
