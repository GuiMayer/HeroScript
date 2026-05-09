using Core.Logging;
using Core.Math;
using Xunit;

namespace Core.Tests.Math;

public class OperationMetadataProviderTests
{
    private readonly OperationMetadataProvider _provider;

    public OperationMetadataProviderTests()
    {
        var logger = NullLogger.Instance;
        _provider = new OperationMetadataProvider(logger);
    }

    [Fact]
    public void GetAllOperations_ReturnsNonEmptyList()
    {
        // Act
        var operations = _provider.GetAllOperations();

        // Assert
        Assert.NotNull(operations);
        Assert.NotEmpty(operations);
        Assert.True(operations.Count >= 15); // Should have at least basic operations
    }

    [Fact]
    public void GetOperation_WithValidName_ReturnsSuccess()
    {
        // Act
        var result = _provider.GetOperation("ADD");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ADD", result.Value.Name);
        Assert.Equal("+", result.Value.Symbol);
        Assert.Equal("basic", result.Value.Category);
    }

    [Fact]
    public void GetOperation_WithInvalidName_ReturnsFailure()
    {
        // Act
        var result = _provider.GetOperation("NONEXISTENT");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public void GetOperation_WithEmptyName_ReturnsFailure()
    {
        // Act
        var result = _provider.GetOperation("");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be empty", result.Error);
    }

    [Fact]
    public void GetOperation_IsCaseInsensitive()
    {
        // Act
        var result1 = _provider.GetOperation("ADD");
        var result2 = _provider.GetOperation("add");
        var result3 = _provider.GetOperation("Add");

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.True(result3.IsSuccess);
        Assert.Equal(result1.Value.Name, result2.Value.Name);
        Assert.Equal(result1.Value.Name, result3.Value.Name);
    }

    [Fact]
    public void GetOperationsByCategory_ReturnsGroupedOperations()
    {
        // Act
        var grouped = _provider.GetOperationsByCategory();

        // Assert
        Assert.NotNull(grouped);
        Assert.NotEmpty(grouped);
        Assert.True(grouped.ContainsKey("basic"));
        Assert.True(grouped.ContainsKey("advanced"));
        Assert.NotEmpty(grouped["basic"]);
    }

    [Fact]
    public void GetAllOperations_ContainsUnaryOperations()
    {
        // Act
        var operations = _provider.GetAllOperations();
        var unaryOps = operations.Where(op => op.IsUnary).ToList();

        // Assert
        Assert.NotEmpty(unaryOps);
        Assert.Contains(unaryOps, op => op.Name == "SQRT");
        Assert.Contains(unaryOps, op => op.Name == "ABS");
        Assert.Contains(unaryOps, op => op.Name == "NEGATE");
    }

    [Fact]
    public void GetAllOperations_ContainsAccumulatorOperations()
    {
        // Act
        var operations = _provider.GetAllOperations();
        var accumulatorOps = operations.Where(op => op.Behavior == "accumulator").ToList();

        // Assert
        Assert.NotEmpty(accumulatorOps);
        Assert.Contains(accumulatorOps, op => op.Name == "ADD");
        Assert.Contains(accumulatorOps, op => op.Name == "MULTIPLY");
    }

    [Fact]
    public void OperationMetadata_HasRequiredFields()
    {
        // Act
        var result = _provider.GetOperation("ADD");

        // Assert
        Assert.True(result.IsSuccess);
        var operation = result.Value;
        Assert.False(string.IsNullOrEmpty(operation.Name));
        Assert.False(string.IsNullOrEmpty(operation.Symbol));
        Assert.False(string.IsNullOrEmpty(operation.Description));
        Assert.False(string.IsNullOrEmpty(operation.Category));
        Assert.False(string.IsNullOrEmpty(operation.Behavior));
    }
}
