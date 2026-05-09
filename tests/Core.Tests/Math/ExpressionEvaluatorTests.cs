using Core.Math;
using Xunit;

namespace Core.Tests.Math;

public class ExpressionEvaluatorTests
{
    private readonly ExpressionEvaluator _evaluator;

    public ExpressionEvaluatorTests()
    {
        _evaluator = new ExpressionEvaluator();
    }

    [Fact]
    public void Evaluate_UnaryOperation_ReturnsCorrectResult()
    {
        // Arrange
        var request = new ExpressionEvaluationRequest
        {
            InitialValue = 25f,
            Steps = new List<ExpressionStep>
            {
                new ExpressionStep { Operation = "SQRT" }
            }
        };

        // Act
        var result = _evaluator.Evaluate(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5f, result.Value.Result);
    }

    [Fact]
    public void Evaluate_ImplicitMode_ReturnsCorrectResult()
    {
        // Arrange
        var request = new ExpressionEvaluationRequest
        {
            InitialValue = 10f,
            Steps = new List<ExpressionStep>
            {
                new ExpressionStep { Operation = "ADD", Values = new[] { 5f } },
                new ExpressionStep { Operation = "MULTIPLY", Values = new[] { 2f } }
            }
        };

        // Act
        var result = _evaluator.Evaluate(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(30f, result.Value.Result); // (10 + 5) * 2 = 30
    }

    [Fact]
    public void Evaluate_LiteralMode_ReturnsCorrectResult()
    {
        // Arrange
        var request = new ExpressionEvaluationRequest
        {
            InitialValue = 0f,
            Steps = new List<ExpressionStep>
            {
                new ExpressionStep { Operation = "ADD", Operands = new List<string> { "10", "20" } }
            }
        };

        // Act
        var result = _evaluator.Evaluate(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(30f, result.Value.Result);
    }

    [Fact]
    public void Evaluate_SymbolicMode_ReturnsCorrectResult()
    {
        // Arrange
        var request = new ExpressionEvaluationRequest
        {
            InitialValue = 10f,
            Steps = new List<ExpressionStep>
            {
                new ExpressionStep { Operation = "ADD", Operands = new List<string> { "$current", "params.bonus" } }
            },
            Parameters = new Dictionary<string, float> { ["bonus"] = 5f }
        };

        // Act
        var result = _evaluator.Evaluate(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(25f, result.Value.Result); // 10 + 10 + 5 = 25
    }

    [Fact]
    public void Evaluate_MixedValuesAndOperands_ReturnsFailure()
    {
        // Arrange
        var request = new ExpressionEvaluationRequest
        {
            InitialValue = 10f,
            Steps = new List<ExpressionStep>
            {
                new ExpressionStep 
                { 
                    Operation = "ADD", 
                    Values = new[] { 5f },
                    Operands = new List<string> { "10" }
                }
            }
        };

        // Act
        var result = _evaluator.Evaluate(request);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot have both Values and Operands", result.Error);
    }

    [Fact]
    public void Evaluate_InvalidSymbolicOperand_ReturnsFailure()
    {
        // Arrange
        var request = new ExpressionEvaluationRequest
        {
            InitialValue = 10f,
            Steps = new List<ExpressionStep>
            {
                new ExpressionStep { Operation = "ADD", Operands = new List<string> { "params.nonexistent" } }
            },
            Parameters = new Dictionary<string, float>()
        };

        // Act
        var result = _evaluator.Evaluate(request);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Invalid operands", result.Error);
    }

    [Fact]
    public void Evaluate_InvalidLiteralOperand_ReturnsFailure()
    {
        // Arrange
        var request = new ExpressionEvaluationRequest
        {
            InitialValue = 10f,
            Steps = new List<ExpressionStep>
            {
                new ExpressionStep { Operation = "ADD", Operands = new List<string> { "not_a_number" } }
            }
        };

        // Act
        var result = _evaluator.Evaluate(request);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Invalid literal operand", result.Error);
    }

    [Fact]
    public void Evaluate_ComplexExpression_ReturnsCorrectResult()
    {
        // Arrange
        var request = new ExpressionEvaluationRequest
        {
            InitialValue = 100f,
            Steps = new List<ExpressionStep>
            {
                new ExpressionStep { Operation = "MULTIPLY", Values = new[] { 1.5f } },
                new ExpressionStep { Operation = "ADD", Operands = new List<string> { "params.bonus" } },
                new ExpressionStep { Operation = "FLOOR" }
            },
            Parameters = new Dictionary<string, float> { ["bonus"] = 25f }
        };

        // Act
        var result = _evaluator.Evaluate(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(175f, result.Value.Result); // floor((100 * 1.5) + 25) = floor(175) = 175
    }
}
