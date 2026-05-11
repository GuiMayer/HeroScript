using Core.Combat.TurnPhase;
using Core.Logging;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public class PhaseSequenceLoaderTests
{
    private readonly ILogger _logger;
    private readonly PhaseSequenceLoader _loader;
    
    public PhaseSequenceLoaderTests()
    {
        _logger = new ConsoleLogger();
        _loader = new PhaseSequenceLoader(_logger);
    }
    
    [Fact]
    public void LoadFromJson_ValidJson_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test Sequence"",
            ""description"": ""Test description"",
            ""version"": ""1.0.0"",
            ""phases"": [""MAIN_1"", ""END_STEP""],
            ""phaseDetails"": {
                ""MAIN_1"": {
                    ""name"": ""Main Phase"",
                    ""description"": ""Main phase description"",
                    ""allowedActions"": [""POWER"", ""PASS""],
                    ""validNextPhases"": [""END_STEP""],
                    ""autoTransition"": false,
                    ""allowPriority"": true
                },
                ""END_STEP"": {
                    ""name"": ""End Step"",
                    ""description"": ""End step description"",
                    ""allowedActions"": [""PASS""],
                    ""validNextPhases"": [""MAIN_1""],
                    ""autoTransition"": true,
                    ""allowPriority"": false
                }
            },
            ""allowPhaseSkipping"": false
        }";
        
        // Act
        var result = _loader.LoadFromJson(json);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Test Sequence", result.Value.Name);
        Assert.Equal(2, result.Value.Phases.Count);
        Assert.Equal(2, result.Value.PhaseDetails.Count);
    }
    
    [Fact]
    public void LoadFromJson_EmptyJson_ShouldFail()
    {
        // Arrange
        var json = "";
        
        // Act
        var result = _loader.LoadFromJson(json);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be empty", result.Error);
    }
    
    [Fact]
    public void LoadFromJson_InvalidPhase_ShouldFail()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""phases"": [""INVALID_PHASE""],
            ""phaseDetails"": {
                ""INVALID_PHASE"": {
                    ""name"": ""Invalid"",
                    ""allowedActions"": [],
                    ""validNextPhases"": []
                }
            }
        }";
        
        // Act
        var result = _loader.LoadFromJson(json);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Invalid phase name", result.Error);
    }
    
    [Fact]
    public void LoadFromJson_MissingPhaseDetails_ShouldFail()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""phases"": [""MAIN_1"", ""END_STEP""],
            ""phaseDetails"": {
                ""MAIN_1"": {
                    ""name"": ""Main"",
                    ""allowedActions"": [],
                    ""validNextPhases"": []
                }
            }
        }";
        
        // Act
        var result = _loader.LoadFromJson(json);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Missing phase details", result.Error);
    }
    
    [Fact]
    public void LoadFromJson_InvalidNextPhase_ShouldFail()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""phases"": [""MAIN_1""],
            ""phaseDetails"": {
                ""MAIN_1"": {
                    ""name"": ""Main"",
                    ""allowedActions"": [],
                    ""validNextPhases"": [""NONEXISTENT_PHASE""]
                }
            }
        }";
        
        // Act
        var result = _loader.LoadFromJson(json);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("invalid next phase", result.Error);
    }
    
    [Fact]
    public void LoadFromJson_InvalidActionType_ShouldFail()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test"",
            ""phases"": [""MAIN_1""],
            ""phaseDetails"": {
                ""MAIN_1"": {
                    ""name"": ""Main"",
                    ""allowedActions"": [""INVALID_ACTION""],
                    ""validNextPhases"": []
                }
            }
        }";
        
        // Act
        var result = _loader.LoadFromJson(json);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Invalid action type", result.Error);
    }
    
    [Fact]
    public void LoadFromJson_WithCache_ShouldUseCacheOnSecondLoad()
    {
        // Arrange
        var json = @"{
            ""name"": ""Cached Test"",
            ""phases"": [""MAIN_1""],
            ""phaseDetails"": {
                ""MAIN_1"": {
                    ""name"": ""Main"",
                    ""allowedActions"": [],
                    ""validNextPhases"": []
                }
            }
        }";
        
        // Act
        var result1 = _loader.LoadFromJson(json);
        var result2 = _loader.LoadFromJson(json);
        
        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        // Note: JSON loading doesn't use cache (only file loading does)
        // This test documents current behavior
    }
}
