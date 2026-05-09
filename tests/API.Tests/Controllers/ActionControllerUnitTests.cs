using API.Controllers;
using API.Models.Actions;
using Core.Combat;
using Core.Common;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using CoreLogger = Core.Logging.ILogger;

namespace API.Tests.Controllers;

/// <summary>
/// Unit tests for ActionController using mocked dependencies.
/// These tests focus on controller logic, validation, and error handling.
/// </summary>
public class ActionControllerUnitTests
{
    private readonly Mock<IActionManager> _mockActionManager;
    private readonly Mock<CoreLogger> _mockLogger;
    private readonly ActionController _controller;

    public ActionControllerUnitTests()
    {
        _mockActionManager = new Mock<IActionManager>();
        _mockLogger = new Mock<CoreLogger>();
        _controller = new ActionController(_mockActionManager.Object, _mockLogger.Object);
    }

    #region GetAllActions Tests

    [Fact]
    public void GetAllActions_ReturnsOkWithActions()
    {
        // Arrange
        var actions = new List<ActionDefinition>
        {
            new ActionDefinition
            {
                ActionId = "test_action",
                DisplayName = "Test Action",
                ActionType = ActionType.BASIC_ATTACK,
                Cooldown = 0,
                Tags = new List<string> { "test" }
            }
        };
        _mockActionManager.Setup(m => m.GetAllDefinitions()).Returns(actions);

        // Act
        var result = _controller.GetAllActions();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ActionSummaryDto>>(okResult.Value);
        Assert.Single(summaries);
        Assert.Equal("test_action", summaries[0].ActionId);
    }

    [Fact]
    public void GetAllActions_ReturnsEmptyList_WhenNoActions()
    {
        // Arrange
        _mockActionManager.Setup(m => m.GetAllDefinitions()).Returns(new List<ActionDefinition>());

        // Act
        var result = _controller.GetAllActions();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ActionSummaryDto>>(okResult.Value);
        Assert.Empty(summaries);
    }

    [Fact]
    public void GetAllActions_Returns500_OnException()
    {
        // Arrange
        _mockActionManager.Setup(m => m.GetAllDefinitions()).Throws(new Exception("Database error"));

        // Act
        var result = _controller.GetAllActions();

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    #endregion

    #region GetAction Tests

    [Fact]
    public void GetAction_ReturnsOk_WithValidId()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "fireball",
            DisplayName = "Fireball",
            ActionType = ActionType.POWER,
            Cooldown = 3,
            Tags = new List<string> { "magic" }
        };
        _mockActionManager.Setup(m => m.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(action));

