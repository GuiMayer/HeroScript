using API.Controllers;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Text.Json;

namespace API.Tests.Controllers;

[Trait("Category", "Unit")]

public sealed class RunControllerTests
{
    private readonly Mock<IRunManager> _runManager = new();
    private readonly Mock<IRunStateRepository> _repository = new();
    private readonly RunController _controller;

    public RunControllerTests()
    {
        _controller = new RunController(_runManager.Object, _repository.Object, Mock.Of<ILogger<RunController>>());
    }

    [Fact]
    public void StartRun_ReturnsRunState()
    {
        var state = CreateRun();
        _runManager
            .Setup(m => m.StartRun(new RunStartOptions("test", "default_run", "hero", null, null)))
            .Returns(Result<RunState>.Success(state));

        var result = _controller.StartRun(new StartRunRequest("test", "default_run", "hero"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        var json = JsonSerializer.SerializeToElement(
            ok.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("stateHash").GetString()));
        Assert.Equal((ulong)0, json.GetProperty("step").GetUInt64());
        _runManager.Verify(m => m.StartRun(new RunStartOptions("test", "default_run", "hero", null, null)), Times.Once);
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
