using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Resources;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Combat;

public class CombatRunCoordinatorTests
{
    private readonly Mock<ICombatSystem> _combatSystem = new();
    private readonly Mock<IRunManager> _runManager = new();
    private readonly Mock<IActionManager> _actionManager = new();

    [Fact]
    public void ExecuteAction_WithCardInHand_ExecutesCombatThenDiscardsCard()
    {
        var runId = Guid.NewGuid();
        var command = Command(runId, "fireball");
        var combatState = CreateCombatState();
        var runState = CreateRunState(runId, hand: new[] { "fireball" }, discard: new[] { "fireball" });
        var coordinator = CreateCoordinator();

        _runManager.Setup(m => m.HasCardInHand(runId, "fireball"))
            .Returns(Result<bool>.Success(true));
        _actionManager.Setup(m => m.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(new ActionDefinition { ActionId = "fireball" }));
        _combatSystem.Setup(m => m.ExecuteAction(Guid.Empty, command))
            .Returns(Result<CombatState>.Success(combatState));
        _runManager.Setup(m => m.ConsumeCardsFromHand(runId, It.Is<IReadOnlyList<string>>(cards => cards.Single() == "fireball"), CardConsumeDestination.Discard))
            .Returns(Result<IReadOnlyList<string>>.Success(new[] { "fireball" }));
        _runManager.Setup(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(runState));

        var result = coordinator.ExecuteAction(Guid.Empty, command);

        Assert.True(result.IsSuccess);
        Assert.Equal("fireball", result.Value.ConsumedCardId);
        Assert.Equal(CardConsumeDestination.Discard, result.Value.Destination);
        _combatSystem.Verify(m => m.ExecuteAction(Guid.Empty, command), Times.Once);
        _runManager.Verify(m => m.ConsumeCardsFromHand(runId, It.IsAny<IReadOnlyList<string>>(), CardConsumeDestination.Discard), Times.Once);
    }

    [Fact]
    public void ExecuteAction_WithExhaustTag_ConsumesToExhaust()
    {
        var runId = Guid.NewGuid();
        var command = Command(runId, "fireball");
        var coordinator = CreateCoordinator();

        _runManager.Setup(m => m.HasCardInHand(runId, "fireball"))
            .Returns(Result<bool>.Success(true));
        _actionManager.Setup(m => m.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(new ActionDefinition
            {
                ActionId = "fireball",
                Tags = new List<string> { "spell", "exhaust" }
            }));
        _combatSystem.Setup(m => m.ExecuteAction(Guid.Empty, command))
            .Returns(Result<CombatState>.Success(CreateCombatState()));
        _runManager.Setup(m => m.ConsumeCardsFromHand(runId, It.IsAny<IReadOnlyList<string>>(), CardConsumeDestination.Exhaust))
            .Returns(Result<IReadOnlyList<string>>.Success(new[] { "fireball" }));
        _runManager.Setup(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(CreateRunState(runId, hand: Array.Empty<string>(), exhaust: new[] { "fireball" })));

        var result = coordinator.ExecuteAction(Guid.Empty, command);

        Assert.True(result.IsSuccess);
        Assert.Equal(CardConsumeDestination.Exhaust, result.Value.Destination);
        _runManager.Verify(m => m.ConsumeCardsFromHand(runId, It.IsAny<IReadOnlyList<string>>(), CardConsumeDestination.Exhaust), Times.Once);
    }

