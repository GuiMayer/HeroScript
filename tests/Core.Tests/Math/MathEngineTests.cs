using M = Core.Math;
using Core.Config;
using Core.Logging;
using System.Collections.Generic;
using Xunit;
using Moq;

namespace Core.Tests.Math
{
    public class MathEngineTests
    {
        private readonly M.MathEngine _engine;
        private readonly Mock<IConfigManager> _mockConfigManager;
        private readonly Mock<IResourceLoader> _mockResourceLoader;

        public MathEngineTests()
        {
            // Setup mock config manager
            _mockConfigManager = new Mock<IConfigManager>();
            _mockConfigManager.Setup(m => m.CurrentConfig).Returns("default");
            _mockConfigManager.Setup(m => m.GetConfigPath(It.IsAny<string>())).Returns("configs/default");
            _mockConfigManager.Setup(m => m.ResolveInheritanceChain(It.IsAny<string>()))
                .Returns(new List<string> { "default" });

            // Setup mock resource loader to return formula data from JSON file
            _mockResourceLoader = new Mock<IResourceLoader>();
            var formulasPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Resources", "formulas", "math_formulas.json");
            var formulasJson = System.IO.File.ReadAllText(formulasPath);
            var formulasDoc = System.Text.Json.JsonDocument.Parse(formulasJson);
            var formulasDict = new Dictionary<string, System.Text.Json.JsonElement>();
            foreach (var prop in formulasDoc.RootElement.EnumerateObject())
            {
                formulasDict[prop.Name] = prop.Value;
            }
            _mockResourceLoader.Setup(m => m.LoadResource(
                "formulas/math_formulas.json",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<bool>()))
                .Returns(formulasDict);

            // Create formula loader with mock resource loader
            var formulaLoader = new M.FormulaLoader(_mockResourceLoader.Object);
            
            // Create mock logger
            var mockLogger = new Mock<ILogger>();
            
            // Create engine with dependencies
            _engine = new M.MathEngine(_mockConfigManager.Object, formulaLoader, mockLogger.Object);
        }

