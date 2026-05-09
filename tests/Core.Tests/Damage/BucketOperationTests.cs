using Xunit;
using Moq;
using Core.Damage;
using Core.Events;
using Core.Logging;
using Core.Math;
using System.Collections.Generic;

namespace Core.Tests.Damage;

/// <summary>
/// Testes para operações de bucket (MULTIPLY, ADD_FLAT, etc.)
/// Valida cada tipo de operação isoladamente
/// </summary>
public class BucketOperationTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<IMathEngine> _mockMathEngine;

    public BucketOperationTests()
    {
        _mockLogger = DamageTestHelpers.CreateMockLogger();
        _mockEventBus = DamageTestHelpers.CreateMockEventBus();
        _mockMathEngine = DamageTestHelpers.CreateMockMathEngine();
    }

    // ==================== MULTIPLY OPERATION ====================

    [Fact]
    public void Multiply_WithConstantValue_MultipliesDamage()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "multiply_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "constant:1.5"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(150f, result.CurrentDamage);
    }

    [Fact]
    public void Multiply_WithModifier_UsesModifierValue()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "multiply_modifier",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.MULTIPLY,
                    Source = "modifier:strength_mult"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "strength_mult", 1.2f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(120f, result.CurrentDamage, precision: 2); // Tolera pequenas diferenças de ponto flutuante
    }

    // ==================== ADD_FLAT OPERATION ====================

    [Fact]
    public void AddFlat_WithConstantValue_AddsToDamage()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "add_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_FLAT,
                    Source = "constant:50"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(150f, result.CurrentDamage);
    }

    [Fact]
    public void AddFlat_WithModifier_AddsModifierValue()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "add_modifier",
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
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "bonus_damage", 25f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(125f, result.CurrentDamage);
    }

    // ==================== SET_TAG OPERATION ====================

    [Fact]
    public void SetTag_AddsTagToContext()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "set_tag_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.SET_TAG,
                    Source = "tag:critical"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Contains("critical", result.Tags);
        Assert.Equal(100f, result.CurrentDamage); // Dano não muda
    }

    // ==================== REMOVE_TAG OPERATION ====================

    [Fact]
    public void RemoveTag_RemovesTagFromContext()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "remove_tag_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.REMOVE_TAG,
                    Source = "tag:physical"
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: new List<string> { "physical", "melee" }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.DoesNotContain("physical", result.Tags);
        Assert.Contains("melee", result.Tags);
    }

    // ==================== SET_MODIFIER OPERATION ====================

    [Fact]
    public void SetModifier_SetsModifierValue()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "set_modifier_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.SET_MODIFIER,
                    Source = "constant:42",
                    Parameters = new Dictionary<string, object> { { "key", "new_modifier" } }
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.True(result.Modifiers.ContainsKey("new_modifier"));
        Assert.Equal(42f, result.Modifiers["new_modifier"]);
    }

    // ==================== ADD_TO_MODIFIER OPERATION ====================

    [Fact]
    public void AddToModifier_AddsToExistingModifier()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "add_to_modifier_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_TO_MODIFIER,
                    Source = "constant:10",
                    Parameters = new Dictionary<string, object> { { "key", "strength" } }
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "strength", 20f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(30f, result.Modifiers["strength"]);
    }

    [Fact]
    public void AddToModifier_CreatesModifierIfMissing()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "add_to_new_modifier",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ADD_TO_MODIFIER,
                    Source = "constant:15",
                    Parameters = new Dictionary<string, object> { { "key", "new_stat" } }
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, _mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.True(result.Modifiers.ContainsKey("new_stat"));
        Assert.Equal(15f, result.Modifiers["new_stat"]);
    }

    // ==================== APPLY_FORMULA OPERATION ====================

    [Fact]
    public void ApplyFormula_WithValidFormula_AppliesCorrectly()
    {
        // Arrange
        var mockMathEngine = new Mock<IMathEngine>();
        var mockExpression = new MathExpression(150f);
        
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "ARMOR_MITIGATION",
            100f,
            It.IsAny<Dictionary<string, float>>()))
            .Returns(mockExpression);

        var bucket = new BucketDefinition
        {
            BucketId = "formula_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.APPLY_FORMULA,
                    Source = "formula:ARMOR_MITIGATION",
                    Parameters = new Dictionary<string, object>
                    {
                        { "armor", 50f }
                    }
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(150f, result.CurrentDamage);
        mockMathEngine.Verify(m => m.BuildFromFormula("ARMOR_MITIGATION", 100f, It.IsAny<Dictionary<string, float>>()), Times.Once);
    }

    [Fact]
    public void ApplyFormula_WithModifierParameter_PassesModifierValue()
    {
        // Arrange
        var mockMathEngine = new Mock<IMathEngine>();
        var mockExpression = new MathExpression(75f);
        
        Dictionary<string, float>? capturedParams = null;
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "ARMOR_MITIGATION",
            100f,
            It.IsAny<Dictionary<string, float>>()))
            .Callback<string, float, Dictionary<string, float>>((formula, input, parameters) => 
            {
                capturedParams = parameters;
            })
            .Returns(mockExpression);

        var bucket = new BucketDefinition
        {
            BucketId = "formula_modifier_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.APPLY_FORMULA,
                    Source = "formula:ARMOR_MITIGATION",
                    Parameters = new Dictionary<string, object>
                    {
                        { "armor", "modifier:armor_value" }
                    }
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { { "armor_value", 200f } }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.NotNull(capturedParams);
        Assert.Equal(200f, capturedParams["armor"]);
    }

    [Fact]
    public void ApplyFormula_WithCurrentDamageParameter_PassesCurrentDamage()
    {
        // Arrange
        var mockMathEngine = new Mock<IMathEngine>();
        var mockExpression = new MathExpression(50f);
        
        Dictionary<string, float>? capturedParams = null;
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "DAMAGE_REDUCTION",
            100f,
            It.IsAny<Dictionary<string, float>>()))
            .Callback<string, float, Dictionary<string, float>>((formula, input, parameters) => 
            {
                capturedParams = parameters;
            })
            .Returns(mockExpression);

        var bucket = new BucketDefinition
        {
            BucketId = "formula_current_damage",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.APPLY_FORMULA,
                    Source = "formula:DAMAGE_REDUCTION",
                    Parameters = new Dictionary<string, object>
                    {
                        { "damage", "current_damage" }
                    }
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object);
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.NotNull(capturedParams);
        Assert.Equal(100f, capturedParams["damage"]);
    }

    // ==================== ROLL_CRIT_TIER OPERATION ====================

    [Fact]
    public void RollCritTier_WithGuaranteedCrit_AppliesCritMultiplier()
    {
        // Arrange
        var mockMathEngine = new Mock<IMathEngine>();
        var mockRandomProvider = DamageTestHelpers.CreateMockRandomProvider(0.99); // 99% - não passa no check de 50%
        
        // Setup para CRIT_GUARANTEED_TIER (floor(150 / 100) = 1)
        var guaranteedTierExpr = new MathExpression(1f);
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "CRIT_GUARANTEED_TIER",
            It.IsAny<float>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns(guaranteedTierExpr);
        
        // Setup para CRIT_EXTRA_CHANCE (150 % 100 = 50)
        var extraChanceExpr = new MathExpression(50f);
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "CRIT_EXTRA_CHANCE",
            It.IsAny<float>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns(extraChanceExpr);
        
        // Setup para CRIT_DAMAGE_MULTIPLIER (1 + 1 * (2.0 - 1) = 2.0)
        var critMultExpr = new MathExpression(2.0f);
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "CRIT_DAMAGE_MULTIPLIER",
            It.IsAny<float>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns(critMultExpr);

        var bucket = new BucketDefinition
        {
            BucketId = "crit_test",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ROLL_CRIT_TIER
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, mockRandomProvider.Object);
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> 
            { 
                { "crit_chance", 150f },
                { "crit_multiplier", 2.0f }
            }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(200f, result.CurrentDamage); // 100 * 2.0
        Assert.True(result.Metadata.ContainsKey("crit_tier"));
        Assert.Equal(1, result.Metadata["crit_tier"]);
    }

    [Fact]
    public void RollCritTier_WithExtraTierRoll_IncrementsTier()
    {
        // Arrange
        var mockMathEngine = new Mock<IMathEngine>();
        var mockRandomProvider = DamageTestHelpers.CreateMockRandomProvider(0.3); // 30% roll, passa se extra_chance >= 30
        
        // Setup para CRIT_GUARANTEED_TIER (floor(150 / 100) = 1)
        var guaranteedTierExpr = new MathExpression(1f);
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "CRIT_GUARANTEED_TIER",
            It.IsAny<float>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns(guaranteedTierExpr);
        
        // Setup para CRIT_EXTRA_CHANCE (150 % 100 = 50, que é > 30)
        var extraChanceExpr = new MathExpression(50f);
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "CRIT_EXTRA_CHANCE",
            It.IsAny<float>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns(extraChanceExpr);
        
        // Setup para CRIT_DAMAGE_MULTIPLIER com tier 2 (1 + 2 * (2.0 - 1) = 3.0)
        var critMultExpr = new MathExpression(3.0f);
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "CRIT_DAMAGE_MULTIPLIER",
            It.IsAny<float>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns(critMultExpr);

        var bucket = new BucketDefinition
        {
            BucketId = "crit_extra_tier",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ROLL_CRIT_TIER
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, mockRandomProvider.Object);
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> 
            { 
                { "crit_chance", 150f },
                { "crit_multiplier", 2.0f }
            }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(300f, result.CurrentDamage); // 100 * 3.0
        Assert.Equal(2, result.Metadata["crit_tier"]);
    }

    [Fact]
    public void RollCritTier_WithNoCritChance_NoMultiplier()
    {
        // Arrange
        var mockMathEngine = new Mock<IMathEngine>();
        var mockRandomProvider = DamageTestHelpers.CreateMockRandomProvider(0.5);
        
        // Setup para tier 0
        var guaranteedTierExpr = new MathExpression(0f);
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "CRIT_GUARANTEED_TIER",
            It.IsAny<float>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns(guaranteedTierExpr);
        
        var extraChanceExpr = new MathExpression(0f);
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "CRIT_EXTRA_CHANCE",
            It.IsAny<float>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns(extraChanceExpr);
        
        // Setup para multiplicador 1.0 (sem crítico)
        var critMultExpr = new MathExpression(1.0f);
        mockMathEngine.Setup(m => m.BuildFromFormula(
            "CRIT_DAMAGE_MULTIPLIER",
            It.IsAny<float>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns(critMultExpr);

        var bucket = new BucketDefinition
        {
            BucketId = "no_crit",
            Order = 1,
            Operations = new List<BucketOperation>
            {
                new BucketOperation
                {
                    Type = OperationType.ROLL_CRIT_TIER
                }
            }
        };
        
        var processor = new GenericBucketProcessor(bucket, mockMathEngine.Object, _mockEventBus.Object, _mockLogger.Object, mockRandomProvider.Object);
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> 
            { 
                { "crit_chance", 0f },
                { "crit_multiplier", 2.0f }
            }
        );

        // Act
        var result = processor.Process(context);

        // Assert
        Assert.Equal(100f, result.CurrentDamage); // Sem mudança
        Assert.Equal(0, result.Metadata["crit_tier"]);
    }

    // ==================== MULTIPLE OPERATIONS ====================

    [Fact]
    public void MultipleOperations_AppliedInOrder()
    {
        // Arrange
        var bucket = new BucketDefinition
        {
            BucketId = "multi_op_test",
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
                    Source = "constant:2"
                },
                new BucketOperation
                {
                    Type = OperationType.SET_TAG,
                    Source = "tag:boosted"
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
        Assert.Contains("boosted", result.Tags);
    }
}
