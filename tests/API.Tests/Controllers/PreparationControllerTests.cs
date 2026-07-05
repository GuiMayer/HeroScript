using API.Controllers;
using Core.Common;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Unit")]

public sealed class PreparationControllerTests
{
    private readonly Mock<IRunManager> _runManager = new();
    private readonly PreparationController _controller;

    public PreparationControllerTests()
    {
        _controller = new PreparationController(_runManager.Object, Mock.Of<ILogger<PreparationController>>());
    }

    [Fact]
    public void Start_ReturnsPreparationOptions()
    {
        var runId = Guid.NewGuid();
        var preparation = new PreparationState { RunId = runId, PreparationId = "basic_preparation" };
        _runManager.Setup(m => m.CreatePreparation(runId, "basic_preparation")).Returns(Result<PreparationState>.Success(preparation));

        var result = _controller.Start(runId, new StartPreparationRequest("basic_preparation"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public void Apply_ReturnsBadRequestWhenOptionFails()
    {
        var runId = Guid.NewGuid();
        var preparationId = Guid.NewGuid();
        _runManager.Setup(m => m.ApplyPreparationOption(runId, preparationId, "x"))
            .Returns(Result<PreparationOptionState>.Failure("invalid"));

        var result = _controller.Apply(runId, preparationId, "x");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Apply_ReturnsModifierMetadata()
    {
        var runId = Guid.NewGuid();
        var preparationId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var option = new PreparationOptionState
        {
            OptionId = "train_spell",
            ApplyModifiers =
            {
                new PreparationModifierGrantState { OwnerId = "run", ModifierId = "flat_power_bonus", Stacks = 1 }
            },
            AppliedModifierInstanceIds = { instanceId },
            Applied = true
        };
        _runManager.Setup(m => m.ApplyPreparationOption(runId, preparationId, "train_spell"))
            .Returns(Result<PreparationOptionState>.Success(option));

        var result = _controller.Apply(runId, preparationId, "train_spell");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }
}
