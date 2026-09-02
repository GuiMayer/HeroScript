using API.Controllers;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Resources;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Unit")]

public sealed class CombatControllerTurnTests
{
    private readonly Mock<ICombatSystem> _combatSystem = new();
    private readonly Mock<IActionManager> _actionManager = new();
    private readonly Mock<IActionAffordabilityService> _affordabilityService = new();
    private readonly Mock<ICombatRunCoordinator> _combatRunCoordinator = new();
    private readonly Mock<IRunManager> _runManager = new();
    private readonly CombatController _controller;

    public CombatControllerTurnTests()
    {
        _controller = new CombatController(
            _combatSystem.Object,
            _actionManager.Object,
            _affordabilityService.Object,
            _combatRunCoordinator.Object,
            _runManager.Object,
            Mock.Of<ILogger<CombatController>>());
    }

    [Fact]
    public void GetAvailableActions_WithActorAndRun_FiltersByHandAndUsesActorResources()
    {
        var state = CreateCombatState();
        var runId = Guid.NewGuid();
        var fireball = new ActionDefinition
        {
            ActionId = "fireball",
            DisplayName = "Fireball",
            ActionType = ActionType.POWER
        };
        var heal = new ActionDefinition
        {
            ActionId = "heal",
            DisplayName = "Heal",
            ActionType = ActionType.POWER
        };

        _combatSystem.Setup(s => s.GetCombatState(state.CombatId))
            .Returns(Result<CombatState>.Success(state));
        _actionManager.Setup(m => m.GetAllDefinitions())
            .Returns(new[] { fireball, heal });
        _runManager.Setup(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(new RunState
            {
                RunId = runId,
                Deck = new DeckState { Hand = new List<string> { "fireball" } }
            }));
        _affordabilityService.Setup(s => s.CanAfford(It.IsAny<ActionDefinition>(), state.Hero.ResourceState.Resources))
            .Returns((ActionDefinition action, IReadOnlyDictionary<string, ResourcePool> _) =>
                Result<AffordabilityResult>.Success(new AffordabilityResult { ActionId = action.ActionId, CanAfford = true }));

        var result = _controller.GetAvailableActions(state.CombatId, "hero", runId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _affordabilityService.Verify(s => s.CanAfford(It.Is<ActionDefinition>(a => a.ActionId == "fireball"), state.Hero.ResourceState.Resources), Times.Once);
        _affordabilityService.Verify(s => s.CanAfford(It.Is<ActionDefinition>(a => a.ActionId == "heal"), state.Hero.ResourceState.Resources), Times.Once);
    }

    [Fact]
    public void CanAffordAction_WithActorAndRun_ReturnsAffordabilityForActor()
    {
        var state = CreateCombatState();
        var runId = Guid.NewGuid();
        var fireball = new ActionDefinition
        {
            ActionId = "fireball",
            ActionType = ActionType.POWER,
            Tags = new List<string> { "exhaust" }
        };

        _combatSystem.Setup(s => s.GetCombatState(state.CombatId))
            .Returns(Result<CombatState>.Success(state));
        _actionManager.Setup(m => m.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(fireball));
        _runManager.Setup(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(new RunState
            {
                RunId = runId,
                Deck = new DeckState { Hand = new List<string> { "fireball" } }
            }));
        _affordabilityService.Setup(s => s.CanAfford(fireball, state.Hero.ResourceState.Resources))
            .Returns(Result<AffordabilityResult>.Success(new AffordabilityResult
            {
                ActionId = "fireball",
                CanAfford = true
            }));

        var result = _controller.CanAffordAction(state.CombatId, "fireball", "hero", runId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _affordabilityService.Verify(s => s.CanAfford(fireball, state.Hero.ResourceState.Resources), Times.Once);
    }

    private static CombatState CreateCombatState()
    {
        return new CombatState
        {
            CombatId = Guid.NewGuid(),
            Hero = CreateEntity("hero", true, 30),
            Enemies = new[]
            {
                CreateEntity("enemy_1", false, 10),
                CreateEntity("enemy_dead", false, 0)
            }
        };
    }

    private static CombatEntity CreateEntity(string entityId, bool isHero, float health)
    {
        return new CombatEntity
        {
            EntityId = entityId,
            Name = entityId,
            IsHero = isHero,
            ResourceState = new EntityResourceState
            {
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = new ResourcePool
                    {
                        Definition = new ResourceDefinition
                        {
                            ResourceId = "health",
                            Category = ResourceCategory.VITAL,
                            DefaultMin = 0,
                            DefaultMax = 100
                        },
                        Current = health,
                        Maximum = 100
                    }
                }
            }
        };
    }
}
