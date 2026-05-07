using Core.Math;
using System;
using System.Collections.Generic;
using Xunit;

namespace Core.Tests.Math
{
    /// <summary>
    /// Tests to validate the three API operation modes:
    /// 1. Implicit Mode (Values) - implicit accumulator
    /// 2. Explicit Literal Mode (numeric Operands) - fixed operands
    /// 3. Explicit Symbolic Mode (Operands with $current, params.X) - dynamic operands
    /// </summary>
    public class MathExpressionApiTests
    {
        [Fact]
        public void Mode1_ImplicitAccumulator_WithValues_ReturnsCorrectResult()
        {
            // Arrange: Simulates POST /api/math/expression/evaluate
            // { "initialValue": 10, "steps": [{ "operation": "ADD", "values": [5, 3] }] }
            var expression = new MathExpression(10);
            expression.AddRawStep("ADD", new float[] { 5, 3 });
            expression.AddRawStep("MULTIPLY", new float[] { 2 });

            // Act
            var result = expression.Build();

            // Assert: (10 + 5 + 3) * 2 = 36
            var expected = (10 + 5 + 3) * 2;
            Assert.Equal(expected, result, precision: 3);
        }

        [Fact]
        public void Mode2_ExplicitLiteral_WithNumericOperands_ReturnsCorrectResult()
        {
            // Arrange: Simulates POST /api/math/expression/evaluate
            // { "initialValue": 0, "steps": [{ "operation": "ADD", "operands": ["10", "20"] }] }
            var expression = new MathExpression(0);
            expression.AddRawStepWithOperands("ADD", new List<string> { "10", "20" });
            expression.AddRawStepWithOperands("MULTIPLY", new List<string> { "2" });

            // Act
            var result = expression.Build();

            // Assert: (0 + 10 + 20) * 2 = 60
            var expected = (0 + 10 + 20) * 2;
            Assert.Equal(expected, result, precision: 3);
        }

        [Fact]
        public void Mode3_ExplicitSymbolic_WithCurrentOperand_ReturnsCorrectResult()
        {
            // Arrange: Simulates POST /api/math/expression/evaluate
            // { "initialValue": 10, "steps": [{ "operation": "MULTIPLY", "operands": ["$current", "2"] }] }
            var initialValue = 10f;
            var parameters = new Dictionary<string, float>();

            // Simulate operand resolution
            var operand1 = MathEngine.ResolveOperandPublic("$current", parameters, initialValue, initialValue);
            var operand2 = MathEngine.ResolveOperandPublic("2", parameters, initialValue, initialValue);

            var expression = new MathExpression(initialValue);
            expression.AddRawStep("MULTIPLY", new float[] { operand1, operand2 });

            // Act
            var result = expression.Build();

            // Assert: 10 * 10 * 2 = 200
            var expected = 10 * 10 * 2;
            Assert.Equal(expected, result, precision: 3);
        }

        [Fact]
        public void Mode3_ExplicitSymbolic_WithParameterOperand_ReturnsCorrectResult()
        {
            // Arrange: Simulates POST /api/math/expression/evaluate
            // { "initialValue": 0, "parameters": {"BONUS": 50}, "steps": [{ "operation": "ADD", "operands": ["params.BONUS"] }] }
            var initialValue = 0f;
            var parameters = new Dictionary<string, float> { { "BONUS", 50 } };

            // Simulate operand resolution
            var operand = MathEngine.ResolveOperandPublic("params.BONUS", parameters, initialValue, initialValue);

            var expression = new MathExpression(initialValue);
            expression.AddRawStep("ADD", new float[] { operand });

            // Act
            var result = expression.Build();

            // Assert
            var expected = 50f;
            Assert.Equal(expected, result, precision: 3);
        }

        [Fact]
        public void Mode3_ExplicitSymbolic_LerpFormula_ReturnsCorrectResult()
        {
            // Arrange: Simulates POST /api/math/expression/evaluate
            // LERP formula: START + (TARGET - START) * T
            // {
            //   "initialValue": 0,
            //   "parameters": {"TARGET": 100, "START": 0, "T": 0.5},
            //   "steps": [
            //     { "operation": "ADD", "operands": ["params.TARGET"] },
            //     { "operation": "SUBTRACT", "operands": ["params.START"] },
            //     { "operation": "MULTIPLY", "operands": ["params.T"] },
            //     { "operation": "ADD", "operands": ["params.START"] }
            //   ]
            // }
            var initialValue = 0f;
            var parameters = new Dictionary<string, float>
            {
                { "TARGET", 100 },
                { "START", 0 },
                { "T", 0.5f }
            };

            var expression = new MathExpression(initialValue);
            float currentValue = initialValue;

            // Step 1: ADD params.TARGET → 0 + 100 = 100
            var op1 = MathEngine.ResolveOperandPublic("params.TARGET", parameters, initialValue, currentValue);
            expression.AddRawStep("ADD", new float[] { op1 });
            currentValue = MathEngine.SimulateOperationResult("ADD", currentValue, new float[] { op1 });

            // Step 2: SUBTRACT params.START → 100 - 0 = 100
            var op2 = MathEngine.ResolveOperandPublic("params.START", parameters, initialValue, currentValue);
            expression.AddRawStep("SUBTRACT", new float[] { op2 });
            currentValue = MathEngine.SimulateOperationResult("SUBTRACT", currentValue, new float[] { op2 });

            // Step 3: MULTIPLY params.T → 100 * 0.5 = 50
            var op3 = MathEngine.ResolveOperandPublic("params.T", parameters, initialValue, currentValue);
            expression.AddRawStep("MULTIPLY", new float[] { op3 });
            currentValue = MathEngine.SimulateOperationResult("MULTIPLY", currentValue, new float[] { op3 });

            // Step 4: ADD params.START → 50 + 0 = 50
            var op4 = MathEngine.ResolveOperandPublic("params.START", parameters, initialValue, currentValue);
            expression.AddRawStep("ADD", new float[] { op4 });

            // Act
            var result = expression.Build();

            // Assert: LERP(0, 100, 0.5) = 50
            var expected = 50f;
            Assert.Equal(expected, result, precision: 3);
        }

        [Fact]
        public void Validation_MissingParameters_ReturnsError()
        {
            // Arrange
            var operands = new List<string> { "params.MISSING" };

            // Act
            var errors = MathEngine.ValidateOperands(operands, null);

            // Assert
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Validation_InvalidOperand_ReturnsError()
        {
            // Arrange
            var operands = new List<string> { "invalid_operand" };

            // Act
            var errors = MathEngine.ValidateOperands(operands, new Dictionary<string, float>());

            // Assert
            Assert.NotEmpty(errors);
        }
    }
}
