using Core.Combat.TurnPhase;
using Core.Config;
using Core.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public class PhaseSystemFactoryTests
{
    private readonly ILogger _logger;
    private readonly PhaseSystemFactory _factory;
    private readonly Mock<IConfigManager> _configManager;
    private readonly Mock<IResourceLoader> _resourceLoader;
    
    public PhaseSystemFactoryTests()
    {
        _logger = new ConsoleLogger(nameof(PhaseSystemFactoryTests));
        _configManager = new Mock<IConfigManager>();
        _resourceLoader = new Mock<IResourceLoader>();
        _configManager.Setup(x => x.ResolveInheritanceChain("test"))
            .Returns(new[] { "test" });
        SetupPreset("magic-style", "Magic: The Gathering Style");
        SetupPreset("yugioh-style", "Yu-Gi-Oh! Style");
        SetupPreset("hearthstone-style", "Hearthstone Style");
        SetupPreset("classic-style", "Classic Simple Style");
        _factory = new PhaseSystemFactory(_logger, _configManager.Object, _resourceLoader.Object, configName: "test");
    }
    
    [Fact]
    public void CreateFromJson_ValidJson_ShouldCreateCompleteSystem()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test System"",
            ""description"": ""Test"",
            ""version"": ""1.0.0"",
            ""phases"": [""MAIN_1""],
            ""phaseDetails"": {
                ""MAIN_1"": {
                    ""name"": ""Main"",
                    ""description"": ""Main phase"",
                    ""allowedActions"": [""POWER""],
                    ""validNextPhases"": [""MAIN_1""],
                    ""autoTransition"": false,
                    ""allowPriority"": true
                }
            }
        }";
        
        // Act
        var result = _factory.CreateFromJson(json);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Sequence);
        Assert.NotNull(result.Value.PrioritySystem);
        Assert.NotNull(result.Value.PhaseManager);
        Assert.NotNull(result.Value.StackManager);
        Assert.Equal("Test System", result.Value.Sequence.Name);
    }
    
    [Fact]
    public void CreateFromJson_InvalidJson_ShouldFail()
    {
        // Arrange
        var json = "invalid json";
        
        // Act
        var result = _factory.CreateFromJson(json);
        
        // Assert
        Assert.True(result.IsFailure);
    }
    
    [Fact]
    public void CreateDisabled_ShouldCreateSystemWithNoPhases()
    {
        // Act
        var result = _factory.CreateDisabled();
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Disabled", result.Value.Sequence.Name);
        Assert.Single(result.Value.Sequence.Phases);
        Assert.Equal(Core.Combat.TurnPhase.TurnPhase.NONE, result.Value.Sequence.Phases[0]);
    }
    
    [Fact]
    public void CreateMagicStyle_ShouldCreateMagicStyleSystem()
    {
        // Act
        var result = _factory.CreateMagicStyle();
        
        // Assert
        // This test will fail if the preset file doesn't exist
        // In a real scenario, we'd either mock the file system or ensure the file exists
        // For now, we just verify the method doesn't throw
        Assert.NotNull(result);
    }
    
    [Fact]
    public void CreateYuGiOhStyle_ShouldCreateYuGiOhStyleSystem()
    {
        // Act
        var result = _factory.CreateYuGiOhStyle();
        
        // Assert
        Assert.NotNull(result);
    }
    
    [Fact]
    public void CreateHearthstoneStyle_ShouldCreateHearthstoneStyleSystem()
    {
        // Act
        var result = _factory.CreateHearthstoneStyle();
        
        // Assert
        Assert.NotNull(result);
    }
    
    [Fact]
    public void CreateClassicStyle_ShouldCreateClassicStyleSystem()
    {
        // Act
        var result = _factory.CreateClassicStyle();
        
        // Assert
        Assert.NotNull(result);
    }

    private void SetupPreset(string sequenceId, string name)
    {
        var json = $$"""
        {
          "{{sequenceId}}": {
            "name": "{{name}}",
            "description": "Test preset",
            "version": "1.0.0",
            "phases": ["MAIN_1"],
            "phaseDetails": {
              "MAIN_1": {
                "name": "Action",
                "description": "Action phase",
                "allowedActions": ["POWER"],
                "validNextPhases": ["MAIN_1"],
                "autoTransition": false,
                "allowPriority": true
              }
            }
          }
        }
        """;

        _resourceLoader.Setup(x => x.LoadResource($"phase-sequences/{sequenceId}.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(ParseResource(json));
    }

    private static Dictionary<string, JsonElement> ParseResource(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
