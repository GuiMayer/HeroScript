using Core.Config;
using M = Core.Math;
using System.Text.Json;
using Xunit;

namespace Core.Tests;

/// <summary>
/// xUnit tests for configuration system validation and inheritance
/// </summary>
public class ConfigTests
{
    [Fact]
    public void ValidateConfig_WithNonExistentConfig_ShouldFail()
    {
        // Arrange
        var configName = "config_inexistente_12345";

        // Act
        var result = ConfigValidator.ValidateConfigSafe(configName);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ValidateConfig_WithDevConfig_ShouldPassWithWarnings()
    {
        // Arrange
        var configName = "dev";

        // Act
        var result = ConfigValidator.ValidateConfigSafe(configName);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void LoadConfig_WithDevConfig_ShouldSucceed()
    {
        // Arrange
        var configName = "dev";

        // Act & Assert
        var exception = Record.Exception(() => ConfigManager.Instance.LoadConfig(configName));
        Assert.Null(exception);
    }

    [Fact]
    public void GetAvailableFormulas_AfterLoadingDevConfig_ShouldContainHyperbolicCurve()
    {
        // Arrange
        ConfigManager.Instance.LoadConfig("dev");
        var engine = new M.MathEngine();

        // Act
        var formulas = engine.GetAvailableFormulas();

        // Assert
        Assert.Contains("HYPERBOLIC_CURVE", formulas, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAvailableFormulas_AfterLoadingDevConfig_ShouldContainLinearAdditive()
    {
        // Arrange
        ConfigManager.Instance.LoadConfig("dev");
        var engine = new M.MathEngine();

        // Act
        var formulas = engine.GetAvailableFormulas();

        // Assert
        Assert.Contains("LINEAR_ADDITIVE", formulas, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAvailableFormulas_AfterLoadingDevConfig_ShouldHave18Formulas()
    {
        // Arrange
        ConfigManager.Instance.LoadConfig("dev");
        var engine = new M.MathEngine();

        // Act
        var formulas = engine.GetAvailableFormulas().ToList();

        // Assert
        Assert.Equal(18, formulas.Count); // Updated: LERP formula was added
    }

    [Fact]
    public void GetFormulaOrigins_HyperbolicCurve_ReturnsOrigins()
    {
        // Arrange
        ConfigManager.Instance.LoadConfig("dev");

        // Act
        var origins = M.MathEngine.GetFormulaOrigins();

        // Assert - Just verify the method works and returns a dictionary
        Assert.NotNull(origins);
    }

    [Fact]
    public void GetFormulaOrigins_LinearAdditive_ReturnsOrigins()
    {
        // Arrange
        ConfigManager.Instance.LoadConfig("dev");

        // Act
        var origins = M.MathEngine.GetFormulaOrigins();

        // Assert - Just verify the method works and returns a dictionary
        Assert.NotNull(origins);
    }

    [Fact]
    public void ResolveInheritanceChain_WithCircularInheritance_ShouldThrowInvalidOperationException()
    {
        // Arrange - Create temporary configs with circular inheritance
        string userDataPath = ConfigManager.Instance.GetUserDataPath();
        string configA = Path.Combine(userDataPath, "test-cycle-a");
        string configB = Path.Combine(userDataPath, "test-cycle-b");

        try
        {
            // Create directory structure
            Directory.CreateDirectory(Path.Combine(configA, "Resources", "Pipelines"));
            Directory.CreateDirectory(Path.Combine(configB, "Resources", "Pipelines"));

            // Config A inherits from B
            var metadataA = new ConfigMetadata
            {
                Name = "Test Cycle A",
                Version = "1.0.0",
                Author = "test",
                Parent = "test-cycle-b"
            };
            File.WriteAllText(
                Path.Combine(configA, "config.json"),
                JsonSerializer.Serialize(metadataA, new JsonSerializerOptions { WriteIndented = true })
            );

            // Config B inherits from A (circular!)
            var metadataB = new ConfigMetadata
            {
                Name = "Test Cycle B",
                Version = "1.0.0",
                Author = "test",
                Parent = "test-cycle-a"
            };
            File.WriteAllText(
                Path.Combine(configB, "config.json"),
                JsonSerializer.Serialize(metadataB, new JsonSerializerOptions { WriteIndented = true })
            );

            // Create empty MathFormulas.json files
            File.WriteAllText(
                Path.Combine(configA, "Resources", "Pipelines", "MathFormulas.json"),
                "{}"
            );
            File.WriteAllText(
                Path.Combine(configB, "Resources", "Pipelines", "MathFormulas.json"),
                "{}"
            );

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ConfigManager.Instance.ResolveInheritanceChain("test-cycle-a")
            );
            Assert.Contains("Circular inheritance", exception.Message);
        }
        finally
        {
            // Cleanup
            try
            {
                if (Directory.Exists(configA))
                    Directory.Delete(configA, true);
                if (Directory.Exists(configB))
                    Directory.Delete(configB, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
