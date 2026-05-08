using Core.Combat;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Combat;

public class CombatIntegrationTests
{
    private static IResourceManager CreateMockResourceManager()
    {
        var mock = new Mock<IResourceManager>();
        
        mock.Setup(rm => rm.CreatePool(It.IsAny<string>(), It.IsAny<float>()))
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
        
        return mock.Object;
    }

    [Fact]
    public void FullCombatFlow_ShouldPublishAllEvents()
    {
        // Arrange
        var logger = NullLogger.Instance;
        var eventBus = new EventBus(logger);
        var resourceManager = CreateMockResourceManager();
        var combatSystem = new CombatSystem(logger, resourceManager, eventBus);

        var eventsPublished = new List<string>();
        eventBus.Subscribe<CombatStartedEvent>(e => eventsPublished.Add("CombatStarted"));
        eventBus.Subscribe<ActionExecutedEvent>(e => eventsPublished.Add("ActionExecuted"));
        eventBus.Subscribe<EnergyChangedEvent>(e => eventsPublished.Add("EnergyChanged"));
        eventBus.Subscribe<CombatEndedEvent>(e => eventsPublished.Add("CombatEnded"));

        // Act
        var startResult = combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        combatSystem.ExecuteAction(combatId, ActionType.BASIC_ATTACK, targetId: targetId);
        combatSystem.ExecuteAction(combatId, ActionType.POWER, "FIREBALL", targetId);
        combatSystem.EndCombat(combatId);

        // Assert
        Assert.Contains("CombatStarted", eventsPublished);
        Assert.Contains("ActionExecuted", eventsPublished);
        Assert.Contains("EnergyChanged", eventsPublished);
        Assert.Contains("CombatEnded", eventsPublished);
    }

    [Fact]
    public void CombatToVictory_ShouldTrackAllActions()
    {
        // Arrange
        var logger = NullLogger.Instance;
        var resourceManager = CreateMockResourceManager();
        var combatSystem = new CombatSystem(logger, resourceManager);

        // Act
        var startResult = combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Kill enemy (50 HP)
        combatSystem.ExecuteAction(combatId, ActionType.POWER, "FIREBALL", targetId); // -30 HP
        combatSystem.ExecuteAction(combatId, ActionType.BASIC_ATTACK, targetId: targetId); // -10 HP
        combatSystem.ExecuteAction(combatId, ActionType.BASIC_ATTACK, targetId: targetId); // -10 HP (dead)

        var finalState = combatSystem.GetCombatState(combatId).Value;

        // Assert
        Assert.Equal(CombatStatus.VICTORY, finalState.Status);
        Assert.False(finalState.Enemies[0].IsAlive);
        Assert.Equal(3, finalState.ActionHistory.Count);
    }
}
