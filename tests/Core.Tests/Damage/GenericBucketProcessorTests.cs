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
}
