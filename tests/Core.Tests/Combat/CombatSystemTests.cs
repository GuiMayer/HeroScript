using Core.Combat;
using Core.Events;
using Core.Logging;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Combat;

public class CombatSystemTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<IResourceManager> _mockResourceManager;
    private readonly CombatSystem _combatSystem;

    public CombatSystemTests()
    {
        _mockLogger = new Mock<ILogger>();
        _mockEventBus = new Mock<IEventBus>();
        _mockResourceManager = new Mock<IResourceManager>();
        
        // Setup ResourceManager to return valid pools
        _mockResourceManager.Setup(rm => rm.CreatePool(It.IsAny<string>(), It.IsAny<float>()))
            .Returns((string resourceId, float current) =>
            {
                var definition = new ResourceDefinition
                {
                    ResourceId = resourceId,
                    DisplayName = resourceId == "health" ? "Health" : "Energy",
                    Category = resourceId == "health" ? ResourceCategory.VITAL : ResourceCategory.TACTICAL,
                    DefaultMin = 0,
                    DefaultMax = resourceId == "health" ? 100 : 10,
                    DefaultCurrent = current,
                    CanBeNegative = false
                };

                return new ResourcePool
                {
                    Definition = definition,
                    Current = current,
                    Maximum = resourceId == "health" ? 100 : 10,
                    Minimum = 0
                };
            });
        
        _combatSystem = new CombatSystem(_mockLogger.Object, _mockResourceManager.Object, _mockEventBus.Object);
    }

    [Fact]
    public void StartCombat_WithValidInput_ShouldSucceed()
    {
        // Arrange
        var heroId = "hero-1";
        var enemies = new List<string> { "enemy-1", "enemy-2" };
        var initialEnergy = 3;

        // Act
        var result = _combatSystem.StartCombat(heroId, enemies, initialEnergy);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(heroId, result.Value.Hero.EntityId);
        Assert.Equal(2, result.Value.Enemies.Count);
        Assert.Equal(initialEnergy, result.Value.GetHeroResource("energy")?.Current ?? 0);
        Assert.Equal(CombatStatus.ACTIVE, result.Value.Status);
    }

    [Fact]
    public void StartCombat_WithEmptyHeroId_ShouldFail()
    {
        // Arrange
        var heroId = "";
        var enemies = new List<string> { "enemy-1" };

        // Act
        var result = _combatSystem.StartCombat(heroId, enemies);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Hero ID cannot be empty", result.Error);
    }

    [Fact]
    public void StartCombat_WithNoEnemies_ShouldFail()
    {
        // Arrange
        var heroId = "hero-1";
        var enemies = new List<string>();

        // Act
        var result = _combatSystem.StartCombat(heroId, enemies);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("At least one enemy is required", result.Error);
    }

    [Fact]
    public void ExecuteAction_BasicAttack_ShouldDealDamageAndGainEnergy()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 0);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, ActionType.BASIC_ATTACK, targetId: targetId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.GetHeroResource("energy")?.Current ?? 0); // Gained 1 energy
        Assert.Equal(40, result.Value.Enemies[0].CurrentHp); // 50 - 10 = 40
        Assert.Single(result.Value.ActionHistory);
    }

    [Fact]
    public void ExecuteAction_Power_ShouldConsumeEnergyAndDealDamage()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act
        var result = _combatSystem.ExecuteAction(
            combatId,
            ActionType.POWER,
            powerId: "FIREBALL",
            targetId: targetId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.GetHeroResource("energy")?.Current ?? -1); // 3 - 3 = 0
        Assert.Equal(20, result.Value.Enemies[0].CurrentHp); // 50 - 30 = 20
        Assert.Single(result.Value.ActionHistory);
    }

    [Fact]
    public void ExecuteAction_PowerWithoutEnergy_ShouldFail()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 0);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act
        var result = _combatSystem.ExecuteAction(
            combatId,
            ActionType.POWER,
            powerId: "FIREBALL",
            targetId: targetId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Insufficient energy", result.Error);
    }

    [Fact]
    public void ExecuteAction_KillAllEnemies_ShouldSetStatusToVictory()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act - Kill enemy (50 HP: 1 power = 30 dmg, 2 basic = 20 dmg)
        _combatSystem.ExecuteAction(combatId, ActionType.POWER, "FIREBALL", targetId); // 50 - 30 = 20 HP
        _combatSystem.ExecuteAction(combatId, ActionType.BASIC_ATTACK, targetId: targetId); // 20 - 10 = 10 HP, +1 energy
        var result = _combatSystem.ExecuteAction(combatId, ActionType.BASIC_ATTACK, targetId: targetId); // 10 - 10 = 0 HP (dead)

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CombatStatus.VICTORY, result.Value.Status);
        Assert.False(result.Value.Enemies[0].IsAlive);
    }

    [Fact]
    public void ExecuteAction_Pass_ShouldNotChangeState()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var initialEnergy = startResult.Value.GetHeroResource("energy")?.Current ?? 0;
        var initialEnemyHp = startResult.Value.Enemies[0].CurrentHp;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, ActionType.PASS);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(initialEnergy, result.Value.GetHeroResource("energy")?.Current ?? 0);
        Assert.Equal(initialEnemyHp, result.Value.Enemies[0].CurrentHp);
        Assert.Single(result.Value.ActionHistory);
    }

    [Fact]
    public void ExecuteAction_EndTurn_ShouldIncrementTurn()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" });
        var combatId = startResult.Value.CombatId;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, ActionType.END_TURN);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.CurrentTurn);
        Assert.Single(result.Value.ActionHistory);
    }

    [Fact]
    public void GetCombatState_WithValidId_ShouldReturnState()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" });
        var combatId = startResult.Value.CombatId;

        // Act
        var result = _combatSystem.GetCombatState(combatId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(combatId, result.Value.CombatId);
    }

    [Fact]
    public void EndCombat_WithValidId_ShouldReturnResult()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" });
        var combatId = startResult.Value.CombatId;

        // Act
        var result = _combatSystem.EndCombat(combatId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(combatId, result.Value.CombatId);
        Assert.Equal(CombatStatus.ACTIVE, result.Value.Status);
    }

    [Fact]
    public void CombatExists_WithValidId_ShouldReturnTrue()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" });
        var combatId = startResult.Value.CombatId;

        // Act
        var exists = _combatSystem.CombatExists(combatId);

        // Assert
        Assert.True(exists);
    }
}
