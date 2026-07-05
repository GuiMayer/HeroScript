using API.Controllers;
using API.Models.Actions;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using CoreLogger = Core.Logging.ILogger;

namespace API.Tests.Controllers;

[Trait("Category", "Unit")]

public class ActionControllerTests
{
    private readonly Mock<IActionManager> _actionManager = new();
    private readonly ActionController _controller;

    public ActionControllerTests()
    {
        _controller = new ActionController(_actionManager.Object, Mock.Of<CoreLogger>());
    }

    [Fact]
    public void GetAllActions_ReturnsSuccessAndActionList()
    {
        _actionManager.Setup(m => m.GetAllDefinitions()).Returns(new List<ActionDefinition>
        {
            TestAction("basic_attack", ActionType.BASIC_ATTACK)
        });

        var result = _controller.GetAllActions();

        var ok = Assert.IsType<OkObjectResult>(result);
        var actions = Assert.IsAssignableFrom<List<ActionSummaryDto>>(ok.Value);
        var action = Assert.Single(actions);
        Assert.Equal("basic_attack", action.ActionId);
        Assert.Equal("BASIC_ATTACK", action.ActionType);
    }

    [Fact]
    public void GetAction_WithValidId_ReturnsActionDetails()
    {
        _actionManager.Setup(m => m.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(TestAction("fireball", ActionType.POWER)));

        var result = _controller.GetAction("fireball");

        var ok = Assert.IsType<OkObjectResult>(result);
        var action = Assert.IsType<ActionDefinitionDto>(ok.Value);
        Assert.Equal("fireball", action.ActionId);
        Assert.Equal("POWER", action.ActionType);
    }

    [Fact]
    public void GetAction_WithInvalidId_ReturnsNotFound()
    {
        _actionManager.Setup(m => m.GetDefinition("invalid_action_id"))
            .Returns(Result<ActionDefinition>.Failure("Action not found: invalid_action_id"));

        var result = _controller.GetAction("invalid_action_id");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetActionsByType_ReturnsFilteredActions()
    {
        _actionManager.Setup(m => m.GetDefinitionsByType(ActionType.BASIC_ATTACK))
            .Returns(new List<ActionDefinition> { TestAction("basic_attack", ActionType.BASIC_ATTACK) });

        var result = _controller.GetActionsByType("BASIC_ATTACK");

        var ok = Assert.IsType<OkObjectResult>(result);
        var actions = Assert.IsAssignableFrom<List<ActionSummaryDto>>(ok.Value);
        var action = Assert.Single(actions);
        Assert.Equal("BASIC_ATTACK", action.ActionType);
    }

    [Fact]
    public void ValidateAction_WithValidDefinition_ReturnsValid()
    {
        _actionManager.Setup(m => m.ValidateActionDefinition(It.IsAny<ActionDefinition>()))
            .Returns(Result.Success());

        var request = new ActionValidationRequest
        {
            Definition = new ActionDefinitionDto
            {
                ActionId = "test_action",
                DisplayName = "Test Action",
                ActionType = "POWER",
                Costs = new ActionCostsDto()
            }
        };

        var result = _controller.ValidateAction(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ActionValidationResponse>(ok.Value);
        Assert.True(response.IsValid);
    }

    private static ActionDefinition TestAction(string id, ActionType type) => new()
    {
        ActionId = id,
        DisplayName = id,
        ActionType = type,
        Tags = new List<string> { "test" },
        Costs = new ActionCosts()
    };
}
