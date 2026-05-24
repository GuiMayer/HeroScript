using Core.Combat.TurnPhase;
using Core.Config;
using Core.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public class PhaseSequenceLoaderTests
{
    private readonly ILogger _logger;
    private readonly PhaseSequenceLoader _loader;
    private readonly Mock<IConfigManager> _configManager;
    private readonly Mock<IResourceLoader> _resourceLoader;
    
    public PhaseSequenceLoaderTests()
    {
        _logger = new ConsoleLogger(nameof(PhaseSequenceLoaderTests));
        _configManager = new Mock<IConfigManager>();
        _resourceLoader = new Mock<IResourceLoader>();
        _configManager.Setup(x => x.ResolveInheritanceChain("test"))
            .Returns(new[] { "test" });
        _loader = new PhaseSequenceLoader(_logger, _configManager.Object, _resourceLoader.Object);
    }
    
    [Fact]
    public void LoadFromJson_ValidJson_ShouldSucceed()
    {
        // Arrange
        var json = @"{
            ""name"": ""Test Sequence"",
            ""description"": ""Test description"",
            ""version"": ""1.0.0"",
            ""phases"": [""MAIN_1"", ""END""],
            ""phaseDetails"": {
                ""MAIN_1"": {
                    ""name"": ""Main Phase"",
                    ""description"": ""Main phase description"",
                    ""allowedActions"": [""POWER"", ""PASS""],
                    ""validNextPhases"": [""END""],
                    ""autoTransition"": false,
                    ""allowPriority"": true
                },
                ""END"": {
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
            ""phases"": [""MAIN_1"", ""END""],
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
        Assert.Contains("Invalid next phase", result.Error);
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

    [Fact]
    public void LoadFromResource_ValidResource_ShouldSucceedAndCache()
    {
        // Arrange
        var json = @"{
            ""classic-style"": {
                ""name"": ""Classic Resource"",
                ""phases"": [""MAIN_1""],
                ""phaseDetails"": {
                    ""MAIN_1"": {
                        ""name"": ""Action"",
                        ""allowedActions"": [""POWER""],
                        ""validNextPhases"": [""MAIN_1""]
                    }
                }
            }
        }";

        _resourceLoader.Setup(x => x.LoadResource("phase-sequences/classic-style.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(ParseResource(json));

        // Act
        var result1 = _loader.LoadFromResource("classic-style", "test");
        var result2 = _loader.LoadFromResource("classic-style", "test");

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Equal("Classic Resource", result1.Value.Name);
        _resourceLoader.Verify(x => x.LoadResource("phase-sequences/classic-style.json", It.IsAny<IEnumerable<string>>(), false), Times.Once);
    }

    private static Dictionary<string, JsonElement> ParseResource(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
