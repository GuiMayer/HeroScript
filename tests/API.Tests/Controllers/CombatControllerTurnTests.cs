using API.Controllers;
using Core.Combat;
using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Common;
using Core.Entity.Controllers;
using Core.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class CombatControllerTurnTests
{
    private readonly Mock<ICombatSystem> _combatSystem = new();
    private readonly Mock<IActionManager> _actionManager = new();
    private readonly Mock<IActionAffordabilityService> _affordabilityService = new();
    private readonly Mock<IGambitEngine> _gambitEngine = new();
    private readonly CombatController _controller;

    public CombatControllerTurnTests()
    {
        _controller = new CombatController(
            _combatSystem.Object,
            _actionManager.Object,
            _affordabilityService.Object,
            _gambitEngine.Object,
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
