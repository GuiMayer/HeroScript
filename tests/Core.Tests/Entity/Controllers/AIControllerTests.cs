using Core.Combat;
using Core.Entity;
using Core.Entity.Components;
using Core.Entity.Controllers;
using Core.Logging;
using Core.Resources;
using Xunit;

namespace Core.Tests.Entity.Controllers;

public class AIControllerTests
{
    [Fact]
    public async Task DecideAction_Aggressive_ShouldAlwaysAttack()
    {
        // Arrange
        var controller = new AIController(AIBehaviorType.AGGRESSIVE, new ConsoleLogger("Test"));
        var entity = CreateEntityWithHealth(100);
        var combatState = CreateCombatState();
        
        // Act
        var result = await controller.DecideAction(entity, combatState);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ActionType.BASIC_ATTACK, result.Value!.ActionType);
        Assert.NotNull(result.Value.TargetId);
    }
    
    [Fact]
    public async Task DecideAction_Defensive_ShouldPass_WhenHealthLow()
    {
        // Arrange
        var controller = new AIController(
            AIBehaviorType.DEFENSIVE, 
            new ConsoleLogger("Test"),
            fleeHealthThreshold: 0.5f);
        var entity = CreateEntityWithHealth(30); // 30% health
        var combatState = CreateCombatState();
        
        // Act
        var result = await controller.DecideAction(entity, combatState);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ActionType.PASS, result.Value!.ActionType);
    }
    
    [Fact]
    public async Task DecideAction_Defensive_ShouldAttack_WhenHealthHigh()
    {
        // Arrange
        var controller = new AIController(
            AIBehaviorType.DEFENSIVE,
            new ConsoleLogger("Test"),
            fleeHealthThreshold: 0.3f);
        var entity = CreateEntityWithHealth(80); // 80% health
        var combatState = CreateCombatState();
        
        // Act
        var result = await controller.DecideAction(entity, combatState);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ActionType.BASIC_ATTACK, result.Value!.ActionType);
    }
    
    [Fact]
    public async Task DecideAction_Balanced_ShouldBeAggressive_WhenHealthHigh()
    {
        // Arrange
        var controller = new AIController(
            AIBehaviorType.BALANCED,
            new ConsoleLogger("Test"),
            lowHealthThreshold: 0.5f);
        var entity = CreateEntityWithHealth(80);
        var combatState = CreateCombatState();
        
        // Act
        var result = await controller.DecideAction(entity, combatState);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ActionType.BASIC_ATTACK, result.Value!.ActionType);
    }
    
    [Fact]
    public async Task DecideAction_Balanced_ShouldBeDefensive_WhenHealthLow()
    {
        // Arrange
        var controller = new AIController(
            AIBehaviorType.BALANCED,
            new ConsoleLogger("Test"),
            lowHealthThreshold: 0.5f,
            fleeHealthThreshold: 0.3f);
        var entity = CreateEntityWithHealth(20); // 20% health
        var combatState = CreateCombatState();
        
        // Act
        var result = await controller.DecideAction(entity, combatState);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ActionType.PASS, result.Value!.ActionType);
    }
    
    [Fact]
    public async Task DecideAction_ShouldFail_WhenNoResourceComponent()
    {
        // Arrange
        var controller = new AIController(AIBehaviorType.AGGRESSIVE, new ConsoleLogger("Test"));
        var entity = new Core.Entity.Entity { EntityId = "test" };
        var combatState = CreateCombatState();
        
        // Act
        var result = await controller.DecideAction(entity, combatState);
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("no ResourceComponent", result.Error);
    }
    
    [Fact]
    public async Task DecideAction_ShouldFail_WhenNoHealthResource()
    {
        // Arrange
        var controller = new AIController(AIBehaviorType.AGGRESSIVE, new ConsoleLogger("Test"));
        var resourceState = new EntityResourceState
        {
            EntityId = "test",
            Resources = new Dictionary<string, ResourcePool>()
        };
        var entity = new Core.Entity.Entity { EntityId = "test" }
            .AddComponent(new ResourceComponent(resourceState));
        var combatState = CreateCombatState();
        
        // Act
        var result = await controller.DecideAction(entity, combatState);
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("no health resource", result.Error);
    }
    
    [Fact]
    public void OnTurnStart_ShouldNotThrow()
    {
        // Arrange
        var controller = new AIController(AIBehaviorType.AGGRESSIVE, new ConsoleLogger("Test"));
        var entity = CreateEntityWithHealth(100);
        var combatState = CreateCombatState();
        
        // Act & Assert
        controller.OnTurnStart(entity, combatState);
    }
    
    [Fact]
    public void OnTurnEnd_ShouldNotThrow()
    {
        // Arrange
        var controller = new AIController(AIBehaviorType.AGGRESSIVE, new ConsoleLogger("Test"));
        var entity = CreateEntityWithHealth(100);
        var combatState = CreateCombatState();
        
        // Act & Assert
        controller.OnTurnEnd(entity, combatState);
    }
    
    [Fact]
    public void OnDamageTaken_ShouldNotThrow()
    {
        // Arrange
        var controller = new AIController(AIBehaviorType.AGGRESSIVE, new ConsoleLogger("Test"));
        var entity = CreateEntityWithHealth(100);
        
        // Act & Assert
        controller.OnDamageTaken(entity, 10f);
    }
    
    [Fact]
    public void OnDamageDealt_ShouldNotThrow()
    {
        // Arrange
        var controller = new AIController(AIBehaviorType.AGGRESSIVE, new ConsoleLogger("Test"));
        var entity = CreateEntityWithHealth(100);
        
        // Act & Assert
        controller.OnDamageDealt(entity, 10f);
    }
    
    [Fact]
    public void AIController_ShouldHaveCorrectType()
    {
        // Arrange & Act
        var controller = new AIController(AIBehaviorType.AGGRESSIVE, new ConsoleLogger("Test"));
        
        // Assert
        Assert.Equal(EntityControllerType.AI_BEHAVIOR_TREE, controller.Type);
    }
    
    private Core.Entity.Entity CreateEntityWithHealth(float healthPercent)
    {
        var healthDef = new ResourceDefinition
        {
            ResourceId = "health",
            Category = ResourceCategory.VITAL,
            DefaultCurrent = 100,
            DefaultMax = 100
        };
        
        var healthPool = new ResourcePool
        {
            ResourceId = "health",
            Current = healthPercent,
            Maximum = 100,
            Definition = healthDef
        };
        
        var resourceState = new EntityResourceState
        {
            EntityId = "test",
            Resources = new Dictionary<string, ResourcePool> { ["health"] = healthPool }
        };
        
        return new Core.Entity.Entity { EntityId = "test" }
            .AddComponent(new ResourceComponent(resourceState));
    }
    
    private CombatState CreateCombatState()
    {
        var heroHealthDef = new ResourceDefinition
        {
            ResourceId = "health",
            Category = ResourceCategory.VITAL,
            DefaultCurrent = 100,
            DefaultMax = 100
        };
        
        var heroHealthPool = new ResourcePool
        {
            ResourceId = "health",
            Current = 100,
            Maximum = 100,
            Definition = heroHealthDef
        };
        
        var heroResourceState = new EntityResourceState
        {
            EntityId = "hero",
            Resources = new Dictionary<string, ResourcePool> { ["health"] = heroHealthPool }
        };
        
        var hero = new CombatEntity
        {
            EntityId = "hero",
            Name = "Hero",
            IsHero = true,
            ResourceState = heroResourceState
        };
        
        return new CombatState
        {
            CombatId = Guid.NewGuid(),
            Hero = hero,
            Enemies = new List<CombatEntity>(),
            CurrentTurn = 1,
            Status = CombatStatus.ACTIVE
        };
    }
}
