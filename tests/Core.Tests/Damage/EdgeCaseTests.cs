using System;
using System.Collections.Generic;
using Core.Damage;
using Core.Events;
using Core.Logging;
using Core.Math;
using Moq;
using Xunit;

namespace Core.Tests.Damage;

/// <summary>
/// Testes de casos extremos e situações limites do sistema de dano.
/// </summary>
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

    // ==================== EMPTY/NULL INPUTS ====================

    [Fact]
    public void DamageContext_WithEmptyTags_WorksCorrectly()
    {
        // Arrange & Act
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string>(),
            Modifiers = new Dictionary<string, float>(),
            Metadata = new Dictionary<string, object>()
        };

        // Assert
        Assert.NotNull(context.Tags);
        Assert.Empty(context.Tags);
    }

    [Fact]
    public void Bucket_WithNoOperations_PassesThroughUnchanged()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "empty_bucket",
            Order = 1,
            Operations = new List<BucketOperation>()
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage);
    }

    [Fact]
    public void Bucket_WithNoFilterConditions_AlwaysExecutes()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "no_filter_bucket",
            Order = 1,
            FilterConditions = new List<FilterCondition>(),
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:2"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(200f, result.CurrentDamage);
    }

    // ==================== EXTREME VALUES ====================

    [Fact]
    public void Multiply_WithVeryLargeMultiplier_HandlesCorrectly()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "large_mult",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:1000000"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100000000f, result.CurrentDamage);
    }

    [Fact]
    public void AddFlat_WithVeryLargeValue_HandlesCorrectly()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "large_add",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "constant:999999999"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 1f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(1000000000f, result.CurrentDamage);
    }

    [Fact]
    public void Multiply_WithVerySmallMultiplier_HandlesCorrectly()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "small_mult",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:0.0001"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(0.01f, result.CurrentDamage, precision: 5);
    }

    // ==================== ZERO VALUES ====================

    [Fact]
    public void DamageContext_WithZeroDamage_WorksCorrectly()
    {
        // Arrange & Act
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 0f);

        // Assert
        Assert.Equal(0f, context.BaseDamage);
        Assert.Equal(0f, context.CurrentDamage);
    }

    [Fact]
    public void Multiply_ByZero_ResultsInZeroDamage()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "mult_zero",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:0"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(0f, result.CurrentDamage);
    }

    [Fact]
    public void AddFlat_WithZero_KeepsDamageUnchanged()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "add_zero",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "constant:0"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage);
    }

    // ==================== NEGATIVE VALUES ====================

    [Fact]
    public void DamageContext_WithNegativeDamage_WorksCorrectly()
    {
        // Arrange & Act
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: -50f);

        // Assert
        Assert.Equal(-50f, context.BaseDamage);
        Assert.Equal(-50f, context.CurrentDamage);
    }

    [Fact]
    public void AddFlat_WithNegativeValue_ReducesDamage()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "add_negative",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "constant:-30"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(70f, result.CurrentDamage);
    }

    [Fact]
    public void Multiply_WithNegativeMultiplier_InvertsDamage()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "mult_negative",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:-1"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(-100f, result.CurrentDamage);
    }

    // ==================== MISSING KEYS ====================

    [Fact]
    public void ModifierAbove_WithMissingKey_SkipsBucket()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "missing_key_test",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_ABOVE,
                    Parameter = "nonexistent_key:50"
                }
            },
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:2"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert - bucket não executou porque filtro falhou
        Assert.Equal(100f, result.CurrentDamage);
    }

    [Fact]
    public void AddFlat_FromMissingModifier_AddsZero()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "missing_modifier_source",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "modifier:nonexistent_key"
                }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage); // 100 + 0 (missing key defaults to 0)
    }

    // ==================== LARGE COLLECTIONS ====================

    [Fact]
    public void DamageContext_WithManyTags_HandlesCorrectly()
    {
        // Arrange
        var tags = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            tags.Add($"tag_{i}");
        }

        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = tags,
            Modifiers = new Dictionary<string, float>(),
            Metadata = new Dictionary<string, object>()
        };

        // Act & Assert
        Assert.Equal(100, context.Tags.Count);
        Assert.Contains("tag_50", context.Tags);
    }

    [Fact]
    public void DamageContext_WithManyModifiers_HandlesCorrectly()
    {
        // Arrange
        var modifiers = new Dictionary<string, float>();
        for (int i = 0; i < 100; i++)
        {
            modifiers[$"modifier_{i}"] = i * 1.5f;
        }

        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string>(),
            Modifiers = modifiers,
            Metadata = new Dictionary<string, object>()
        };

        // Act & Assert
        Assert.Equal(100, context.Modifiers.Count);
        Assert.Equal(75f, context.Modifiers["modifier_50"]);
    }

    [Fact]
    public void Bucket_WithManyOperations_ExecutesAllInOrder()
    {
        // Arrange
        var operations = new List<BucketOperation>();
        for (int i = 0; i < 50; i++)
        {
            operations.Add(new BucketOperation
            {
                Type = OperationType.ADD_FLAT,
                Source = "constant:1"
            });
        }

        var bucket = new BucketDefinition
        {
            BucketId = "many_ops",
            Order = 1,
            Operations = operations
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(150f, result.CurrentDamage); // 100 + (50 * 1)
    }

    // ==================== CHAINED OPERATIONS ====================

    [Fact]
    public void MultipleMultiplications_ChainCorrectly()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "chain_mult",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:2" },
                new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:3" },
                new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:5" }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 10f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(300f, result.CurrentDamage); // 10 * 2 * 3 * 5
    }

    [Fact]
    public void MixedOperations_ExecuteInOrder()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "mixed_ops",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation { Type = OperationType.ADD_FLAT, Source = "constant:50" },
                new BucketOperation { Type = OperationType.MULTIPLY, Source = "constant:2" },
                new BucketOperation { Type = OperationType.ADD_FLAT, Source = "constant:-30" }
            }
        };

        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(270f, result.CurrentDamage); // (100 + 50) * 2 - 30 = 270
    }
}
