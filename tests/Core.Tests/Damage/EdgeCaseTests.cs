using System;
using System.Collections.Generic;
using Core.Combat.Models;
using Core.Damage;
using Core.Effects;
using Core.Events;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

/// <summary>
/// Testes de casos extremos e limites do sistema de dano
/// </summary>
[Trait("Category", "EdgeCase")]
public class EdgeCaseTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<IMathEngine> _mockMathEngine;

    public EdgeCaseTests()
    {
        _mockLogger = DamageTestHelpers.CreateMockLogger();
        _mockEventBus = DamageTestHelpers.CreateMockEventBus();
        _mockMathEngine = DamageTestHelpers.CreateMockMathEngine();
    }

    // ==================== EXTREME NUMERIC VALUES ====================

    [Fact]
    public void DamageContext_WithFloatMaxValue_DoesNotOverflow()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: float.MaxValue,
            modifiers: new Dictionary<string, float>
            {
                ["base_damage"] = float.MaxValue
            });

        // Act
        var exception = Record.Exception(() =>
        {
            var result = context with { CurrentDamage = float.MaxValue };
        });

        // Assert
        Assert.Null(exception);
        Assert.Equal(float.MaxValue, context.CurrentDamage);
    }

    [Fact]
    public void DamageContext_WithFloatMinValue_HandlesNegativeCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: float.MinValue,
            modifiers: new Dictionary<string, float>
            {
                ["base_damage"] = float.MinValue
            });

        // Act
        var exception = Record.Exception(() =>
        {
            var result = context with { CurrentDamage = float.MinValue };
        });

        // Assert
        Assert.Null(exception);
        Assert.Equal(float.MinValue, context.CurrentDamage);
    }

    [Fact]
    public void DamageContext_WithInfinityValue_HandlesGracefully()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: float.PositiveInfinity);

        // Act & Assert
        Assert.True(float.IsInfinity(context.BaseDamage));
        Assert.True(float.IsPositiveInfinity(context.BaseDamage));
    }

    [Fact]
    public void DamageContext_WithNaNValue_IsDetectable()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: float.NaN);

        // Act & Assert
        Assert.True(float.IsNaN(context.BaseDamage));
    }

    [Fact]
    public void DamageContext_WithVeryLargeMultiplier_CalculatesCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float>
            {
                ["damage_multiplier"] = 1000000f
            });

        // Assert
        Assert.Equal(100f, context.BaseDamage);
        Assert.Equal(1000000f, context.Modifiers["damage_multiplier"]);
    }

    [Fact]
    public void DamageContext_WithVerySmallMultiplier_MaintainsPrecision()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float>
            {
                ["damage_multiplier"] = 0.00001f
            });

        // Assert
        Assert.Equal(100f, context.BaseDamage);
        Assert.Equal(0.00001f, context.Modifiers["damage_multiplier"]);
    }

    // ==================== EMPTY/NULL COLLECTIONS ====================

    [Fact]
    public void DamageContext_WithEmptyModifiers_DoesNotThrow()
    {
        // Arrange & Act
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 50f,
            modifiers: new Dictionary<string, float>());

        // Assert
        Assert.NotNull(context.Modifiers);
        Assert.Empty(context.Modifiers);
    }

    [Fact]
    public void DamageContext_WithEmptyTags_DoesNotThrow()
    {
        // Arrange & Act
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 50f,
            tags: new List<string>());

        // Assert
        Assert.NotNull(context.Tags);
        Assert.Empty(context.Tags);
    }

    [Fact]
    public void DamageContext_WithEmptyMetadata_DoesNotThrow()
    {
        // Arrange & Act
        var context = new DamageContext
        {
            BaseDamage = 50f,
            CurrentDamage = 50f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            Metadata = new Dictionary<string, object>()
        };

        // Assert
        Assert.NotNull(context.Metadata);
        Assert.Empty(context.Metadata);
    }

    [Fact]
    public void DamageContext_WithVeryLargeDictionaries_HandlesEfficiently()
    {
        // Arrange
        var largeModifiers = new Dictionary<string, float>();
        for (int i = 0; i < 10000; i++)
        {
            largeModifiers[$"modifier_{i}"] = i * 0.1f;
        }

        // Act
        var exception = Record.Exception(() =>
        {
            var context = DamageTestHelpers.CreateBasicContext(
                baseDamage: 100f,
                modifiers: largeModifiers);
        });

        // Assert
        Assert.Null(exception);
    }

    // ==================== SPECIAL CHARACTER HANDLING ====================

    [Fact]
    public void DamageContext_WithSpecialCharactersInTags_HandlesCorrectly()
    {
        // Arrange
        var specialTags = new List<string>
        {
            "fire🔥",
            "ice❄️",
            "poison☠️",
            "holy✨",
            "tag_with_ünïcödé",
            "tag-with-dashes",
            "tag.with.dots",
            "tag$with$symbols"
        };

        // Act
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: specialTags);

        // Assert
        foreach (var tag in specialTags)
        {
            Assert.Contains(tag, context.Tags);
        }
    }

    [Fact]
    public void DamageContext_WithVeryLongTagName_HandlesCorrectly()
    {
        // Arrange
        var veryLongTag = new string('a', 10000);
        var tags = new List<string> { veryLongTag };

        // Act
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: tags);

        // Assert
        Assert.Contains(veryLongTag, context.Tags);
    }

    [Fact]
    public void DamageContext_WithWhitespaceInKeys_MaintainsWhitespace()
    {
        // Arrange
        var modifiers = new Dictionary<string, float>
        {
            ["  leading_spaces"] = 10f,
            ["trailing_spaces  "] = 20f,
            ["  both  "] = 30f,
            ["tab\ttab"] = 40f
        };

        // Act
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: modifiers);

        // Assert
        Assert.Equal(10f, context.Modifiers["  leading_spaces"]);
        Assert.Equal(20f, context.Modifiers["trailing_spaces  "]);
        Assert.Equal(30f, context.Modifiers["  both  "]);
        Assert.Equal(40f, context.Modifiers["tab\ttab"]);
    }

    // ==================== BOUNDARY CONDITIONS ====================

    [Fact]
    public void BucketDefinition_WithZeroOrder_IsValid()
    {
        // Arrange & Act
        var bucket = DamageTestHelpers.CreateSimpleBucket(
            bucketId: "zero_order_bucket",
            order: 0);

        // Assert
        Assert.Equal(0, bucket.Order);
    }

    [Fact]
    public void BucketDefinition_WithNegativeOrder_IsValid()
    {
        // Arrange & Act
        var bucket = DamageTestHelpers.CreateSimpleBucket(
            bucketId: "negative_order_bucket",
            order: -100);

        // Assert
        Assert.Equal(-100, bucket.Order);
    }

    [Fact]
    public void BucketDefinition_WithMaxIntOrder_IsValid()
    {
        // Arrange & Act
        var bucket = DamageTestHelpers.CreateSimpleBucket(
            bucketId: "max_order_bucket",
            order: int.MaxValue);

        // Assert
        Assert.Equal(int.MaxValue, bucket.Order);
    }

    [Fact]
    public void PipelineConfiguration_WithEmptyBuckets_IsValid()
    {
        // Arrange & Act
        var config = DamageTestHelpers.CreateSimplePipeline(
            configName: "empty_pipeline",
            buckets: new List<BucketDefinition>());

        // Assert
        Assert.NotNull(config.Buckets);
        Assert.Empty(config.Buckets);
    }

    [Fact]
    public void PipelineConfiguration_WithThousandsOfBuckets_HandlesCorrectly()
    {
        // Arrange
        var manyBuckets = new List<BucketDefinition>();
        for (int i = 0; i < 5000; i++)
        {
            manyBuckets.Add(DamageTestHelpers.CreateSimpleBucket($"bucket_{i}", order: i));
        }

        // Act
        var exception = Record.Exception(() =>
        {
            var config = DamageTestHelpers.CreateSimplePipeline(
                configName: "large_pipeline",
                buckets: manyBuckets);
        });

        // Assert
        Assert.Null(exception);
    }

    // ==================== MODIFIER KEY COLLISIONS ====================

    [Fact]
    public void DamageContext_WithDuplicateModifierKeys_LastValueWins()
    {
        // Arrange
        var modifiers = new Dictionary<string, float>
        {
            ["damage_bonus"] = 10f
        };
        modifiers["damage_bonus"] = 20f; // Overwrite

        // Act
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: modifiers);

        // Assert
        Assert.Equal(20f, context.Modifiers["damage_bonus"]);
    }

    [Fact]
    public void DamageContext_WithCaseVariationInKeys_TreatsAsDifferent()
    {
        // Arrange
        var modifiers = new Dictionary<string, float>
        {
            ["Damage_Bonus"] = 10f,
            ["damage_bonus"] = 20f,
            ["DAMAGE_BONUS"] = 30f
        };

        // Act
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: modifiers);

        // Assert
        Assert.Equal(3, context.Modifiers.Count);
        Assert.Equal(10f, context.Modifiers["Damage_Bonus"]);
        Assert.Equal(20f, context.Modifiers["damage_bonus"]);
        Assert.Equal(30f, context.Modifiers["DAMAGE_BONUS"]);
    }

    // ==================== TAG EDGE CASES ====================

    [Fact]
    public void DamageContext_WithDuplicateTags_StoresOnlyUnique()
    {
        // Arrange
        var tags = new List<string> { "fire", "fire", "fire", "ice", "ice" };

        // Act
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: tags);

        // Assert
        Assert.Equal(2, context.Tags.Count); // HashSet removes duplicates
        Assert.Contains("fire", context.Tags);
        Assert.Contains("ice", context.Tags);
    }

    [Fact]
    public void DamageContext_WithEmptyStringTag_IsAllowed()
    {
        // Arrange
        var tags = new List<string> { "", "fire", "" };

        // Act
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: tags);

        // Assert
        Assert.Contains("", context.Tags);
        Assert.Contains("fire", context.Tags);
    }

    [Fact]
    public void DamageContext_WithThousandsOfTags_HandlesCorrectly()
    {
        // Arrange
        var manyTags = new List<string>();
        for (int i = 0; i < 10000; i++)
        {
            manyTags.Add($"tag_{i}");
        }

        // Act
        var exception = Record.Exception(() =>
        {
            var context = DamageTestHelpers.CreateBasicContext(
                baseDamage: 100f,
                tags: manyTags);
        });

        // Assert
        Assert.Null(exception);
    }

    // ==================== METADATA EDGE CASES ====================

    [Fact]
    public void DamageContext_WithNullMetadataValues_HandlesCorrectly()
    {
        // Arrange
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            Metadata = new Dictionary<string, object>
            {
                ["null_value"] = null!,
                ["non_null_value"] = "test"
            }
        };

        // Act & Assert
        Assert.Null(context.Metadata["null_value"]);
        Assert.Equal("test", context.Metadata["non_null_value"]);
    }

    [Fact]
    public void DamageContext_WithMixedMetadataTypes_StoresCorrectly()
    {
        // Arrange
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            Metadata = new Dictionary<string, object>
            {
                ["int_value"] = 42,
                ["float_value"] = 3.14f,
                ["string_value"] = "test",
                ["bool_value"] = true,
                ["list_value"] = new List<int> { 1, 2, 3 },
                ["dict_value"] = new Dictionary<string, int> { ["a"] = 1 }
            }
        };

        // Assert
        Assert.Equal(42, context.Metadata["int_value"]);
        Assert.Equal(3.14f, context.Metadata["float_value"]);
        Assert.Equal("test", context.Metadata["string_value"]);
        Assert.Equal(true, context.Metadata["bool_value"]);
        Assert.IsType<List<int>>(context.Metadata["list_value"]);
        Assert.IsType<Dictionary<string, int>>(context.Metadata["dict_value"]);
    }

    // ==================== RESOURCE EDGE CASES ====================

    [Fact]
    public void CombatEntity_WithZeroMaximumHealth_IsValid()
    {
        // Arrange & Act
        var entity = CreateEntityWithHealth(
            id: "zero_max_entity",
            current: 0f,
            maximum: 0f);

        // Assert
        Assert.Equal(0f, entity.ResourceState.Resources["health"].Maximum);
    }

    [Fact]
    public void CombatEntity_WithNegativeArmor_IsAllowed()
    {
        // Arrange & Act
        var entity = CreateEntityWithArmor("negative_armor", -50f);

        // Assert
        Assert.Equal(-50f, entity.ResourceState.Resources["armor"].Current);
    }

    [Fact]
    public void CombatEntity_WithCurrentAboveMaximum_IsValid()
    {
        // Arrange & Act
        var entity = CreateEntityWithHealth(
            id: "overheal_entity",
            current: 150f,
            maximum: 100f);

        // Assert
        Assert.Equal(150f, entity.ResourceState.Resources["health"].Current);
        Assert.Equal(100f, entity.ResourceState.Resources["health"].Maximum);
    }

    // ==================== MORE MULTIPLIER EDGE CASES ====================

    [Fact]
    public void DamageContext_WithEmptyMoreMultipliers_IsValid()
    {
        // Arrange
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            MoreMultipliers = new List<float>(),
            Metadata = new Dictionary<string, object>()
        };

        // Assert
        Assert.NotNull(context.MoreMultipliers);
        Assert.Empty(context.MoreMultipliers);
    }

    [Fact]
    public void DamageContext_WithZeroMoreMultiplier_IsValid()
    {
        // Arrange
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            MoreMultipliers = new List<float> { 0f },
            Metadata = new Dictionary<string, object>()
        };

        // Assert
        Assert.Contains(0f, context.MoreMultipliers);
    }

    [Fact]
    public void DamageContext_WithNegativeMoreMultiplier_IsValid()
    {
        // Arrange
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            MoreMultipliers = new List<float> { -2.5f },
            Metadata = new Dictionary<string, object>()
        };

        // Assert
        Assert.Contains(-2.5f, context.MoreMultipliers);
    }

    [Fact]
    public void DamageContext_WithHundredsOfMoreMultipliers_HandlesCorrectly()
    {
        // Arrange
        var manyMultipliers = new List<float>();
        for (int i = 0; i < 1000; i++)
        {
            manyMultipliers.Add(1.1f);
        }

        // Act
        var exception = Record.Exception(() =>
        {
            var context = new DamageContext
            {
                BaseDamage = 100f,
                CurrentDamage = 100f,
                Modifiers = new Dictionary<string, float>(),
                Tags = new HashSet<string>(),
                MoreMultipliers = manyMultipliers,
                Metadata = new Dictionary<string, object>()
            };
        });

        // Assert
        Assert.Null(exception);
    }

    // ==================== HELPER METHODS ====================

    private static CombatEntity CreateEntityWithHealth(string id, float current, float maximum)
    {
        return new CombatEntity
        {
            EntityId = id,
            Name = id,
            IsHero = false,
            ResourceState = new ResourceSet
            {
                OwnerId = id,
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = new()
                    {
                        ResourceId = "health",
                        Current = current,
                        Maximum = maximum,
                        Minimum = 0f,
                        Definition = new ResourceDefinition
                        {
                            ResourceId = "health",
                            DisplayName = "Health",
                            Category = ResourceCategory.VITAL
                        }
                    }
                }
            }
        };
    }

    private static CombatEntity CreateEntityWithArmor(string id, float armor)
    {
        return new CombatEntity
        {
            EntityId = id,
            Name = id,
            IsHero = false,
            ResourceState = new ResourceSet
            {
                OwnerId = id,
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["armor"] = new()
                    {
                        ResourceId = "armor",
                        Current = armor,
                        Maximum = 999f,
                        Minimum = float.MinValue,
                        Definition = new ResourceDefinition
                        {
                            ResourceId = "armor",
                            DisplayName = "Armor",
                            Category = ResourceCategory.TEMPORARY
                        }
                    }
                }
            }
        };
    }
}
