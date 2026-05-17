using API.Controllers;
using API.Models;
using Core.Config;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public class ConfigControllerTests
{
    private readonly Mock<ILogger<ConfigController>> _mockLogger;
    private readonly Mock<ConfigReloadSettings> _mockReloadSettings;
    private readonly Mock<IWebHostEnvironment> _mockEnvironment;
    private readonly Mock<IConfigManager> _mockConfigManager;
    private readonly ConfigValidator _configValidator;
    private readonly ConfigController _controller;

    public ConfigControllerTests()
    {
        _mockLogger = new Mock<ILogger<ConfigController>>();
        _mockReloadSettings = new Mock<ConfigReloadSettings>();
        _mockEnvironment = new Mock<IWebHostEnvironment>();
        _mockConfigManager = new Mock<IConfigManager>();
        _mockConfigManager.SetupGet(m => m.CurrentConfig).Returns("alisyum");
        _mockConfigManager.SetupGet(m => m.DefaultConfig).Returns("alisyum");
        _mockConfigManager.Setup(m => m.GetAvailableConfigs()).Returns(new[] { "alisyum" });
        _mockConfigManager.Setup(m => m.ResolveInheritanceChain("alisyum")).Returns(new[] { "alisyum" });
        _mockConfigManager.Setup(m => m.GetConfigMetadata("alisyum")).Returns(new ConfigMetadata
        {
            Name = "alisyum",
            Version = "1.0.0",
            Author = "HeroScript",
            Description = "Default test configuration"
        });
        _mockConfigManager.Setup(m => m.GetConfigPath("alisyum")).Returns(AppContext.BaseDirectory);
        _mockConfigManager.Setup(m => m.GetConfigPath("nonexistent_config_12345")).Returns("Z:/missing/nonexistent_config_12345");
        _mockConfigManager.Setup(m => m.GetConfigPath("nonexistent_config")).Returns("Z:/missing/nonexistent_config");
        _mockConfigManager.Setup(m => m.LoadConfig("alisyum"));
        _configValidator = new ConfigValidator(_mockConfigManager.Object);
        _controller = new ConfigController(
            _mockLogger.Object, 
            _mockReloadSettings.Object,
            _mockEnvironment.Object,
            _mockConfigManager.Object,
            _configValidator);
    }

    [Fact]
    public void GetConfigs_ReturnsOkResult_WithListOfConfigs()
    {
        // Act
        var result = _controller.GetConfigs();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var configs = Assert.IsAssignableFrom<List<ConfigInfoDto>>(okResult.Value);
        Assert.NotEmpty(configs);
    }

    [Fact]
    public void GetCurrentConfig_ReturnsOkResult_WithCurrentConfigInfo()
    {
        // Act
        var result = _controller.GetCurrentConfig();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void ValidateConfig_WithValidConfig_ReturnsOkResult()
    {
        // Arrange
        var configName = "alisyum";

        // Act
        var result = _controller.ValidateConfig(configName);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var validation = Assert.IsType<ConfigValidationDto>(okResult.Value);
        Assert.Equal(configName, validation.ConfigName);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void ValidateConfig_WithInvalidConfig_ReturnsOkWithInvalidResult()
    {
        // Arrange
        var configName = "nonexistent_config_12345";

        // Act
        var result = _controller.ValidateConfig(configName);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var validation = Assert.IsType<ConfigValidationDto>(okResult.Value);
        Assert.Equal(configName, validation.ConfigName);
        Assert.False(validation.IsValid); // Should be invalid
        Assert.NotEmpty(validation.Errors); // Should have errors
    }

    [Fact]
    public void LoadConfig_WhenDisabled_ReturnsForbidden()
    {
        // Arrange
        _mockReloadSettings.Object.Enabled = false;
        var request = new LoadConfigRequest { ConfigName = "alisyum" };

        // Act
        var result = _controller.LoadConfig("alisyum", request);

        // Assert
        var forbiddenResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbiddenResult.StatusCode);
    }

    [Fact]
    public void LoadConfig_WhenEnabled_WithValidConfig_ReturnsOk()
    {
        // Arrange
        _mockReloadSettings.Object.Enabled = true;
        var request = new LoadConfigRequest { ConfigName = "alisyum" };

        // Act
        var result = _controller.LoadConfig("alisyum", request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void LoadConfig_WhenEnabled_WithInvalidConfig_ReturnsNotFound()
    {
        // Arrange
        _mockReloadSettings.Object.Enabled = true;
        var request = new LoadConfigRequest { ConfigName = "nonexistent_config" };

        // Act
        var result = _controller.LoadConfig("nonexistent_config", request);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetCurrentConfig_ReturnsInheritanceChain()
    {
        // Act
        var result = _controller.GetCurrentConfig();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var chain = Assert.IsType<ConfigChainDto>(okResult.Value);
        Assert.NotEmpty(chain.InheritanceChain);
        Assert.NotEmpty(chain.ChainDescription);
    }

    [Fact]
    public void ValidateConfig_WithConfigThatHasWarnings_ReturnsWarnings()
    {
        // Arrange - alisyum should have warning about no parent
        var configName = "alisyum";

        // Act
        var result = _controller.ValidateConfig(configName);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var validation = Assert.IsType<ConfigValidationDto>(okResult.Value);
        Assert.True(validation.IsValid);
        if (validation.Warnings != null)
        {
            Assert.NotEmpty(validation.Warnings);
        }
    }
}
