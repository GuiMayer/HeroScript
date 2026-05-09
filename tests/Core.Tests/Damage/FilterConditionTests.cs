using Xunit;
using Moq;
using Core.Damage;
using Core.Events;
using Core.Logging;
using Core.Math;
using System.Collections.Generic;

namespace Core.Tests.Damage;

/// <summary>
/// Testes para avaliação de FilterCondition
/// Valida todos os tipos de filtros e edge cases
/// </summary>
public class FilterConditionTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<IMathEngine> _mockMathEngine;

    public FilterConditionTests()
    {
        _mockLogger = DamageTestHelpers.CreateMockLogger();
        _mockEventBus = DamageTestHelpers.CreateMockEventBus();
        _mockMathEngine = DamageTestHelpers.CreateMockMathEngine();
    }

    // ==================== TAG_PRESENT FILTER ====================

    [Fact]
    public void TagPresent_WithMatchingTag_PassesFilter()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "tag_present_test",
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
        Assert.Equal(200f, result.CurrentDamage); // Operação executada
    }

    [Fact]
    public void TagPresent_WithoutMatchingTag_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "tag_present_fail",
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
        Assert.Equal(100f, result.CurrentDamage); // Operação não executada
    }

    [Fact]
    public void TagPresent_CaseSensitive_MatchesExactly()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "tag_case_test",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.TAG_PRESENT,
                    Parameter = "Critical"
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
            .WithTag("critical"); // lowercase

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage); // Não deve passar (case sensitive)
    }

    // ==================== TAG_ABSENT FILTER ====================

    [Fact]
    public void TagAbsent_WithoutTag_PassesFilter()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "tag_absent_test",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.TAG_ABSENT,
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
        Assert.Equal(200f, result.CurrentDamage); // Operação executada
    }

    [Fact]
    public void TagAbsent_WithTag_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "tag_absent_fail",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.TAG_ABSENT,
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
        Assert.Equal(100f, result.CurrentDamage); // Operação não executada
    }

    // ==================== MODIFIER_PRESENT FILTER ====================

    [Fact]
    public void ModifierPresent_WithModifier_PassesFilter()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_present_test",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_PRESENT,
                    Parameter = "strength"
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "strength", 10f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(200f, result.CurrentDamage); // Operação executada
    }

    [Fact]
    public void ModifierPresent_WithoutModifier_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_present_fail",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_PRESENT,
                    Parameter = "strength"
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
        Assert.Equal(100f, result.CurrentDamage); // Operação não executada
    }

    [Fact]
    public void ModifierPresent_WithZeroValue_StillPassesFilter()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_zero_test",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_PRESENT,
                    Parameter = "strength"
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "strength", 0f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(200f, result.CurrentDamage); // Operação executada (modifier existe, mesmo com valor 0)
    }

    // ==================== MODIFIER_ABOVE FILTER ====================

    [Fact]
    public void ModifierAbove_WithValueAboveThreshold_PassesFilter()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_above_test",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_ABOVE,
                    Parameter = "crit_chance",
                    Value = 50f
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "crit_chance", 75f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(200f, result.CurrentDamage); // Operação executada
    }

    [Fact]
    public void ModifierAbove_WithValueBelowThreshold_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_above_fail",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_ABOVE,
                    Parameter = "crit_chance",
                    Value = 50f
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "crit_chance", 25f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage); // Operação não executada
    }

    [Fact]
    public void ModifierAbove_WithValueEqualToThreshold_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_above_equal",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_ABOVE,
                    Parameter = "crit_chance",
                    Value = 50f
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "crit_chance", 50f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage); // Operação não executada (deve ser ACIMA, não igual)
    }

    [Fact]
    public void ModifierAbove_WithMissingModifier_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_above_missing",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_ABOVE,
                    Parameter = "crit_chance",
                    Value = 50f
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
        Assert.Equal(100f, result.CurrentDamage); // Operação não executada
    }

    [Fact]
    public void ModifierAbove_WithNegativeValues_WorksCorrectly()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_above_negative",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_ABOVE,
                    Parameter = "resistance",
                    Value = -10f
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "resistance", -5f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(200f, result.CurrentDamage); // -5 > -10, então passa
    }

    // ==================== MODIFIER_BELOW FILTER ====================

    [Fact]
    public void ModifierBelow_WithValueBelowThreshold_PassesFilter()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_below_test",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_BELOW,
                    Parameter = "health_percent",
                    Value = 30f
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "health_percent", 15f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(200f, result.CurrentDamage); // Operação executada
    }

    [Fact]
    public void ModifierBelow_WithValueAboveThreshold_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_below_fail",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_BELOW,
                    Parameter = "health_percent",
                    Value = 30f
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "health_percent", 50f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage); // Operação não executada
    }

    [Fact]
    public void ModifierBelow_WithValueEqualToThreshold_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_below_equal",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_BELOW,
                    Parameter = "health_percent",
                    Value = 30f
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "health_percent", 30f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage); // Operação não executada (deve ser ABAIXO, não igual)
    }

    [Fact]
    public void ModifierBelow_WithMissingModifier_SkipsOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "modifier_below_missing",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_BELOW,
                    Parameter = "health_percent",
                    Value = 30f
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
        Assert.Equal(100f, result.CurrentDamage); // Operação não executada
    }

    // ==================== COMBINED FILTERS ====================

    [Fact]
    public void CombinedFilters_AllMustPass_ToExecuteOperations()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "combined_filters",
            Order = 1,
            FilterConditions = new List<FilterCondition>
            {
                new FilterCondition
                {
                    Type = FilterType.TAG_PRESENT,
                    Parameter = "physical"
                },
                new FilterCondition
                {
                    Type = FilterType.MODIFIER_ABOVE,
                    Parameter = "strength",
                    Value = 10f
                },
                new FilterCondition
                {
                    Type = FilterType.TAG_ABSENT,
                    Parameter = "magical"
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
        
        var contextAllPass = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "strength", 15f } },
            tags: new List<string> { "physical" }
        );
        
        var contextOneFails = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "strength", 5f } }, // Falha: strength <= 10
            tags: new List<string> { "physical" }
        );

        // Act
        var resultAllPass = processor.Process(contextAllPass);
        var resultOneFails = processor.Process(contextOneFails);

        // Assert
        Assert.Equal(300f, resultAllPass.CurrentDamage);   // Todos filtros passaram
        Assert.Equal(100f, resultOneFails.CurrentDamage);  // Um filtro falhou
    }
}
