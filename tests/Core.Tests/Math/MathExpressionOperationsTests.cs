using M = Core.Math;
using Xunit;

namespace Core.Tests.Math;

/// <summary>
/// xUnit tests for MathExpression basic operations
/// </summary>
public class MathExpressionOperationsTests
{
    [Fact]
    public void Min_WithSmallerValue_ShouldReturnSmallerValue()
    {
        // Arrange & Act
        var result = new M.MathExpression(10).Min(5).Build();

        // Assert
        Assert.Equal(5, result, precision: 4);
    }

    [Fact]
    public void Min_WithLargerValue_ShouldReturnCurrent()
    {
        // Arrange & Act
        var result = new M.MathExpression(10).Min(20).Build();

        // Assert
        Assert.Equal(10, result, precision: 4);
    }

    [Fact]
    public void Min_WithEqualValue_ShouldReturnValue()
    {
        // Arrange & Act
        var result = new M.MathExpression(10).Min(10).Build();

        // Assert
        Assert.Equal(10, result, precision: 4);
    }

    [Fact]
    public void Min_WithNegativeValues_ShouldReturnSmallest()
    {
        // Arrange & Act
        var result = new M.MathExpression(-5).Min(-10).Build();

        // Assert
        Assert.Equal(-10, result, precision: 4);
    }

    [Fact]
    public void Min_WithMultipleValues_ShouldReturnSmallest()
    {
        // Arrange & Act
        var result = new M.MathExpression(100).Min(50, 75, 25).Build();

        // Assert
        Assert.Equal(25, result, precision: 4);
    }

    [Fact]
    public void Min_WithZero_ShouldReturnZero()
    {
        // Arrange & Act
        var result = new M.MathExpression(10).Min(0).Build();

        // Assert
        Assert.Equal(0, result, precision: 4);
    }

    [Fact]
    public void Min_Chained_ShouldReturnSmallest()
    {
        // Arrange & Act
        var result = new M.MathExpression(100).Min(50).Min(75).Build();

        // Assert
        Assert.Equal(50, result, precision: 4);
    }

    [Fact]
    public void Min_WithNoValues_ShouldThrowInvalidOperationException()
    {
        // Arrange & Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new M.MathExpression(10).AddRawStep("MIN", Array.Empty<float>()).Build()
        );
    }

    [Fact]
    public void Divide_WithZero_ShouldThrowDivideByZeroException()
    {
        Assert.Throws<DivideByZeroException>(() =>
            new M.MathExpression(10).Divide(0).Build()
        );
    }

    [Fact]
    public void Multiply_WithOverflow_ShouldThrowInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new M.MathExpression(float.MaxValue).Multiply(2).Build()
        );

        Assert.Contains("Infinity", ex.Message);
    }

    [Fact]
    public void Pow_WithOverflow_ShouldThrowInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new M.MathExpression(float.MaxValue).Pow(2).Build()
        );

        Assert.Contains("Infinity", ex.Message);
    }

    [Fact]
    public void Abs_WithNegativeValue_ReturnsPositiveValue()
    {
        var result = new M.MathExpression(-15).Abs().Build();

        Assert.Equal(15, result, precision: 4);
    }

    [Fact]
    public void Negate_WithPositiveValue_ReturnsNegativeValue()
    {
        var result = new M.MathExpression(15).Negate().Build();

        Assert.Equal(-15, result, precision: 4);
    }

    [Fact]
    public void Max_WithNegativeValues_ReturnsLargestValue()
    {
        var result = new M.MathExpression(-20).Max(-10, -30).Build();

        Assert.Equal(-10, result, precision: 4);
    }

    [Fact]
    public void RoundAndCeil_ReturnExpectedValues()
    {
        var rounded = new M.MathExpression(10.456f).Round(2).Build();
        var ceiled = new M.MathExpression(10.1f).Ceil().Build();

        Assert.Equal(10.46f, rounded, precision: 2);
        Assert.Equal(11f, ceiled, precision: 4);
    }
}
