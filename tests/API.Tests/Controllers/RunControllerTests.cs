using API.Controllers;
using Core.Common;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class RunControllerTests
{
    private readonly Mock<IRunManager> _runManager = new();
    private readonly RunController _controller;

    public RunControllerTests()
    {
        _controller = new RunController(_runManager.Object, Mock.Of<ILogger<RunController>>());
    }

    [Fact]
    public void StartRun_ReturnsRunState()
    {
        var state = CreateRun();
        _runManager
            .Setup(m => m.StartRun("test", "default_run", "hero"))
            .Returns(Result<RunState>.Success(state));

        var result = _controller.StartRun(new StartRunRequest("test", "default_run", "hero"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _runManager.Verify(m => m.StartRun("test", "default_run", "hero"), Times.Once);
    }

    [Fact]
    public void GetHand_ReturnsHandForRun()
    {
        var state = CreateRun();
        _runManager.Setup(m => m.GetRun(state.RunId)).Returns(Result<RunState>.Success(state));

        var result = _controller.GetHand(state.RunId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public void Draw_ReturnsBadRequestWhenRunManagerFails()
    {
        var runId = Guid.NewGuid();
        _runManager.Setup(m => m.DrawCards(runId, 2)).Returns(Result<IReadOnlyList<string>>.Failure("no deck"));

        var result = _controller.Draw(runId, new CountRequest(2));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static RunState CreateRun()
    {
        return new RunState
        {
            ConfigName = "test",
            PlayerEntityId = "hero",
            Gold = 10,
            Deck = new DeckState
            {
                DrawPile = new List<string> { "c" },
                Hand = new List<string> { "a", "b" }
            }
        };
    }
}
