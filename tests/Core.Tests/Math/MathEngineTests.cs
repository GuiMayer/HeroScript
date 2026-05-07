using M = Core.Math;
using System.Collections.Generic;
using Xunit;

namespace Core.Tests.Math
{
    public class MathEngineTests
    {
        private readonly M.MathEngine _engine;

        public MathEngineTests()
        {
            _engine = new M.MathEngine();
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
            // Arrange: 1 / (input/100 + 1) => 50 / 100 = 0.5, 0.5 + 1 = 1.5, 1 / 1.5 = 0.6667
            float input = 50;
            float expected = 0.6667f;

            // Act
            var expr = _engine.BuildFromFormula("ARMOR_REDUCTION", input);
            float result = expr.Build();

            // Assert
            Assert.Equal(expected, result, precision: 3);
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
    }
}
