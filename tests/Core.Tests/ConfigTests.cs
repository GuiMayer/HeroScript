using Core.Config;
using M = Core.Math;
using System.Text.Json;
using Xunit;
using Moq;

namespace Core.Tests;

/// <summary>
/// xUnit tests for configuration system validation and inheritance
/// </summary>
public class ConfigTests
{
    private readonly Mock<IConfigManager> _mockConfigManager;
    private readonly Mock<IResourceLoader> _mockResourceLoader;
    private readonly ConfigValidator _configValidator;
    private readonly M.MathEngine _mathEngine;

    public ConfigTests()
    {
        // Setup mock config manager
        _mockConfigManager = new Mock<IConfigManager>();
        _mockConfigManager.Setup(m => m.CurrentConfig).Returns("dev");
        _mockConfigManager.Setup(m => m.GetConfigPath(It.IsAny<string>())).Returns("configs/dev");
        _mockConfigManager.Setup(m => m.ResolveInheritanceChain(It.IsAny<string>()))
            .Returns(new List<string> { "default", "dev" });
        _mockConfigManager.Setup(m => m.GetUserDataPath()).Returns(Path.Combine(Path.GetTempPath(), "heroscript-test"));

        // Setup mock resource loader
        _mockResourceLoader = new Mock<IResourceLoader>();
        
        // Create config validator with mock
        _configValidator = new ConfigValidator(_mockConfigManager.Object);
        
        // Create formula loader and math engine with mocks
        var formulaLoader = new M.FormulaLoader(_mockResourceLoader.Object);
        _mathEngine = new M.MathEngine(_mockConfigManager.Object, formulaLoader);
    }

    [Fact]
    public void ValidateConfig_WithNonExistentConfig_ShouldFail()
    {
        // Arrange
        var configName = "config_inexistente_12345";
        _mockConfigManager.Setup(m => m.ConfigExists(configName)).Returns(false);

        // Act
        var result = _configValidator.ValidateConfigSafe(configName);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ValidateConfig_WithDevConfig_ShouldPassWithWarnings()
    {
        // Arrange
        var configName = "dev";
        _mockConfigManager.Setup(m => m.ConfigExists(configName)).Returns(true);
        _mockConfigManager.Setup(m => m.GetConfigMetadata(configName))
            .Returns(new ConfigMetadata { Name = "Dev", Version = "1.0.0", Author = "test" });

        // Act
        var result = _configValidator.ValidateConfigSafe(configName);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void LoadConfig_WithDevConfig_ShouldSucceed()
    {
        // Arrange
        var configName = "dev";
        _mockConfigManager.Setup(m => m.LoadConfig(configName));

        // Act & Assert
        var exception = Record.Exception(() => _mockConfigManager.Object.LoadConfig(configName));
        Assert.Null(exception);
    }

    [Fact]
    public void GetAvailableFormulas_ShouldReturnFormulas()
    {
        // Arrange
        var mockFormulas = new Dictionary<string, JsonElement>
        {
            ["HYPERBOLIC_CURVE"] = JsonDocument.Parse("{}").RootElement,
            ["LINEAR_ADDITIVE"] = JsonDocument.Parse("{}").RootElement
        };
        
        _mockResourceLoader.Setup(m => m.LoadResource(
            "Pipelines/MathFormulas.json",
            It.IsAny<IEnumerable<string>>(),
            false))
            .Returns(mockFormulas);

        // Act
        var formulas = _mathEngine.GetAvailableFormulas();

        // Assert
        Assert.Contains("HYPERBOLIC_CURVE", formulas, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("LINEAR_ADDITIVE", formulas, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetFormulaOrigins_ReturnsOrigins()
    {
        // Arrange
        var mockOrigins = new Dictionary<string, string>
        {
            ["HYPERBOLIC_CURVE"] = "default",
            ["LINEAR_ADDITIVE"] = "dev"
        };
        
        _mockResourceLoader.Setup(m => m.GetResourceOrigins("Pipelines/MathFormulas.json"))
            .Returns(mockOrigins);

        // Act
        var origins = _mathEngine.GetFormulaOrigins();

        // Assert
        Assert.NotNull(origins);
    }

    [Fact]
    public void ResolveInheritanceChain_WithCircularInheritance_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var configName = "test-cycle-a";
        _mockConfigManager.Setup(m => m.ResolveInheritanceChain(configName))
            .Throws(new InvalidOperationException("Circular inheritance detected"));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _mockConfigManager.Object.ResolveInheritanceChain(configName)
        );
        Assert.Contains("Circular inheritance", exception.Message);
    }
}
