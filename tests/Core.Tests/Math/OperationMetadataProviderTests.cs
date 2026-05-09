using Core.Math;
using Xunit;

namespace Core.Tests.Math;

public class OperationMetadataProviderTests
{
    private readonly OperationMetadataProvider _provider;

    public OperationMetadataProviderTests()
    {
        _provider = new OperationMetadataProvider();
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
    public void GetOperation_WithValidName_ReturnsOperation()
    {
        // Act
        var operation = _provider.GetOperation("ADD");

        // Assert
        Assert.NotNull(operation);
        Assert.Equal("ADD", operation.Name);
        Assert.Equal("+", operation.Symbol);
        Assert.Equal("basic", operation.Category);
    }

    [Fact]
    public void GetOperation_WithInvalidName_ReturnsNull()
    {
        // Act
        var operation = _provider.GetOperation("NONEXISTENT");

        // Assert
        Assert.Null(operation);
    }

    [Fact]
    public void GetOperation_IsCaseInsensitive()
    {
        // Act
        var operation1 = _provider.GetOperation("ADD");
        var operation2 = _provider.GetOperation("add");
        var operation3 = _provider.GetOperation("Add");

        // Assert
        Assert.NotNull(operation1);
        Assert.NotNull(operation2);
        Assert.NotNull(operation3);
        Assert.Equal(operation1.Name, operation2.Name);
        Assert.Equal(operation1.Name, operation3.Name);
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
        var operation = _provider.GetOperation("ADD");

        // Assert
        Assert.NotNull(operation);
        Assert.False(string.IsNullOrEmpty(operation.Name));
        Assert.False(string.IsNullOrEmpty(operation.Symbol));
        Assert.False(string.IsNullOrEmpty(operation.Description));
        Assert.False(string.IsNullOrEmpty(operation.Category));
        Assert.False(string.IsNullOrEmpty(operation.Behavior));
    }
}
