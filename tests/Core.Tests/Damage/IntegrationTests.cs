using System;
using System.Collections.Generic;
using Core.Combat;
using Core.Damage;
using Core.Events;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

/// <summary>
/// Testes de integração que simulam diferentes estilos de sistemas de dano
/// (Path of Exile, Genshin Impact, Card Game, Simple RPG)
/// </summary>
public class IntegrationTests
{
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IMathEngine> _mockMathEngine;
    private readonly Mock<IRandomProvider> _mockRandomProvider;

    public IntegrationTests()
    {
        _mockEventBus = new Mock<IEventBus>();
        _mockLogger = new Mock<ILogger>();
        _mockMathEngine = new Mock<IMathEngine>();
        _mockRandomProvider = new Mock<IRandomProvider>();
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

    // ==================== PATH OF EXILE STYLE ====================

    [Fact]
    public void PathOfExileStyle_IncreasedDamage_AddsAdditively()
    {
        // Arrange - PoE style: increased damage adds together
        var config = new PipelineConfiguration
        {
            ConfigName = "poe_increased",
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "increased",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        // CurrentDamage * (1 + increased_total)
                        // First calculate the multiplier: 1 + increased
                        new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:1.5" } // 1 + 0.5 = 1.5
                    }
                }
            }
        };

        var manager = PipelineManager.CreateWithConfig(config, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, _mockRandomProvider.Object);

        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string>(),
            Modifiers = new Dictionary<string, float>(),
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = manager.ExecutePipeline(context);

        // Assert - 100 * (1 + 0.5) = 150
        Assert.Equal(150f, result.CurrentDamage);
    }

    [Fact]
    public void PathOfExileStyle_MoreDamage_MultipliesSeparately()
    {
        // Arrange - PoE style: more multipliers are separate
        var config = new PipelineConfiguration
        {
            ConfigName = "poe_more",
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "more1",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.MULTIPLY, Source = "modifier:more_multiplier_1" }
                    }
                },
                new BucketDefinition
                {
                    BucketId = "more2",
                    Order = 2,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.MULTIPLY, Source = "modifier:more_multiplier_2" }
                    }
                }
            }
        };

        var manager = PipelineManager.CreateWithConfig(config, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, _mockRandomProvider.Object);

        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string>(),
            Modifiers = new Dictionary<string, float>
            {
                ["more_multiplier_1"] = 1.3f, // 30% more
                ["more_multiplier_2"] = 1.2f  // 20% more
            },
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = manager.ExecutePipeline(context);

        // Assert - 100 * 1.3 * 1.2 = 156
        Assert.Equal(156f, result.CurrentDamage);
    }

    // ==================== GENSHIN IMPACT STYLE ====================

    [Fact]
    public void GenshinStyle_ElementalBonus_AppliesConditionally()
    {
        // Arrange - Genshin style: elemental damage bonus
        var config = new PipelineConfiguration
        {
            ConfigName = "genshin_elemental",
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "elemental_bonus",
                    Order = 1,
                    FilterConditions = new List<FilterCondition>
                    {
                        new FilterCondition { Type = FilterType.TAG_PRESENT, Parameter = "pyro" }
                    },
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:1.466" } // 46.6% pyro bonus
                    }
                }
            }
        };

        var manager = PipelineManager.CreateWithConfig(config, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, _mockRandomProvider.Object);

        var contextWithPyro = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string> { "pyro" },
            Modifiers = new Dictionary<string, float>(),
            Metadata = new Dictionary<string, object>()
        };

        var contextWithoutPyro = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string> { "physical" },
            Modifiers = new Dictionary<string, float>(),
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var resultWithPyro = manager.ExecutePipeline(contextWithPyro);
        var resultWithoutPyro = manager.ExecutePipeline(contextWithoutPyro);

        // Assert
        Assert.Equal(146.6f, resultWithPyro.CurrentDamage, precision: 1);
        Assert.Equal(100f, resultWithoutPyro.CurrentDamage);
    }

    // ==================== CARD GAME STYLE ====================

    [Fact]
    public void CardGameStyle_TypeAdvantage_DoublesOrHalvesDamage()
    {
        // Arrange - Card game style: type advantage/weakness
        var config = new PipelineConfiguration
        {
            ConfigName = "card_type",
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "advantage",
                    Order = 1,
                    FilterConditions = new List<FilterCondition>
                    {
                        new FilterCondition { Type = FilterType.MODIFIER_PRESENT, Parameter = "type_advantage" }
                    },
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:2.0" }
                    }
                },
                new BucketDefinition
                {
                    BucketId = "weakness",
                    Order = 2,
                    FilterConditions = new List<FilterCondition>
                    {
                        new FilterCondition { Type = FilterType.MODIFIER_PRESENT, Parameter = "type_weakness" }
                    },
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:0.5" }
                    }
                }
            }
        };

        var manager = PipelineManager.CreateWithConfig(config, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, _mockRandomProvider.Object);

        var advantageContext = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string>(),
            Modifiers = new Dictionary<string, float> { ["type_advantage"] = 1f },
            Metadata = new Dictionary<string, object>()
        };

        var weaknessContext = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string>(),
            Modifiers = new Dictionary<string, float> { ["type_weakness"] = 1f },
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var advantageResult = manager.ExecutePipeline(advantageContext);
        var weaknessResult = manager.ExecutePipeline(weaknessContext);

        // Assert
        Assert.Equal(200f, advantageResult.CurrentDamage);
        Assert.Equal(50f, weaknessResult.CurrentDamage);
    }

    [Fact]
    public void CardGameStyle_ShieldReduction_BlocksDamage()
    {
        // Arrange - Card game style: shield blocks damage
        var config = new PipelineConfiguration
        {
            ConfigName = "card_shield",
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "shield",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.ADD_FLAT, Source = "modifier:shield_value" }
                    }
                }
            }
        };

        var manager = PipelineManager.CreateWithConfig(config, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, _mockRandomProvider.Object);

        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string>(),
            Modifiers = new Dictionary<string, float>
            {
                ["shield_value"] = -30f // Shield blocks 30 damage
            },
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = manager.ExecutePipeline(context);

        // Assert - 100 - 30 = 70
        Assert.Equal(70f, result.CurrentDamage);
    }

    // ==================== SIMPLE RPG STYLE ====================

    [Fact]
    public void SimpleRPGStyle_AttackStat_MultipliesBaseDamage()
    {
        // Arrange - Simple RPG: attack stat multiplies base damage
        var config = new PipelineConfiguration
        {
            ConfigName = "rpg_attack",
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "attack_stat",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.MULTIPLY, Source = "modifier:attack_multiplier" }
                    }
                }
            }
        };

        var manager = PipelineManager.CreateWithConfig(config, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, _mockRandomProvider.Object);

        var context = new DamageContext
        {
            BaseDamage = 50f,
            CurrentDamage = 50f,
            Tags = new HashSet<string>(),
            Modifiers = new Dictionary<string, float>
            {
                ["attack_multiplier"] = 2.5f // 150 attack stat = 2.5x multiplier
            },
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = manager.ExecutePipeline(context);

        // Assert - 50 * 2.5 = 125
        Assert.Equal(125f, result.CurrentDamage);
    }

    [Fact]
    public void SimpleRPGStyle_DefenseReduction_SubtractsFlat()
    {
        // Arrange - Simple RPG: defense subtracts flat amount
        var config = new PipelineConfiguration
        {
            ConfigName = "rpg_defense",
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "defense",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.ADD_FLAT, Source = "modifier:defense_reduction" }
                    }
                }
            }
        };

        var manager = PipelineManager.CreateWithConfig(config, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, _mockRandomProvider.Object);

        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string>(),
            Modifiers = new Dictionary<string, float>
            {
                ["defense_reduction"] = -25f // 25 defense
            },
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = manager.ExecutePipeline(context);

        // Assert - 100 - 25 = 75
        Assert.Equal(75f, result.CurrentDamage);
    }

    [Fact]
    public void SimpleRPGStyle_CombinedAttackAndDefense_WorksTogether()
    {
        // Arrange - Simple RPG: attack multiplier then defense reduction
        var config = new PipelineConfiguration
        {
            ConfigName = "rpg_combined",
            Buckets = new List<BucketDefinition>
            {
                new BucketDefinition
                {
                    BucketId = "attack",
                    Order = 1,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.MULTIPLY, Source = "modifier:attack_multiplier" }
                    }
                },
                new BucketDefinition
                {
                    BucketId = "defense",
                    Order = 2,
                    Operations = new List<BucketOperation>
                    {
                        new BucketOperation { Type = OperationType.ADD_FLAT, Source = "modifier:defense_reduction" }
                    }
                }
            }
        };

        var manager = PipelineManager.CreateWithConfig(config, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, _mockRandomProvider.Object);

        var context = new DamageContext
        {
            BaseDamage = 50f,
            CurrentDamage = 50f,
            Tags = new HashSet<string>(),
            Modifiers = new Dictionary<string, float>
            {
                ["attack_multiplier"] = 2.0f,
                ["defense_reduction"] = -20f
            },
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = manager.ExecutePipeline(context);

        // Assert - (50 * 2.0) - 20 = 80
        Assert.Equal(80f, result.CurrentDamage);
    }
}
