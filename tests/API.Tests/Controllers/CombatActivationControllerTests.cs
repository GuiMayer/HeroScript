using API.Controllers;
using Core.Combat.Activation;
using Core.Combat.Models;
using Core.Common;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class CombatActivationControllerTests
{
    private readonly Mock<ICombatActivationCoordinator> _coordinator = new();
    private readonly CombatActivationController _controller;

    public CombatActivationControllerTests()
    {
        _controller = new CombatActivationController(_coordinator.Object, Mock.Of<ILogger<CombatActivationController>>());
    }

    [Fact]
    public void Start_ReturnsActivationState()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        _coordinator.Setup(c => c.StartActivationCycle(combatId, runId, "default_activation"))
            .Returns(Result<CombatActivationResult>.Success(CreateResult(combatId, runId, "hero", drawn: new[] { "strike" })));

        var result = _controller.Start(combatId, new StartActivationRequest(runId, "default_activation"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _coordinator.Verify(c => c.StartActivationCycle(combatId, runId, "default_activation"), Times.Once);
    }

    [Fact]
    public void End_ReturnsBadRequestWhenCoordinatorFails()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        _coordinator.Setup(c => c.EndCurrentActivation(combatId, runId, "enemy"))
            .Returns(Result<CombatActivationResult>.Failure("Actor enemy is not the active actor"));

        var result = _controller.End(combatId, new EndActivationRequest(runId, "enemy"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void ProcessAi_DelegatesToCoordinator()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var gambits = new[] { "default_enemy" };
        _coordinator.Setup(c => c.ProcessCurrentAiActivation(combatId, runId, gambits))
            .Returns(Result<CombatActivationResult>.Success(CreateResult(combatId, runId, "hero")));

        var result = _controller.ProcessAi(combatId, new ProcessActivationAiRequest(runId, gambits));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _coordinator.Verify(c => c.ProcessCurrentAiActivation(combatId, runId, gambits), Times.Once);
    }

    [Fact]
    public void GetIntents_ReturnsCurrentActivationIntents()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        _coordinator.Setup(c => c.GetActivationState(combatId))
            .Returns(Result<CombatActivationResult>.Success(CreateResult(combatId, runId, "enemy")));

        var result = _controller.GetIntents(combatId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _coordinator.Verify(c => c.GetActivationState(combatId), Times.Once);
    }

    private static CombatActivationResult CreateResult(Guid combatId, Guid runId, string actorId, IReadOnlyList<string>? drawn = null) => new()
    {
        CombatState = new CombatState { CombatId = combatId, Hero = new CombatEntity { EntityId = "hero", IsHero = true } },
        RunState = new RunState
        {
            RunId = runId,
            Deck = new DeckState { Hand = drawn?.ToList() ?? new List<string>() }
        },
        ActivationState = new ActivationState
        {
            ActiveActorId = actorId,
            Round = 1,
            ActivationIndex = 0,
            ActivationNumber = 1,
            ActivationOrder = new[] { "hero", "enemy" },
            WaitingForInput = true,
            RulesId = "default_activation",
            RunId = runId
        },
        DrawnCardIds = drawn ?? Array.Empty<string>()
    };
}