        // Act
        var result = _controller.GetAction("fireball");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ActionDefinitionDto>(okResult.Value);
        Assert.Equal("fireball", dto.ActionId);
    }

    [Fact]
    public void GetAction_Returns404_WithInvalidId()
    {
        // Arrange
        _mockActionManager.Setup(m => m.GetDefinition("invalid"))
            .Returns(Result<ActionDefinition>.Failure("Action not found: invalid"));

        // Act
        var result = _controller.GetAction("invalid");

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public void GetAction_Returns400_WithEmptyId()
    {
        // Arrange
        _mockActionManager.Setup(m => m.GetDefinition(""))
            .Returns(Result<ActionDefinition>.Failure("Action ID cannot be empty"));

        // Act
        var result = _controller.GetAction("");

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    #endregion

    #region GetActionsByType Tests

    [Fact]
    public void GetActionsByType_ReturnsFilteredActions()
    {
        // Arrange
        var actions = new List<ActionDefinition>
        {
            new ActionDefinition
            {
                ActionId = "attack1",
                DisplayName = "Attack 1",
                ActionType = ActionType.BASIC_ATTACK,
                Cooldown = 0,
                Tags = new List<string>()
            }
        };
        _mockActionManager.Setup(m => m.GetDefinitionsByType(ActionType.BASIC_ATTACK))
            .Returns(actions);

        // Act
        var result = _controller.GetActionsByType("BASIC_ATTACK");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ActionSummaryDto>>(okResult.Value);
        Assert.Single(summaries);
        Assert.Equal("BASIC_ATTACK", summaries[0].ActionType);
    }

    [Fact]
    public void GetActionsByType_Returns400_WithInvalidType()
    {
        // Act
        var result = _controller.GetActionsByType("INVALID_TYPE");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void GetActionsByType_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        _mockActionManager.Setup(m => m.GetDefinitionsByType(ActionType.POWER))
            .Returns(new List<ActionDefinition>());

        // Act
        var result = _controller.GetActionsByType("POWER");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ActionSummaryDto>>(okResult.Value);
        Assert.Empty(summaries);
    }

    #endregion

    #region GetActionsByTag Tests

    [Fact]
    public void GetActionsByTag_ReturnsFilteredActions()
    {
        // Arrange
        var actions = new List<ActionDefinition>
        {
            new ActionDefinition
            {
                ActionId = "fireball",
                DisplayName = "Fireball",
                ActionType = ActionType.POWER,
                Cooldown = 3,
                Tags = new List<string> { "magic", "fire" }
            }
        };
        _mockActionManager.Setup(m => m.GetDefinitionsByTag("magic"))
            .Returns(actions);

        // Act
        var result = _controller.GetActionsByTag("magic");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ActionSummaryDto>>(okResult.Value);
        Assert.Single(summaries);
        Assert.Contains("magic", summaries[0].Tags);
    }

    [Fact]
    public void GetActionsByTag_Returns400_WithEmptyTag()
    {
        // Act
        var result = _controller.GetActionsByTag("");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void GetActionsByTag_Returns400_WithNullTag()
    {
        // Act
        var result = _controller.GetActionsByTag(null!);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void GetActionsByTag_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        _mockActionManager.Setup(m => m.GetDefinitionsByTag("nonexistent"))
            .Returns(new List<ActionDefinition>());

        // Act
        var result = _controller.GetActionsByTag("nonexistent");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summaries = Assert.IsAssignableFrom<List<ActionSummaryDto>>(okResult.Value);
        Assert.Empty(summaries);
    }

    #endregion

    #region ValidateAction Tests

    [Fact]
    public void ValidateAction_ReturnsValid_WithValidDefinition()
    {
        // Arrange
        var request = new ActionValidationRequest
        {
            Definition = new ActionDefinitionDto
            {
                ActionId = "test",
                DisplayName = "Test",
                ActionType = "BASIC_ATTACK",
                Cooldown = 0,
                BaseDamage = 10,
                Tags = new List<string>()
            }
        };
        _mockActionManager.Setup(m => m.ValidateActionDefinition(It.IsAny<ActionDefinition>()))
            .Returns(Result.Success());

        // Act
        var result = _controller.ValidateAction(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ActionValidationResponse>(okResult.Value);
        Assert.True(response.IsValid);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void ValidateAction_ReturnsInvalid_WithMissingActionId()
    {
        // Arrange
        var request = new ActionValidationRequest
        {
            Definition = new ActionDefinitionDto
            {
                ActionId = "",
                DisplayName = "Test",
                ActionType = "BASIC_ATTACK",
                Cooldown = 0,
                Tags = new List<string>()
            }
        };
        _mockActionManager.Setup(m => m.ValidateActionDefinition(It.IsAny<ActionDefinition>()))
            .Returns(Result.Failure("Action ID cannot be empty"));

        // Act
        var result = _controller.ValidateAction(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ActionValidationResponse>(okResult.Value);
        Assert.False(response.IsValid);
        Assert.Contains("Action ID cannot be empty", response.Errors);
    }

    [Fact]
    public void ValidateAction_ReturnsInvalid_WithMissingDisplayName()
    {
        // Arrange
        var request = new ActionValidationRequest
        {
            Definition = new ActionDefinitionDto
            {
                ActionId = "test",
                DisplayName = "",
                ActionType = "BASIC_ATTACK",
                Cooldown = 0,
                Tags = new List<string>()
            }
        };
        _mockActionManager.Setup(m => m.ValidateActionDefinition(It.IsAny<ActionDefinition>()))
            .Returns(Result.Failure("Display name cannot be empty"));

        // Act
        var result = _controller.ValidateAction(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ActionValidationResponse>(okResult.Value);
        Assert.False(response.IsValid);
        Assert.Contains("Display name cannot be empty", response.Errors);
    }

    [Fact]
    public void ValidateAction_ReturnsInvalid_WithNegativeCooldown()
    {
        // Arrange
        var request = new ActionValidationRequest
        {
            Definition = new ActionDefinitionDto
            {
                ActionId = "test",
                DisplayName = "Test",
                ActionType = "BASIC_ATTACK",
                Cooldown = -1,
                Tags = new List<string>()
            }
        };
        _mockActionManager.Setup(m => m.ValidateActionDefinition(It.IsAny<ActionDefinition>()))
            .Returns(Result.Failure("Cooldown cannot be negative"));

        // Act
        var result = _controller.ValidateAction(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ActionValidationResponse>(okResult.Value);
        Assert.False(response.IsValid);
        Assert.Contains("Cooldown cannot be negative", response.Errors);
    }

    [Fact]
    public void ValidateAction_ReturnsInvalid_WithEmptyResourceIdInCost()
    {
        // Arrange
        var request = new ActionValidationRequest
        {
            Definition = new ActionDefinitionDto
            {
                ActionId = "test",
                DisplayName = "Test",
                ActionType = "BASIC_ATTACK",
                Cooldown = 0,
                Tags = new List<string>(),
                Costs = new ActionCostsDto
                {
                    Costs = new List<ResourceCostDto>
                    {
                        new ResourceCostDto { ResourceId = "", Amount = 10 }
                    }
                }
            }
        };
        _mockActionManager.Setup(m => m.ValidateActionDefinition(It.IsAny<ActionDefinition>()))
            .Returns(Result.Failure("Resource ID in cost cannot be empty"));

        // Act
        var result = _controller.ValidateAction(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ActionValidationResponse>(okResult.Value);
        Assert.False(response.IsValid);
        Assert.Contains("Resource ID in cost cannot be empty", response.Errors);
    }

    [Fact]
    public void ValidateAction_ReturnsInvalid_WithNegativeCostAmount()
    {
        // Arrange
        var request = new ActionValidationRequest
        {
            Definition = new ActionDefinitionDto
            {
                ActionId = "test",
                DisplayName = "Test",
                ActionType = "BASIC_ATTACK",
                Cooldown = 0,
                Tags = new List<string>(),
                Costs = new ActionCostsDto
                {
                    Costs = new List<ResourceCostDto>
                    {
                        new ResourceCostDto { ResourceId = "mana", Amount = -10 }
                    }
                }
            }
        };
        _mockActionManager.Setup(m => m.ValidateActionDefinition(It.IsAny<ActionDefinition>()))
            .Returns(Result.Failure("Cost amount for mana cannot be negative"));

        // Act
        var result = _controller.ValidateAction(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ActionValidationResponse>(okResult.Value);
        Assert.False(response.IsValid);
        Assert.Contains("cannot be negative", response.Errors[0]);
    }

    [Fact]
    public void ValidateAction_Returns400_WithNullRequest()
    {
        // Act
        var result = _controller.ValidateAction(null!);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void ValidateAction_Returns400_WithNullDefinition()
    {
        // Arrange
        var request = new ActionValidationRequest { Definition = null! };

        // Act
        var result = _controller.ValidateAction(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    #endregion
}
