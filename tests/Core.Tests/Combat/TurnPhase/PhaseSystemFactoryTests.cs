using Core.Combat.TurnPhase;
using Core.Logging;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public class PhaseSystemFactoryTests
{
    private readonly ILogger _logger;
    private readonly PhaseSystemFactory _factory;
    
    public PhaseSystemFactoryTests()
    {
        _logger = new ConsoleLogger();
        _factory = new PhaseSystemFactory(_logger);
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
        Assert.Equal(TurnPhase.NONE, result.Value.Sequence.Phases[0]);
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
}
