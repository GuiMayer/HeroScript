using Xunit;
using Moq;
using Core.Damage;
using Core.Events;
using Core.Logging;
using Core.Math;
using System.Collections.Generic;

namespace Core.Tests.Damage;

/// <summary>
/// Testes para GenericBucketProcessor
/// Valida processamento de buckets com filtros e operações
/// </summary>
public class GenericBucketProcessorTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<IMathEngine> _mockMathEngine;

    public GenericBucketProcessorTests()
    {
        _mockLogger = DamageTestHelpers.CreateMockLogger();
        _mockEventBus = DamageTestHelpers.CreateMockEventBus();
        _mockMathEngine = DamageTestHelpers.CreateMockMathEngine();
    }

    [Fact]
    public void Process_WithNoFilters_ExecutesOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:2.0"
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

    [Fact]
    public void Process_WithMatchingFilter_ExecutesOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "filtered_bucket",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.TAG_PRESENT,
                    Parameter = "critical"
                }
            },
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:2.0"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f)
            .WithTag("critical");

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(200f, result.CurrentDamage);
    }

    [Fact]
    public void Process_WithNonMatchingFilter_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "filtered_bucket",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.TAG_PRESENT,
                    Parameter = "critical"
                }
            },
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:2.0"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage); // Sem mudança
    }

    [Fact]
    public void Process_WithMultipleFilters_RequiresAllToMatch()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "multi_filter_bucket",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.TAG_PRESENT,
                    Parameter = "critical"
                },
                new FilterCondition
                {
                    Type = FilterType.TAG_PRESENT,
                    Parameter = "physical"
                }
            },
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:3.0"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        
        var contextWithBothTags = DamageTestHelpers.CreateBasicContext(baseDamage: 100f)
            .WithTag("critical")
            .WithTag("physical");
            
        var contextWithOneTag = DamageTestHelpers.CreateBasicContext(baseDamage: 100f)
            .WithTag("critical");

        // Act
        var resultWithBoth = processor.Process(contextWithBothTags);
        var resultWithOne = processor.Process(contextWithOneTag);

        // Assert
        Assert.Equal(300f, resultWithBoth.CurrentDamage); // Ambos filtros passaram
        Assert.Equal(100f, resultWithOne.CurrentDamage);  // Um filtro falhou
    }

    [Fact]
    public void Process_WithMultipleOperations_AppliesInOrder()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "multi_op_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "constant:50"
                },
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:2.0"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        // (100 + 50) * 2 = 300
        Assert.Equal(300f, result.CurrentDamage);
    }

    [Fact]
    public void ExecuteAddFlat_WithInvalidSource_ReturnsUnchangedContext()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
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
        Assert.Equal(100f, result.CurrentDamage); // Unchanged
    }

    [Fact]
    public void ExecuteAddFlat_WithInvalidSource_LogsError()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "modifier:missing_key"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        processor.Process(context);

        // Assert
        _mockLogger.Verify(
            l => l.LogError(It.Is<string>(s => s.Contains("Failed to resolve value") && s.Contains("missing_key"))),
            Times.Once
        );
    }

    [Fact]
    public void ExecuteMultiply_WithInvalidSource_ReturnsUnchangedContext()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:invalid_number"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage); // Unchanged
    }

    [Fact]
    public void ExecuteSetModifier_WithInvalidSource_ReturnsUnchangedContext()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.SET_MODIFIER,
                    Source = "",
                    Parameters = new Dictionary<string, object> { ["key"] = "test_key" }
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.False(result.Modifiers.ContainsKey("test_key")); // Modifier not added
    }

    [Fact]
    public void ExecuteAddToModifier_WithInvalidSource_ReturnsUnchangedContext()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_TO_MODIFIER,
                    Source = "modifier:nonexistent",
                    Parameters = new Dictionary<string, object> { ["key"] = "test_key" }
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f)
            .WithModifier("test_key", 50f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(50f, result.Modifiers["test_key"]); // Unchanged
    }

    [Fact]
    public void ResolveValue_WithValidConstant_ReturnsValue()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "constant:123.45"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(223.45f, result.CurrentDamage, precision: 2);
    }

    [Fact]
    public void ResolveValue_WithExistingModifier_ReturnsValue()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "modifier:bonus_damage"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f)
            .WithModifier("bonus_damage", 25f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(125f, result.CurrentDamage);
    }

    [Fact]
    public void ResolveValue_WithCurrentDamage_ReturnsCurrentDamage()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "current_damage"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(200f, result.CurrentDamage); // 100 + 100
    }

    [Fact]
    public void ResolveValue_WithLiteralNumber_ReturnsValue()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "2.5"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(250f, result.CurrentDamage);
    }

    [Fact]
    public void ExecuteAddMoreMultiplier_AddsMultiplierToList()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_MORE_MULTIPLIER,
                    Source = "1.5"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Single(result.MoreMultipliers);
        Assert.Equal(1.5f, result.MoreMultipliers[0]);
        Assert.Equal(100f, result.CurrentDamage); // Not applied yet
    }

    [Fact]
    public void ExecuteAddMoreMultiplier_WithMultipleMultipliers_AddsAllToList()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_MORE_MULTIPLIER,
                    Source = "1.5"
                },
                new BucketOperation
                {
                    Type = OperationType.ADD_MORE_MULTIPLIER,
                    Source = "2.0"
                },
                new BucketOperation
                {
                    Type = OperationType.ADD_MORE_MULTIPLIER,
                    Source = "1.25"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(3, result.MoreMultipliers.Count);
        Assert.Equal(1.5f, result.MoreMultipliers[0]);
        Assert.Equal(2.0f, result.MoreMultipliers[1]);
        Assert.Equal(1.25f, result.MoreMultipliers[2]);
        Assert.Equal(100f, result.CurrentDamage); // Not applied yet
    }

    [Fact]
    public void ExecuteApplyMoreMultipliers_AppliesAllMultipliersSequentially()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_MORE_MULTIPLIER,
                    Source = "1.5"
                },
                new BucketOperation
                {
                    Type = OperationType.ADD_MORE_MULTIPLIER,
                    Source = "2.0"
                },
                new BucketOperation
                {
                    Type = OperationType.APPLY_MORE_MULTIPLIERS,
                    Source = ""
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        // 100 * 1.5 * 2.0 = 300
        Assert.Equal(300f, result.CurrentDamage);
    }

    [Fact]
    public void ExecuteApplyMoreMultipliers_WithNoMultipliers_ReturnsUnchanged()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.APPLY_MORE_MULTIPLIERS,
                    Source = ""
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

    [Fact]
    public void MoreMultipliers_ComplexScenario_WorksCorrectly()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "50"
                },
                new BucketOperation
                {
                    Type = OperationType.ADD_MORE_MULTIPLIER,
                    Source = "1.5"
                },
                new BucketOperation
                {
                    Type = OperationType.ADD_MORE_MULTIPLIER,
                    Source = "1.2"
                },
                new BucketOperation
                {
                    Type = OperationType.APPLY_MORE_MULTIPLIERS,
                    Source = ""
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        // (100 + 50) * 1.5 * 1.2 = 150 * 1.5 * 1.2 = 270
        Assert.Equal(270f, result.CurrentDamage);
    }

    [Fact]
    public void ExecuteAddMoreMultiplier_WithInvalidSource_ReturnsUnchanged()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "test_bucket",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_MORE_MULTIPLIER,
                    Source = "modifier:nonexistent"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Empty(result.MoreMultipliers); // No multiplier added
    }
}