    [Fact]
    public void ExecuteAction_WithRetainTag_DoesNotConsumeCard()
    {
        var runId = Guid.NewGuid();
        var command = Command(runId, "shield_wall");
        var coordinator = CreateCoordinator();

        _runManager.Setup(m => m.HasCardInHand(runId, "shield_wall"))
            .Returns(Result<bool>.Success(true));
        _actionManager.Setup(m => m.GetDefinition("shield_wall"))
            .Returns(Result<ActionDefinition>.Success(new ActionDefinition
            {
                ActionId = "shield_wall",
                Tags = new List<string> { "retain" }
            }));
        _combatSystem.Setup(m => m.ExecuteAction(Guid.Empty, command))
            .Returns(Result<CombatState>.Success(CreateCombatState()));
        _runManager.Setup(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(CreateRunState(runId, hand: new[] { "shield_wall" })));

        var result = coordinator.ExecuteAction(Guid.Empty, command);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ConsumedCardId);
        Assert.Equal(CardConsumeDestination.None, result.Value.Destination);
        _runManager.Verify(m => m.ConsumeCardsFromHand(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CardConsumeDestination>()), Times.Never);
    }

    [Fact]
    public void ExecuteAction_WhenCardMissing_DoesNotExecuteCombat()
    {
        var runId = Guid.NewGuid();
        var command = Command(runId, "fireball");
        var coordinator = CreateCoordinator();

        _runManager.Setup(m => m.HasCardInHand(runId, "fireball"))
            .Returns(Result<bool>.Success(false));

        var result = coordinator.ExecuteAction(Guid.Empty, command);

        Assert.True(result.IsFailure);
        Assert.Contains("not in run hand", result.Error);
        _combatSystem.Verify(m => m.ExecuteAction(It.IsAny<Guid>(), It.IsAny<CombatActionCommand>()), Times.Never);
    }

    [Fact]
    public void ExecuteAction_WhenCombatFails_DoesNotConsumeCard()
    {
        var runId = Guid.NewGuid();
        var command = Command(runId, "fireball");
        var coordinator = CreateCoordinator();

        _runManager.Setup(m => m.HasCardInHand(runId, "fireball"))
            .Returns(Result<bool>.Success(true));
        _actionManager.Setup(m => m.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(new ActionDefinition { ActionId = "fireball" }));
        _combatSystem.Setup(m => m.ExecuteAction(Guid.Empty, command))
            .Returns(Result<CombatState>.Failure("invalid target"));

        var result = coordinator.ExecuteAction(Guid.Empty, command);

        Assert.True(result.IsFailure);
        _runManager.Verify(m => m.ConsumeCardsFromHand(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CardConsumeDestination>()), Times.Never);
    }

    [Fact]
    public void ExecuteAction_Pass_DoesNotRequireCard()
    {
        var runId = Guid.NewGuid();
        var command = new CombatActionCommand
        {
            RunId = runId,
            ActorId = "hero",
            ActionType = ActionType.PASS
        };
        var coordinator = CreateCoordinator();

        _combatSystem.Setup(m => m.ExecuteAction(Guid.Empty, command))
            .Returns(Result<CombatState>.Success(CreateCombatState()));
        _runManager.Setup(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(CreateRunState(runId, hand: new[] { "fireball" })));

        var result = coordinator.ExecuteAction(Guid.Empty, command);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ConsumedCardId);
        _runManager.Verify(m => m.HasCardInHand(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    private CombatRunCoordinator CreateCoordinator() => new(_combatSystem.Object, _runManager.Object, _actionManager.Object);

    private static CombatActionCommand Command(Guid runId, string actionId) => new()
    {
        RunId = runId,
        CardId = actionId,
        ActorId = "hero",
        ActionType = ActionType.POWER,
        PowerId = actionId,
        TargetId = "enemy"
    };

    private static CombatState CreateCombatState() => new()
    {
        Hero = CreateEntity("hero", true),
        Enemies = new[] { CreateEntity("enemy", false) }
    };

    private static CombatEntity CreateEntity(string id, bool isHero) => new()
    {
        EntityId = id,
        Name = id,
        IsHero = isHero,
        ResourceState = new EntityResourceState
        {
            EntityId = id,
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new()
                {
                    ResourceId = "health",
                    Current = 100,
                    Maximum = 100,
                    Minimum = 0,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = "health",
                        DisplayName = "Health",
                        Category = ResourceCategory.VITAL
                    }
                }
            }
        }
    };

    private static RunState CreateRunState(
        Guid runId,
        IReadOnlyList<string> hand,
        IReadOnlyList<string>? discard = null,
        IReadOnlyList<string>? exhaust = null) => new()
        {
            RunId = runId,
            Deck = new DeckState
            {
                Hand = hand.ToList(),
                DiscardPile = discard?.ToList() ?? new List<string>(),
                ExhaustPile = exhaust?.ToList() ?? new List<string>()
            }
        };
}
