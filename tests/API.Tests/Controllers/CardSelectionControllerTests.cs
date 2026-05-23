using API.Controllers;
using Core.Common;
using Core.Run;
using Core.Run.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class CardSelectionControllerTests
{
    private readonly Mock<IRunManager> _runManager = new();
    private readonly CardSelectionController _controller;

    public CardSelectionControllerTests()
    {
        _controller = new CardSelectionController(_runManager.Object, Mock.Of<ILogger<CardSelectionController>>());
    }

    [Fact]
    public void Start_ReturnsSelectionOptions()
    {
        var runId = Guid.NewGuid();
        var selection = new CardSelectionState
        {
            RunId = runId,
            SelectionId = "basic_reward",
            PickCount = 1,
            Options = new List<CardSelectionOptionState> { new() { CardId = "a", Rarity = CardRarity.Common } }
        };
        _runManager.Setup(m => m.CreateCardSelection(runId, "basic_reward")).Returns(Result<CardSelectionState>.Success(selection));

        var result = _controller.Start(runId, new StartCardSelectionRequest("basic_reward"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public void Pick_ReturnsBadRequestOnInvalidPick()
    {
        var runId = Guid.NewGuid();
        var selectionId = Guid.NewGuid();
        _runManager.Setup(m => m.PickCards(runId, selectionId, It.IsAny<IReadOnlyList<string>>()))
            .Returns(Result<CardSelectionState>.Failure("invalid"));

        var result = _controller.Pick(runId, selectionId, new PickCardsRequest(new[] { "x" }));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Reroll_DelegatesToRunManager()
    {
        var runId = Guid.NewGuid();
        var selectionId = Guid.NewGuid();
        var selection = new CardSelectionState { RunId = runId, SelectionId = "basic_reward", PickCount = 1 };
        _runManager.Setup(m => m.RerollCardSelection(runId, selectionId, It.IsAny<IReadOnlyList<string>? >()))
            .Returns(Result<CardSelectionState>.Success(selection));

        var result = _controller.Reroll(runId, selectionId, new RerollCardSelectionRequest(new[] { "heal" }));

        Assert.IsType<OkObjectResult>(result);
        _runManager.Verify(m => m.RerollCardSelection(runId, selectionId, It.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("heal"))), Times.Once);
    }

    [Fact]
    public void Decompose_ReturnsBadRequestOnFailure()
    {
        var runId = Guid.NewGuid();
        var selectionId = Guid.NewGuid();
        _runManager.Setup(m => m.DecomposeCardSelectionOption(runId, selectionId, "fireball"))
            .Returns(Result<CardSelectionState>.Failure("invalid"));

        var result = _controller.Decompose(runId, selectionId, "fireball");

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
