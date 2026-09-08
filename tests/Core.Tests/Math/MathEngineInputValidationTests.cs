using M = Core.Math;
using Core.Config;
using Core.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Moq;

namespace Core.Tests.Math
{
    public class MathEngineInputValidationTests
    {
        private readonly M.MathEngine _engine;
        private readonly Mock<IConfigManager> _mockConfigManager;
        private readonly Mock<IResourceLoader> _mockResourceLoader;

        public MathEngineInputValidationTests()
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

        [Fact]
        public void BuildFromFormula_OperationWithValueAndOperands_ThrowsInvalidOperationException()
        {
            var engine = CreateEngineWithFormulas("""
            {
              "BAD_MIXED_MODE": {
                "description": "Invalid formula using both implicit and explicit modes.",
                "params": { "BONUS": 2 },
                "operations": [
                  { "op": "ADD", "value": "params.BONUS", "operands": ["$current", "params.BONUS"] }
                ]
              }
            }
            """);

            var ex = Assert.Throws<InvalidOperationException>(() => engine.BuildFromFormula("BAD_MIXED_MODE", 10f));
            Assert.Contains("cannot have both 'value' and 'operands'", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_AddWithoutValue_ThrowsInvalidOperationException()
        {
            var engine = CreateEngineWithFormulas("""
            {
              "ADD_WITHOUT_VALUE": {
                "description": "Invalid formula missing required ADD value.",
                "params": {},
                "operations": [
                  { "op": "ADD" }
                ]
              }
            }
            """);

            var ex = Assert.Throws<InvalidOperationException>(() => engine.BuildFromFormula("ADD_WITHOUT_VALUE", 10f));
            Assert.Contains("requires a 'value' field", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_ClampWithoutMax_ThrowsInvalidOperationException()
        {
            var engine = CreateEngineWithFormulas("""
            {
              "CLAMP_WITHOUT_MAX": {
                "description": "Invalid formula missing clamp max.",
                "params": { "MINIMUM": 0 },
                "operations": [
                  { "op": "CLAMP", "min": "params.MINIMUM" }
                ]
              }
            }
            """);

            var ex = Assert.Throws<InvalidOperationException>(() => engine.BuildFromFormula("CLAMP_WITHOUT_MAX", 10f));
            Assert.Contains("requires a 'max' field", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_ReferencesUndefinedParameter_ThrowsInvalidOperationException()
        {
            var engine = CreateEngineWithFormulas("""
            {
              "MISSING_PARAM": {
                "description": "Invalid formula referencing an undefined parameter.",
                "params": { "KNOWN": 1 },
                "operations": [
                  { "op": "MULTIPLY", "value": "params.UNKNOWN" }
                ]
              }
            }
            """);

            var ex = Assert.Throws<InvalidOperationException>(() => engine.BuildFromFormula("MISSING_PARAM", 10f));
            Assert.Contains("undefined parameter 'UNKNOWN'", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("SYSTEM")]
        [InlineData("EVAL")]
        [InlineData("Process.Start")]
        public void BuildFromFormula_UnsupportedOperation_ThrowsInvalidOperationException(string operation)
        {
            var engine = CreateEngineWithFormulas($$"""
            {
              "UNSUPPORTED_OPERATION": {
                "description": "Invalid formula using unsupported operation.",
                "params": {},
                "operations": [
                  { "op": "{{operation}}", "value": "1" }
                ]
              }
            }
            """);

            var ex = Assert.Throws<InvalidOperationException>(() => engine.BuildFromFormula("UNSUPPORTED_OPERATION", 10f));
            Assert.Contains("Unknown operation", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromFormula_DivideParameterZero_ThrowsInvalidOperationException()
        {
            var engine = CreateEngineWithFormulas("""
            {
              "DIVIDE_BY_PARAM": {
                "description": "Invalid formula dividing by a zero parameter.",
                "params": { "DIVISOR": 0 },
                "operations": [
                  { "op": "DIVIDE", "value": "params.DIVISOR" }
                ]
              }
            }
            """);

            var ex = Assert.Throws<InvalidOperationException>(() => engine.BuildFromFormula("DIVIDE_BY_PARAM", 10f));
            Assert.Contains("would cause division by zero", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static M.MathEngine CreateEngineWithFormulas(string formulasJson)
        {
            var configManager = new Mock<IConfigManager>();
            configManager.Setup(m => m.CurrentConfig).Returns("test");
            configManager.Setup(m => m.ResolveInheritanceChain(It.IsAny<string>()))
                .Returns(new List<string> { "test" });

            var resourceLoader = new Mock<IResourceLoader>();
            resourceLoader.Setup(m => m.LoadResource(
                    "formulas/math_formulas.json",
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<bool>()))
                .Returns(ParseResource(formulasJson));

            var formulaLoader = new M.FormulaLoader(resourceLoader.Object);
            var logger = new Mock<ILogger>();

            return new M.MathEngine(configManager.Object, formulaLoader, logger.Object);
        }

        private static Dictionary<string, JsonElement> ParseResource(string json)
        {
            using var document = JsonDocument.Parse(json);
            var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.Clone();
            }

            return result;
        }
    }
}
