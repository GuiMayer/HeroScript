using API.Controllers;
using API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public class OperationControllerTests
{
    private readonly Mock<ILogger<OperationController>> _mockLogger;
    private readonly OperationController _controller;

    public OperationControllerTests()
    {
        _mockLogger = new Mock<ILogger<OperationController>>();
        _controller = new OperationController(_mockLogger.Object);
    }

    [Fact]
    public void GetOperations_ReturnsOkResult_WithListOfOperations()
    {
        // Act
        var result = _controller.GetOperations();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var operations = Assert.IsAssignableFrom<List<OperationMetadataDto>>(okResult.Value);
        Assert.Equal(18, operations.Count); // Should have exactly 18 operations
    }

    [Fact]
    public void GetOperations_ContainsAllBasicOperations()
    {
        // Act
        var result = _controller.GetOperations();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var operations = Assert.IsAssignableFrom<List<OperationMetadataDto>>(okResult.Value);

        // Assert - Check for basic operations
        Assert.Contains(operations, op => op.Name == "ADD");
        Assert.Contains(operations, op => op.Name == "SUBTRACT");
        Assert.Contains(operations, op => op.Name == "MULTIPLY");
        Assert.Contains(operations, op => op.Name == "DIVIDE");
    }

    [Fact]
    public void GetOperations_ContainsAllAdvancedOperations()
    {
        // Act
        var result = _controller.GetOperations();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var operations = Assert.IsAssignableFrom<List<OperationMetadataDto>>(okResult.Value);

        // Assert - Check for advanced operations
        Assert.Contains(operations, op => op.Name == "POW");
        Assert.Contains(operations, op => op.Name == "SQRT");
        Assert.Contains(operations, op => op.Name == "LOG");
        Assert.Contains(operations, op => op.Name == "POW_BASE");
        Assert.Contains(operations, op => op.Name == "DIVIDE_INVERSE");
    }

    [Fact]
    public void GetOperation_WithValidName_ReturnsOkResult()
    {
        // Arrange
        var operationName = "ADD";

        // Act
        var result = _controller.GetOperation(operationName);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var operation = Assert.IsType<OperationMetadataDto>(okResult.Value);
        Assert.Equal("ADD", operation.Name);
        Assert.Equal("+", operation.Symbol);
        Assert.Equal("basic", operation.Category);
    }

    [Fact]
    public void GetOperation_IsCaseInsensitive()
    {
        // Arrange
        var operationName = "add"; // lowercase

        // Act
        var result = _controller.GetOperation(operationName);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var operation = Assert.IsType<OperationMetadataDto>(okResult.Value);
        Assert.Equal("ADD", operation.Name);
    }

    [Fact]
    public void GetOperation_WithInvalidName_ReturnsNotFound()
    {
        // Arrange
        var operationName = "INVALID_OPERATION";

        // Act
        var result = _controller.GetOperation(operationName);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetOperationsByCategory_ReturnsOkResult_WithGroupedOperations()
    {
        // Act
        var result = _controller.GetOperationsByCategory();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var grouped = Assert.IsAssignableFrom<Dictionary<string, List<OperationMetadataDto>>>(okResult.Value);
        
        // Should have 4 categories
        Assert.Equal(4, grouped.Count);
        Assert.True(grouped.ContainsKey("basic"));
        Assert.True(grouped.ContainsKey("advanced"));
        Assert.True(grouped.ContainsKey("multi-value"));
        Assert.True(grouped.ContainsKey("special"));
    }

    [Fact]
    public void GetOperationsByCategory_BasicCategory_ContainsCorrectOperations()
    {
        // Act
        var result = _controller.GetOperationsByCategory();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var grouped = Assert.IsAssignableFrom<Dictionary<string, List<OperationMetadataDto>>>(okResult.Value);

        // Assert
        var basicOps = grouped["basic"];
        Assert.Contains(basicOps, op => op.Name == "ADD");
        Assert.Contains(basicOps, op => op.Name == "SUBTRACT");
        Assert.Contains(basicOps, op => op.Name == "MULTIPLY");
        Assert.Contains(basicOps, op => op.Name == "DIVIDE");
        Assert.Contains(basicOps, op => op.Name == "NEGATE");
        Assert.Contains(basicOps, op => op.Name == "ABS");
    }

    [Fact]
    public void GetOperationsByCategory_AdvancedCategory_ContainsCorrectOperations()
    {
        // Act
        var result = _controller.GetOperationsByCategory();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var grouped = Assert.IsAssignableFrom<Dictionary<string, List<OperationMetadataDto>>>(okResult.Value);

        // Assert
        var advancedOps = grouped["advanced"];
        Assert.Contains(advancedOps, op => op.Name == "POW");
        Assert.Contains(advancedOps, op => op.Name == "SQRT");
        Assert.Contains(advancedOps, op => op.Name == "LOG");
        Assert.Contains(advancedOps, op => op.Name == "POW_BASE");
        Assert.Contains(advancedOps, op => op.Name == "DIVIDE_INVERSE");
    }

    [Fact]
    public void GetOperation_ADD_HasCorrectMetadata()
    {
        // Act
        var result = _controller.GetOperation("ADD");
        var okResult = Assert.IsType<OkObjectResult>(result);
        var operation = Assert.IsType<OperationMetadataDto>(okResult.Value);

        // Assert
        Assert.Equal("ADD", operation.Name);
        Assert.Equal("+", operation.Symbol);
        Assert.Equal("basic", operation.Category);
        Assert.Equal("accumulator", operation.Behavior);
        Assert.False(operation.IsUnary);
        Assert.Equal(1, operation.MinValues);
        Assert.Equal(-1, operation.MaxValues); // unlimited
    }

    [Fact]
    public void GetOperation_SQRT_HasCorrectMetadata()
    {
        // Act
        var result = _controller.GetOperation("SQRT");
        var okResult = Assert.IsType<OkObjectResult>(result);
        var operation = Assert.IsType<OperationMetadataDto>(okResult.Value);

        // Assert
        Assert.Equal("SQRT", operation.Name);
        Assert.Equal("advanced", operation.Category);
        Assert.Equal("unary", operation.Behavior);
        Assert.True(operation.IsUnary);
        Assert.Equal(0, operation.MinValues);
        Assert.Equal(0, operation.MaxValues);
    }

    [Fact]
    public void GetOperation_CLAMP_HasCorrectMetadata()
    {
        // Act
        var result = _controller.GetOperation("CLAMP");
        var okResult = Assert.IsType<OkObjectResult>(result);
        var operation = Assert.IsType<OperationMetadataDto>(okResult.Value);

        // Assert
        Assert.Equal("CLAMP", operation.Name);
        Assert.Equal("multi-value", operation.Category);
        Assert.Equal("unary", operation.Behavior);
        Assert.True(operation.IsUnary);
        Assert.Equal(2, operation.MinValues);
        Assert.Equal(2, operation.MaxValues);
    }
}
