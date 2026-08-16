using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Config;
using Core.Effects;
using Core.Logging;
using Core.Resources;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Combat;

/// <summary>
/// Testes unitários para ActionManager
/// Foca em testar métodos públicos e validação, não o carregamento interno
/// </summary>
public class ActionManagerTests
{
    private readonly Mock<IConfigManager> _mockConfigManager;
    private readonly Mock<IResourceLoader> _mockResourceLoader;
    private readonly Mock<ILogger> _mockLogger;
    private readonly ActionManager _actionManager;

    public ActionManagerTests()
    {
        _mockConfigManager = new Mock<IConfigManager>();
        _mockResourceLoader = new Mock<IResourceLoader>();
        _mockLogger = new Mock<ILogger>();
        _actionManager = new ActionManager(_mockConfigManager.Object, _mockResourceLoader.Object, _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullConfigManager_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionManager(null!, _mockResourceLoader.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullResourceLoader_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionManager(_mockConfigManager.Object, null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionManager(_mockConfigManager.Object, _mockResourceLoader.Object, null!));
    }

    #endregion

    #region GetDefinition Tests

    [Fact]
    public void GetDefinition_WithEmptyId_ReturnsFailure()
    {
        // Act
        var result = _actionManager.GetDefinition("");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be empty", result.Error);
    }

    [Fact]
    public void GetDefinition_WithNullId_ReturnsFailure()
    {
        // Act
        var result = _actionManager.GetDefinition(null!);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void GetDefinition_WithNonExistentId_ReturnsFailure()
    {
        // Act
        var result = _actionManager.GetDefinition("nonexistent_action");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error);
    }

    #endregion

    #region GetAllDefinitions Tests

    [Fact]
    public void GetAllDefinitions_BeforeLoad_ReturnsEmptyList()
    {
        // Act
        var results = _actionManager.GetAllDefinitions();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void LoadActionDefinitions_WithDiscoveredAction_LoadsJsonWithoutHardcodedName()
    {
        // Arrange
        _mockConfigManager
            .Setup(m => m.ResolveInheritanceChain("test"))
            .Returns(new[] { "test" });

        _mockResourceLoader
            .Setup(m => m.DiscoverResources("actions", It.IsAny<IEnumerable<string>>(), "*.json"))
            .Returns(new[] { "new_json_action" });

        var json = """
        {
          "actionId": "new_json_action",
          "displayName": "New JSON Action",
          "actionType": "POWER",
          "costs": {
            "costs": [
              { "resourceId": "energy", "amount": 2 }
            ]
          },
          "effects": [
            { "type": "DAMAGE", "target": "TARGET", "flatValue": 7, "targetResource": "health" }
          ],
          "requiresTarget": true,
          "tags": ["json", "test"]
        }
        """;

        _mockResourceLoader
            .Setup(m => m.LoadResource("actions/new_json_action.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["new_json_action"] = JsonDocument.Parse(json).RootElement.Clone()
            });

        // Act
        _actionManager.LoadActionDefinitions("test");

        // Assert
        var result = _actionManager.GetDefinition("new_json_action");
        Assert.True(result.IsSuccess);
        Assert.Equal("New JSON Action", result.Value.DisplayName);
        Assert.Equal(ActionType.POWER, result.Value.ActionType);
        Assert.Equal(7, result.Value.Effects.Single().FlatValue);
        Assert.Equal("new_json_action.effect.0", result.Value.Effects.Single().EffectId);
    }

    #endregion

    #region GetDefinitionsByType Tests

    [Fact]
    public void GetDefinitionsByType_BeforeLoad_ReturnsEmptyList()
    {
        // Act
        var results = _actionManager.GetDefinitionsByType(ActionType.BASIC_ATTACK);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void GetDefinitionsByType_WithAllTypes_ReturnsEmptyBeforeLoad()
    {
        // Act & Assert
        Assert.Empty(_actionManager.GetDefinitionsByType(ActionType.BASIC_ATTACK));
        Assert.Empty(_actionManager.GetDefinitionsByType(ActionType.POWER));
        Assert.Empty(_actionManager.GetDefinitionsByType(ActionType.PASS));
        Assert.Empty(_actionManager.GetDefinitionsByType(ActionType.END_TURN));
    }

    #endregion

    #region GetDefinitionsByTag Tests

    [Fact]
    public void GetDefinitionsByTag_WithEmptyTag_ReturnsEmpty()
    {
        // Act
        var results = _actionManager.GetDefinitionsByTag("");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void GetDefinitionsByTag_WithNullTag_ReturnsEmpty()
    {
        // Act
        var results = _actionManager.GetDefinitionsByTag(null!);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void GetDefinitionsByTag_BeforeLoad_ReturnsEmpty()
    {
        // Act
        var results = _actionManager.GetDefinitionsByTag("magic");

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region ValidateActionDefinition Tests

    [Fact]
    public void ValidateActionDefinition_WithValidAction_ReturnsSuccess()
    {
        // Arrange
        var definition = new ActionDefinition
        {
            ActionId = "valid_action",
            DisplayName = "Valid Action",
            ActionType = ActionType.BASIC_ATTACK,
            Effects = new List<EffectDefinition> { new EffectDefinition { Type = EffectType.DAMAGE, FlatValue = 10, Target = EffectTarget.TARGET } },
            Cooldown = 0,
            Tags = new List<string> { "test" },
            Costs = new ActionCosts { Costs = new List<ResourceCost>() }
        };

        // Act
        var result = _actionManager.ValidateActionDefinition(definition);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateActionDefinition_WithMissingActionId_ReturnsFailure()
    {
        // Arrange
        var definition = new ActionDefinition
        {
            ActionId = "",
            DisplayName = "Test",
            ActionType = ActionType.BASIC_ATTACK,
            Effects = new List<EffectDefinition> { new EffectDefinition { Type = EffectType.DAMAGE, FlatValue = 10, Target = EffectTarget.TARGET } },
            Cooldown = 0,
            Tags = new List<string>(),
            Costs = new ActionCosts { Costs = new List<ResourceCost>() }
        };

        // Act
        var result = _actionManager.ValidateActionDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Action ID", result.Error);
    }

    [Fact]
    public void ValidateActionDefinition_WithMissingDisplayName_ReturnsFailure()
    {
        // Arrange
        var definition = new ActionDefinition
        {
            ActionId = "test",
            DisplayName = "",
            ActionType = ActionType.BASIC_ATTACK,
            Effects = new List<EffectDefinition> { new EffectDefinition { Type = EffectType.DAMAGE, FlatValue = 10, Target = EffectTarget.TARGET } },
            Cooldown = 0,
            Tags = new List<string>(),
            Costs = new ActionCosts { Costs = new List<ResourceCost>() }
        };

        // Act
        var result = _actionManager.ValidateActionDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Display name", result.Error);
    }

    [Fact]
    public void ValidateActionDefinition_WithNegativeCooldown_ReturnsFailure()
    {
        // Arrange
        var definition = new ActionDefinition
        {
            ActionId = "test",
            DisplayName = "Test",
            ActionType = ActionType.BASIC_ATTACK,
            Effects = new List<EffectDefinition> { new EffectDefinition { Type = EffectType.DAMAGE, FlatValue = 10, Target = EffectTarget.TARGET } },
            Cooldown = -1,
            Tags = new List<string>(),
            Costs = new ActionCosts { Costs = new List<ResourceCost>() }
        };

        // Act
        var result = _actionManager.ValidateActionDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Cooldown", result.Error);
    }

    [Fact]
    public void ValidateActionDefinition_WithEmptyResourceIdInCost_ReturnsFailure()
    {
        // Arrange
        var definition = new ActionDefinition
        {
            ActionId = "test",
            DisplayName = "Test",
            ActionType = ActionType.BASIC_ATTACK,
            Effects = new List<EffectDefinition> { new EffectDefinition { Type = EffectType.DAMAGE, FlatValue = 10, Target = EffectTarget.TARGET } },
            Cooldown = 0,
            Tags = new List<string>(),
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new ResourceCost { ResourceId = "", Amount = 10 }
                }
            }
        };

        // Act
        var result = _actionManager.ValidateActionDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Resource ID", result.Error);
    }

    [Fact]
    public void ValidateActionDefinition_WithNegativeCostAmount_ReturnsFailure()
    {
        // Arrange
        var definition = new ActionDefinition
        {
            ActionId = "test",
            DisplayName = "Test",
            ActionType = ActionType.BASIC_ATTACK,
            Effects = new List<EffectDefinition> { new EffectDefinition { Type = EffectType.DAMAGE, FlatValue = 10, Target = EffectTarget.TARGET } },
            Cooldown = 0,
            Tags = new List<string>(),
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new ResourceCost { ResourceId = "mana", Amount = -10 }
                }
            }
        };

        // Act
        var result = _actionManager.ValidateActionDefinition(definition);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be negative", result.Error);
    }

    #endregion

    #region LoadActionDefinitions Tests

    [Fact]
    public void LoadActionDefinitions_WithInvalidConfig_HandlesGracefully()
    {
        // Arrange
        _mockConfigManager.Setup(m => m.ResolveInheritanceChain(It.IsAny<string>()))
            .Throws(new Exception("Config not found"));

        // Act
        _actionManager.LoadActionDefinitions("invalid_config");
        var allActions = _actionManager.GetAllDefinitions();

        // Assert
        Assert.Empty(allActions);
        _mockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void LoadActionDefinitions_WithEmptyConfig_ReturnsEmptyList()
    {
        // Arrange
        var configChain = new List<string> { "empty_config" };
        _mockConfigManager.Setup(m => m.ResolveInheritanceChain(It.IsAny<string>()))
            .Returns(configChain);
        _mockResourceLoader.Setup(m => m.LoadResource(It.IsAny<string>(), It.IsAny<List<string>>(), false))
            .Returns(new Dictionary<string, System.Text.Json.JsonElement>());

        // Act
        _actionManager.LoadActionDefinitions("empty_config");
        var allActions = _actionManager.GetAllDefinitions();

        // Assert
        Assert.Empty(allActions);
    }

    #endregion
}
