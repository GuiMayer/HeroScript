using API.Controllers;
using API.Models;
using Core.Math;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public class ResourceControllerTests
{
    private readonly Mock<ILogger<ResourceController>> _mockLogger;
    private readonly Mock<ConfigReloadSettings> _mockReloadSettings;
    private readonly Mock<IMathEngine> _mockMathEngine;
    private readonly ResourceController _controller;

    public ResourceControllerTests()
    {
        _mockLogger = new Mock<ILogger<ResourceController>>();
        _mockReloadSettings = new Mock<ConfigReloadSettings>();
        _mockMathEngine = new Mock<IMathEngine>();
        _mockMathEngine.Setup(m => m.GetFormulaOrigins()).Returns(new Dictionary<string, string>
        {
            ["base_damage"] = "Pipelines/MathFormulas.json"
        });
        _controller = new ResourceController(_mockLogger.Object, _mockReloadSettings.Object, _mockMathEngine.Object);
    }

    [Fact]
    public void GetResourceOrigins_WithDefaultPath_ReturnsOkResult()
    {
        // Act
        var result = _controller.GetResourceOrigins();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var origins = Assert.IsType<ResourceOriginDto>(okResult.Value);
        Assert.Equal("Pipelines/MathFormulas.json", origins.ResourcePath);
        Assert.NotNull(origins.Origins);
    }

    [Fact]
    public void GetResourceOrigins_WithExplicitMathFormulasPath_ReturnsOkResult()
    {
        // Arrange
        var path = "Pipelines/MathFormulas.json";

        // Act
        var result = _controller.GetResourceOrigins(path);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var origins = Assert.IsType<ResourceOriginDto>(okResult.Value);
        Assert.Equal(path, origins.ResourcePath);
    }

    [Fact]
    public void GetResourceOrigins_WithNonMathFormulasPath_ReturnsNotFound()
    {
        // Arrange
        var path = "Pipelines/SomeOtherResource.json";

        // Act
        var result = _controller.GetResourceOrigins(path);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetResourceOrigins_WithInvalidPath_ReturnsNotFound()
    {
        // Arrange
        var path = "InvalidPath/Resource.json";

        // Act
        var result = _controller.GetResourceOrigins(path);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void ReloadResource_WhenDisabled_ReturnsForbidden()
    {
        // Arrange
        _mockReloadSettings.Object.Enabled = false;

        // Act
        var result = _controller.ReloadResource();

        // Assert
        var forbiddenResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbiddenResult.StatusCode);
    }

    [Fact]
    public void ReloadResource_WhenEnabled_ReturnsOk()
    {
        // Arrange
        _mockReloadSettings.Object.Enabled = true;

        // Act
        var result = _controller.ReloadResource();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void ReloadResource_WhenEnabled_WithPath_ReturnsOk()
    {
        // Arrange
        _mockReloadSettings.Object.Enabled = true;
        var path = "Pipelines/MathFormulas.json";

        // Act
        var result = _controller.ReloadResource(path);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void GetCacheStats_ReturnsNotImplemented()
    {
        // Act
        var result = _controller.GetCacheStats();

        // Assert
        var notImplementedResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(501, notImplementedResult.StatusCode);
    }

    [Fact]
    public void GetResourceOrigins_ReturnsOriginsAsDictionary()
    {
        // Act
        var result = _controller.GetResourceOrigins();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var origins = Assert.IsType<ResourceOriginDto>(okResult.Value);
        Assert.IsType<Dictionary<string, string>>(origins.Origins);
    }

    [Fact]
    public void ReloadResource_WhenDisabled_ReturnsErrorMessage()
    {
        // Arrange
        _mockReloadSettings.Object.Enabled = false;

        // Act
        var result = _controller.ReloadResource();

        // Assert
        var forbiddenResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbiddenResult.StatusCode);
        
        // Verify error message structure
        var value = forbiddenResult.Value;
        Assert.NotNull(value);
    }
}