        [Fact]
        public void BuildFromFormula_LinearAdditive_ReturnsCorrectResult()
        {
            // Arrange: input + 10
            float input = 5;
            float expected = 15;

            // Act
            var expr = _engine.BuildFromFormula("LINEAR_ADDITIVE", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_MultiplicativeBuff_ReturnsCorrectResult()
        {
            // Arrange: input * 1.5
            float input = 10;
            float expected = 15;

            // Act
            var expr = _engine.BuildFromFormula("MULTIPLICATIVE_BUFF", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_HyperbolicCurve_ReturnsCorrectResult()
        {
            // Arrange: (input + 100) / 100 => (50 + 100) = 150, then 100 / 150 = 0.6667
            float input = 50;
            float expected = 0.6667f;

            // Act
            var expr = _engine.BuildFromFormula("HYPERBOLIC_CURVE", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 3);
        }

        [Fact]
        public void BuildFromFormula_ExponentialScaling_ReturnsCorrectResult()
        {
            // Arrange: (input * 2) ^ 2 => (3 * 2) = 6, then 6^2 = 36
            float input = 3;
            float expected = 36;

            // Act
            var expr = _engine.BuildFromFormula("EXPONENTIAL_SCALING", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_LogarithmicScaling_ReturnsCorrectResult()
        {
            // Arrange: log(input) * 10 => log(2.718281828) ≈ 1, then 1 * 10 = 10
            float input = 2.718281828f;
            float expected = 10f;

            // Act
            var expr = _engine.BuildFromFormula("LOGARITHMIC_SCALING", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 1);
        }

        [Fact]
        public void BuildFromFormula_SigmoidCurve_ReturnsCorrectResult()
        {
            // Arrange: At midpoint (50), should return approximately MAX_VALUE/2 = 50
            float input = 50;
            float expected = 50f;

            // Act
            var expr = _engine.BuildFromFormula("SIGMOID_CURVE", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 0);
        }

        [Fact]
        public void BuildFromFormula_ClampSaturation_WithHighValue_ClampsToMax()
        {
            // Arrange: clamp input between 0 and 999
            float input = 1500;
            float expected = 999;

            // Act
            var expr = _engine.BuildFromFormula("CLAMP_SATURATION", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_ClampSaturation_WithNegativeValue_ClampsToMin()
        {
            // Arrange: clamp input between 0 and 999
            float input = -50;
            float expected = 0;

            // Act
            var expr = _engine.BuildFromFormula("CLAMP_SATURATION", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_ClampSaturation_WithValidValue_ReturnsValue()
        {
            // Arrange: clamp input between 0 and 999
            float input = 500;
            float expected = 500;

            // Act
            var expr = _engine.BuildFromFormula("CLAMP_SATURATION", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_LifeStealConversion_ReturnsCorrectResult()
        {
            // Arrange: input * 0.2
            float input = 100;
            float expected = 20;

            // Act
            var expr = _engine.BuildFromFormula("LIFE_STEAL_CONVERSION", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_ReflectDamage_ReturnsCorrectResult()
        {
            // Arrange: input * 0.15
            float input = 100;
            float expected = 15;

            // Act
            var expr = _engine.BuildFromFormula("REFLECT_DAMAGE", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_StepFunctionMana_ReturnsCorrectResult()
        {
            // Arrange: input / 10 * 1 => 35 / 10 = 3.5, then 3.5 * 1 = 3.5
            float input = 35;
            float expected = 3.5f;

            // Act
            var expr = _engine.BuildFromFormula("STEP_FUNCTION_MANA", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_ArmorReduction_ReturnsCorrectResult()
        {
            // Arrange: damage × (1 - armor / (armor + 2 × damage))
            // With ARMOR=50, DAMAGE=100: 100 × (1 - 50 / (50 + 200)) = 100 × (1 - 50/250) = 100 × 0.8 = 80
            float input = 0; // Input is ignored, formula uses params
            var paramOverrides = new Dictionary<string, float>
            {
                { "ARMOR", 50 },
                { "DAMAGE", 100 }
            };
            float expected = 80f;

            // Act
            var expr = _engine.BuildFromFormula("ARMOR_REDUCTION", input, paramOverrides);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_PercentageBasedDamage_ReturnsCorrectResult()
        {
            // Arrange: BASE_DAMAGE * PERCENTAGE => 100 * 0.5 = 50 (ignores input)
            float input = 50;
            float expected = 50;

            // Act
            var expr = _engine.BuildFromFormula("PERCENTAGE_BASED_DAMAGE", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_CriticalMultiplier_ReturnsCorrectResult()
        {
            // Arrange: input * 2.0 + 50 => 100 * 2.0 + 50 = 250
            float input = 100;
            float expected = 250;

            // Act
            var expr = _engine.BuildFromFormula("CRITICAL_MULTIPLIER", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_CompoundScaling_ReturnsCorrectResult()
        {
            // Arrange: input * 1.5 + 25 => 10 * 1.5 + 25 = 40
            float input = 10;
            float expected = 40;

            // Act
            var expr = _engine.BuildFromFormula("COMPOUND_SCALING", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_InverseCompoundScaling_ReturnsCorrectResult()
        {
            // Arrange: (input + 25) * 1.5 => (10 + 25) * 1.5 = 52.5
            float input = 10;
            float expected = 52.5f;

            // Act
            var expr = _engine.BuildFromFormula("INVERSE_COMPOUND_SCALING", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_SquareRootScaling_ReturnsCorrectResult()
        {
            // Arrange: sqrt(input) * 10 => sqrt(16) * 10 = 40
            float input = 16;
            float expected = 40;

            // Act
            var expr = _engine.BuildFromFormula("SQUARE_ROOT_SCALING", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_ThresholdBonus_WithHighValue_ReturnsCorrectResult()
        {
            // Arrange: (input - 100) clamped [0, 9999] * 1.25 + 100
            // (150-100) = 50, clamped = 50, 50*1.25 = 62.5, 62.5+100 = 162.5
            float input = 150;
            float expected = 162.5f;

            // Act
            var expr = _engine.BuildFromFormula("THRESHOLD_BONUS", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_ThresholdBonus_WithLowValue_ReturnsBaseValue()
        {
            // Arrange: (input - 100) clamped [0, 9999] * 1.25 + 100
            // (50-100) = -50, clamped = 0, 0*1.25 = 0, 0+100 = 100
            float input = 50;
            float expected = 100;

            // Act
            var expr = _engine.BuildFromFormula("THRESHOLD_BONUS", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_WithCustomParameters_ReturnsCorrectResult()
        {
            // Arrange: LINEAR_ADDITIVE with custom FLAT_BONUS = 20
            float input = 5;
            float expected = 25;
            var customParams = new Dictionary<string, float> { { "FLAT_BONUS", 20 } };

            // Act
            var expr = _engine.BuildFromFormula("LINEAR_ADDITIVE", input, customParams);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void BuildFromFormula_WithInvalidFormulaName_ThrowsException()
        {
            // Arrange
            string invalidFormula = "FORMULA_INVALIDA";
            float input = 10;

            // Act & Assert
            Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula(invalidFormula, input));
        }

        // ===== FLOOR Operator Tests =====

        [Theory]
        [InlineData(5.9f, 5f)]
        [InlineData(5.1f, 5f)]
        [InlineData(5.0f, 5f)]
        [InlineData(-5.1f, -6f)]
        [InlineData(-5.9f, -6f)]
        [InlineData(0.9f, 0f)]
        public void Floor_WithVariousInputs_ReturnsCorrectResult(float input, float expected)
        {
            // Arrange & Act
            var expr = new M.MathExpression(input);
            expr.Floor();
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        // ===== MODULO Operator Tests =====

        [Theory]
        [InlineData(10f, 3f, 1f)]
        [InlineData(150f, 100f, 50f)]
        [InlineData(250f, 100f, 50f)]
        [InlineData(99f, 100f, 99f)]
        [InlineData(7.5f, 2.5f, 0f)]
        public void Modulo_WithVariousInputs_ReturnsCorrectResult(float dividend, float divisor, float expected)
        {
            // Arrange & Act
            var expr = new M.MathExpression(dividend);
            expr.Modulo(divisor);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 2);
        }

        [Fact]
        public void Modulo_WithZeroDivisor_ThrowsArgumentException()
        {
            // Arrange
            var expr = new M.MathExpression(10f);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => expr.Modulo(0f));
        }

        // ===== Critical System Formula Tests =====

        [Theory]
        [InlineData(0f, 0)]      // 0% crit = tier 0
        [InlineData(50f, 0)]     // 50% crit = tier 0
        [InlineData(99f, 0)]     // 99% crit = tier 0
        [InlineData(100f, 1)]    // 100% crit = tier 1
        [InlineData(150f, 1)]    // 150% crit = tier 1
        [InlineData(200f, 2)]    // 200% crit = tier 2
        [InlineData(250f, 2)]    // 250% crit = tier 2
        [InlineData(300f, 3)]    // 300% crit = tier 3
        public void BuildFromFormula_CritGuaranteedTier_ReturnsCorrectTier(float critChance, int expectedTier)
        {
            // Arrange
            var customParams = new Dictionary<string, float> { { "CRIT_CHANCE", critChance } };

            // Act
            var expr = _engine.BuildFromFormula("CRIT_GUARANTEED_TIER", 0f, customParams);
            float result = expr.Build();

            // Assert
            Assert.Equal(expectedTier, (int)result);
        }

        [Theory]
        [InlineData(0f, 0f)]      // 0% crit = 0% extra chance
        [InlineData(50f, 50f)]    // 50% crit = 50% extra chance
        [InlineData(99f, 99f)]    // 99% crit = 99% extra chance
        [InlineData(100f, 0f)]    // 100% crit = 0% extra chance (full tier)
        [InlineData(150f, 50f)]   // 150% crit = 50% extra chance
        [InlineData(199f, 99f)]   // 199% crit = 99% extra chance
        [InlineData(200f, 0f)]    // 200% crit = 0% extra chance (full tier)
        [InlineData(250f, 50f)]   // 250% crit = 50% extra chance
        public void BuildFromFormula_CritExtraChance_ReturnsCorrectChance(float critChance, float expectedChance)
        {
            // Arrange
            var customParams = new Dictionary<string, float> { { "CRIT_CHANCE", critChance } };

            // Act
            var expr = _engine.BuildFromFormula("CRIT_EXTRA_CHANCE", 0f, customParams);
            float result = expr.Build();

            // Assert
            Assert.Equal(expectedChance, result, precision: 2);
        }

        [Theory]
        [InlineData(0, 2.0f, 1.0f)]    // Tier 0 = 1x damage (no crit)
        [InlineData(1, 2.0f, 2.0f)]    // Tier 1 = 2x damage
        [InlineData(2, 2.0f, 3.0f)]    // Tier 2 = 3x damage
        [InlineData(3, 2.0f, 4.0f)]    // Tier 3 = 4x damage
        [InlineData(1, 1.5f, 1.5f)]    // Tier 1 with 1.5x mult = 1.5x damage
        [InlineData(2, 1.5f, 2.0f)]    // Tier 2 with 1.5x mult = 2x damage
        [InlineData(1, 3.0f, 3.0f)]    // Tier 1 with 3x mult = 3x damage
        [InlineData(2, 3.0f, 5.0f)]    // Tier 2 with 3x mult = 5x damage
        public void BuildFromFormula_CritDamageMultiplier_ReturnsCorrectMultiplier(int tier, float critMult, float expectedMultiplier)
        {
            // Arrange
            var customParams = new Dictionary<string, float> 
            { 
                { "CRIT_TIER", tier },
                { "CRIT_MULT", critMult }
            };

            // Act
            var expr = _engine.BuildFromFormula("CRIT_DAMAGE_MULTIPLIER", 0f, customParams);
            float result = expr.Build();

            // Assert
            Assert.Equal(expectedMultiplier, result, precision: 2);
        }

        // ===== Integration Test: Full Critical Calculation =====

        [Fact]
        public void CriticalSystem_FullCalculation_WorksCorrectly()
        {
            // Arrange: 250% crit chance, 2.0x multiplier, 100 base damage
            float critChance = 250f;
            float critMult = 2.0f;
            float baseDamage = 100f;

            // Act: Calculate guaranteed tier
            var tierExpr = _engine.BuildFromFormula(
                "CRIT_GUARANTEED_TIER",
                0f,
                new Dictionary<string, float> { { "CRIT_CHANCE", critChance } }
            );
            int guaranteedTier = (int)tierExpr.Build();

            // Act: Calculate extra chance
            var chanceExpr = _engine.BuildFromFormula(
                "CRIT_EXTRA_CHANCE",
                0f,
                new Dictionary<string, float> { { "CRIT_CHANCE", critChance } }
            );
            float extraChance = chanceExpr.Build();

            // Act: Calculate damage multiplier for guaranteed tier
            var multExpr = _engine.BuildFromFormula(
                "CRIT_DAMAGE_MULTIPLIER",
                0f,
                new Dictionary<string, float> 
                { 
                    { "CRIT_TIER", guaranteedTier },
                    { "CRIT_MULT", critMult }
                }
            );
            float damageMultiplier = multExpr.Build();

            // Act: Calculate final damage
            float finalDamage = baseDamage * damageMultiplier;

            // Assert
            Assert.Equal(2, guaranteedTier);           // 250 / 100 = 2
            Assert.Equal(50f, extraChance, precision: 2);  // 250 % 100 = 50
            Assert.Equal(3.0f, damageMultiplier, precision: 2); // 1 + (2 * (2 - 1)) = 3
            Assert.Equal(300f, finalDamage, precision: 2);      // 100 * 3 = 300
        }
    }
}
