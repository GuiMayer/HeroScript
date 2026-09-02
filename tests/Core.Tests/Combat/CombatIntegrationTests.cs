using Core.Combat;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Effects;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Combat;

public class CombatIntegrationTests
{
    private static IActionManager CreateActionManager()
    {
        var mock = new Mock<IActionManager>();
        var actions = new[] { CreateBasicAttack(), CreateFireball() };

        foreach (var action in actions)
        {
            mock.Setup(m => m.GetDefinition(action.ActionId))
                .Returns(Core.Common.Result<ActionDefinition>.Success(action));
        }

        mock.Setup(m => m.GetDefinition(It.Is<string>(id => actions.All(a => a.ActionId != id))))
            .Returns((string id) => Core.Common.Result<ActionDefinition>.Failure($"Action definition not found: {id}"));

        return mock.Object;
    }

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
                    CanBeNegative = false,
                    ThresholdPolicies = resourceId == "health"
                        ?
                        [
                            new ResourceThresholdPolicy
                            {
                                PolicyId = "defeat_when_depleted",
                                Boundary = ResourceThresholdBoundary.AtMinimum,
                                Consequence = ResourceThresholdConsequence.DefeatOwner
                            }
                        ]
                        : []
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
        var combatSystem = new CombatSystem(logger, resourceManager, new FixedTurnOrderCalculator(logger), eventBus, actionManager: CreateActionManager());

        var eventsPublished = new List<string>();
        eventBus.Subscribe<CombatStartedEvent>(e => eventsPublished.Add("CombatStarted"));
        eventBus.Subscribe<ActionExecutedEvent>(e => eventsPublished.Add("ActionExecuted"));
        eventBus.Subscribe<EnergyChangedEvent>(e => eventsPublished.Add("EnergyChanged"));
        eventBus.Subscribe<CombatEndedEvent>(e => eventsPublished.Add("CombatEnded"));

        // Act
        var startResult = combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.BASIC_ATTACK, targetId: targetId));
        combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.POWER, powerId: "FIREBALL", targetId: targetId));
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
        var combatSystem = new CombatSystem(logger, resourceManager, new FixedTurnOrderCalculator(logger), actionManager: CreateActionManager());

        // Act
        var startResult = combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Kill enemy (50 HP)
        combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.POWER, powerId: "FIREBALL", targetId: targetId)); // -30 HP
        combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.BASIC_ATTACK, targetId: targetId)); // -10 HP
        combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.BASIC_ATTACK, targetId: targetId)); // -10 HP (dead)

        var finalState = combatSystem.GetCombatState(combatId).Value;

        // Assert
        Assert.Equal(CombatStatus.VICTORY, finalState.Status);
        Assert.False(finalState.Enemies[0].IsAlive);
        Assert.Equal(3, finalState.ActionHistory.Count);
    }

    private static ActionDefinition CreateBasicAttack()
    {
        return new ActionDefinition
        {
            ActionId = "basic_attack",
            ActionType = ActionType.BASIC_ATTACK,
            Tags = new List<string> { "physical", "melee", "can_crit" },
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = 10, Target = EffectTarget.TARGET, TargetResource = "health" },
                new() { Type = EffectType.MODIFY_RESOURCE, FlatValue = 1, TargetResource = "energy", Target = EffectTarget.SELF }
            }
        };
    }

    private static CombatActionCommand Command(
        string actorId,
        ActionType actionType,
        string? powerId = null,
        string? targetId = null,
        string? costOptionId = null)
    {
        return new CombatActionCommand
        {
            ActorId = actorId,
            ActionType = actionType,
            PowerId = powerId,
            TargetId = targetId,
            CostOptionId = costOptionId
        };
    }

    private static ActionDefinition CreateFireball()
    {
        return new ActionDefinition
        {
            ActionId = "FIREBALL",
            ActionType = ActionType.POWER,
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new() { ResourceId = "energy", Amount = 3 }
                }
            },
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = 30, Target = EffectTarget.TARGET, TargetResource = "health" }
            }
        };
    }
}
