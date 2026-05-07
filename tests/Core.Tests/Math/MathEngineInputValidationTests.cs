using M = Core.Math;
using System;
using System.Collections.Generic;
using Xunit;

namespace Core.Tests.Math
{
    public class MathEngineInputValidationTests
    {
        private readonly M.MathEngine _engine;

        public MathEngineInputValidationTests()
        {
            _engine = new M.MathEngine();
        }

        // ========================================
        // FASE 1: VALIDAÇÕES BÁSICAS
        // ========================================

        [Fact]
        public void BuildFromFormula_WithNullFormulaName_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula(null!, 10f));
            Assert.Contains("Formula name cannot be null or empty", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_WithEmptyFormulaName_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("", 10f));
            Assert.Contains("Formula name cannot be null or empty", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_WithWhitespaceFormulaName_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("   ", 10f));
            Assert.Contains("Formula name cannot be null or empty", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_WithNaNInputValue_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("LINEAR_ADDITIVE", float.NaN));
            Assert.Contains("cannot be NaN", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_WithPositiveInfinityInputValue_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("LINEAR_ADDITIVE", float.PositiveInfinity));
            Assert.Contains("cannot be Infinity", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_WithNegativeInfinityInputValue_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("LINEAR_ADDITIVE", float.NegativeInfinity));
            Assert.Contains("cannot be Infinity", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_WithNaNInParamOverrides_ThrowsArgumentException()
        {
            var paramOverrides = new Dictionary<string, float> { { "ADDITIVE_VALUE", float.NaN } };
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("LINEAR_ADDITIVE", 10f, paramOverrides));
            Assert.Contains("cannot be NaN", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_WithInfinityInParamOverrides_ThrowsArgumentException()
        {
            var paramOverrides = new Dictionary<string, float> { { "MULTIPLIER", float.PositiveInfinity } };
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("MULTIPLICATIVE_BUFF", 10f, paramOverrides));
            Assert.Contains("cannot be Infinity", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_WithValidInputs_Succeeds()
        {
            var expr = _engine.BuildFromFormula("LINEAR_ADDITIVE", 10f);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_WithValidInputsAndOverrides_Succeeds()
        {
            var paramOverrides = new Dictionary<string, float> { { "ADDITIVE_VALUE", 20f } };
            var expr = _engine.BuildFromFormula("LINEAR_ADDITIVE", 10f, paramOverrides);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        // ========================================
        // FASE 2: VALIDAÇÕES CONTEXTUAIS
        // ========================================

        [Fact]
        public void BuildFromFormula_SqrtWithNegativeInput_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("SQUARE_ROOT_SCALING", -5f));
            Assert.Contains("SQRT requires non-negative input", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_SqrtWithZeroInput_Succeeds()
        {
            var expr = _engine.BuildFromFormula("SQUARE_ROOT_SCALING", 0f);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_SqrtWithPositiveInput_Succeeds()
        {
            var expr = _engine.BuildFromFormula("SQUARE_ROOT_SCALING", 25f);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_LogWithZeroInput_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("LOGARITHMIC_SCALING", 0f));
            Assert.Contains("LOG requires positive input", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_LogWithNegativeInput_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _engine.BuildFromFormula("LOGARITHMIC_SCALING", -5f));
            Assert.Contains("LOG requires positive input", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_LogWithPositiveInput_Succeeds()
        {
            var expr = _engine.BuildFromFormula("LOGARITHMIC_SCALING", 10f);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_DivideInverse_ValidationOnlyChecksFirstOperation()
        {
            // Note: This test documents that BuildFromFormula performs simulation validation
            // HYPERBOLIC_CURVE with input=-100 will cause division by zero during simulation
            // The validation happens during BuildFromFormula, not just during Build()
            
            // BuildFromFormula will throw DivideByZeroException during simulation
            Assert.Throws<DivideByZeroException>(() => 
                _engine.BuildFromFormula("HYPERBOLIC_CURVE", -100f));
        }

        [Fact]
        public void BuildFromFormula_DivideInverseWithNonZeroInput_Succeeds()
        {
            var expr = _engine.BuildFromFormula("HYPERBOLIC_CURVE", 10f);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_ParameterUsedInDivideInverseWithZeroValue_ThrowsInvalidOperationException()
        {
            var paramOverrides = new Dictionary<string, float> { { "SCALING_VALUE", 0f } };
            var ex = Assert.Throws<InvalidOperationException>(() => _engine.BuildFromFormula("HYPERBOLIC_CURVE", 10f, paramOverrides));
            Assert.Contains("would cause division by zero", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_ParameterUsedInDivideWithNonZeroValue_Succeeds()
        {
            var paramOverrides = new Dictionary<string, float> { { "BASE_VALUE", 50f } };
            var expr = _engine.BuildFromFormula("HYPERBOLIC_CURVE", 10f, paramOverrides);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_FormulaNotStartingWithProblematicOperation_Succeeds()
        {
            var expr = _engine.BuildFromFormula("LINEAR_ADDITIVE", -100f);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_NegativeInputWithFormulaDoingAbsFirst_Succeeds()
        {
            var expr = _engine.BuildFromFormula("MULTIPLICATIVE_BUFF", -10f);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        // ========================================
        // TESTES DE EDGE CASES
        // ========================================

        [Fact]
        public void BuildFromFormula_VerySmallPositiveInput_Succeeds()
        {
            var expr = _engine.BuildFromFormula("LOGARITHMIC_SCALING", 0.0001f);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_VeryLargeInput_Succeeds()
        {
            var expr = _engine.BuildFromFormula("LINEAR_ADDITIVE", 1000000f);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_MultipleValidOverrides_Succeeds()
        {
            var paramOverrides = new Dictionary<string, float>
            {
                { "SCALING_VALUE", 200f },
                { "BASE_VALUE", 150f }
            };
            var expr = _engine.BuildFromFormula("HYPERBOLIC_CURVE", 10f, paramOverrides);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }

        [Fact]
        public void BuildFromFormula_OverrideOfNonExistentParameter_Succeeds()
        {
            var paramOverrides = new Dictionary<string, float> { { "NON_EXISTENT_PARAM", 100f } };
            var expr = _engine.BuildFromFormula("LINEAR_ADDITIVE", 10f, paramOverrides);
            var result = expr.Build();
            
            Assert.False(float.IsNaN(result));
            Assert.False(float.IsInfinity(result));
        }
    }
}
