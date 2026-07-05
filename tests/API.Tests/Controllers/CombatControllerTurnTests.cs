using API.Controllers;
using API.Models.Combat;
using Core.Combat;
using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Common;
using Core.Entity.Controllers;
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
    private readonly Mock<IGambitEngine> _gambitEngine = new();
    private readonly Mock<ICombatRunCoordinator> _combatRunCoordinator = new();
    private readonly Mock<IRunManager> _runManager = new();
    private readonly CombatController _controller;

    public CombatControllerTurnTests()
    {
        _controller = new CombatController(
            _combatSystem.Object,
            _actionManager.Object,
            _affordabilityService.Object,
            _gambitEngine.Object,
            _combatRunCoordinator.Object,
            _runManager.Object,
            Mock.Of<ILogger<CombatController>>());
    }

    [Fact]
    public void EndTurn_ExecutesEndTurnAction()
    {
        var state = CreateCombatState();
        _combatSystem
            .Setup(s => s.GetCombatState(state.CombatId))
            .Returns(Result<CombatState>.Success(state));
        _combatSystem
            .Setup(s => s.ExecuteAction(state.CombatId, It.Is<CombatActionCommand>(c =>
                c.ActorId == "hero" &&
                c.ActionType == ActionType.END_TURN)))
            .Returns(Result<CombatState>.Success(state with { CurrentTurn = 2 }));

        var result = _controller.EndTurn(state.CombatId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _combatSystem.Verify(s => s.ExecuteAction(state.CombatId, It.Is<CombatActionCommand>(c =>
            c.ActorId == "hero" &&
            c.ActionType == ActionType.END_TURN)), Times.Once);
    }

    [Fact]
    public void ProcessAiTurns_ExecutesGambitDecisionsForAliveEnemies()
    {
        var state = CreateCombatState();
        var updatedState = state with
        {
            Hero = state.Hero.TakeDamage(10),
            ActionHistory = new[]
            {
                new CombatAction
                {
                    ActorId = "enemy_1",
                    ActionType = ActionType.BASIC_ATTACK,
                    TargetId = "hero",
                    DamageDealt = 10
                }
            }
        };
        _combatSystem.Setup(s => s.GetCombatState(state.CombatId)).Returns(Result<CombatState>.Success(state));
        _gambitEngine
            .Setup(g => g.DecideAction(
                It.Is<Core.Entity.Entity>(e => e.EntityId == "enemy_1"),
                state,
                It.IsAny<IEnumerable<string>>()))
            .Returns(Result<EntityAction>.Success(new EntityAction
            {
                ActionType = ActionType.BASIC_ATTACK,
                TargetId = "hero"
            }));
        _combatSystem
            .Setup(s => s.ExecuteAction(state.CombatId, It.Is<CombatActionCommand>(c =>
                c.ActorId == "enemy_1" &&
                c.ActionType == ActionType.BASIC_ATTACK &&
                c.TargetId == "hero")))
            .Returns(Result<CombatState>.Success(updatedState));

        var result = _controller.ProcessAiTurns(state.CombatId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _gambitEngine.Verify(g => g.DecideAction(
            It.Is<Core.Entity.Entity>(e => e.EntityId == "enemy_1"),
            state,
            It.IsAny<IEnumerable<string>>()), Times.Once);
        _combatSystem.Verify(s => s.ExecuteAction(state.CombatId, It.Is<CombatActionCommand>(c =>
            c.ActorId == "enemy_1" &&
            c.ActionType == ActionType.BASIC_ATTACK &&
            c.TargetId == "hero")), Times.Once);
    }

    [Fact]
    public void ExecuteAction_WithRunId_UsesCombatRunCoordinator()
    {
        var state = CreateCombatState();
        var runId = Guid.NewGuid();
        var run = new RunState
        {
            RunId = runId,
            Deck = new DeckState
            {
                Hand = new List<string>(),
                DiscardPile = new List<string> { "fireball" }
            }
        };

        _actionManager.Setup(m => m.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(new ActionDefinition
            {
                ActionId = "fireball",
                ActionType = ActionType.POWER
            }));
        _combatRunCoordinator
            .Setup(c => c.ExecuteAction(state.CombatId, It.Is<CombatActionCommand>(command =>
                command.ActorId == "hero" &&
                command.RunId == runId &&
                command.CardId == "fireball" &&
                command.PowerId == "fireball" &&
                command.TargetId == "enemy_1")))
            .Returns(Result<CombatRunActionResult>.Success(new CombatRunActionResult
            {
                CombatState = state,
                RunState = run,
                ConsumedCardId = "fireball",
                Destination = CardConsumeDestination.Discard
            }));

        var result = _controller.ExecuteAction(state.CombatId, new ExecuteActionRequest
        {
            ActorId = "hero",
            ActionId = "fireball",
            TargetId = "enemy_1",
            RunId = runId,
            CardId = "fireball"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _combatRunCoordinator.Verify(c => c.ExecuteAction(state.CombatId, It.IsAny<CombatActionCommand>()), Times.Once);
        _combatSystem.Verify(s => s.ExecuteAction(It.IsAny<Guid>(), It.IsAny<CombatActionCommand>()), Times.Never);
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
