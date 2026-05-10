using Core.Entity;
using Core.Entity.Components;
using Core.Entity.Controllers;
using Xunit;

namespace Core.Tests.Entity;

public class EntityTests
{
    [Fact]
    public void Entity_ShouldBeCreated_WithBasicProperties()
    {
        // Arrange & Act
        var entity = new Core.Entity.Entity
        {
            EntityId = "test-entity",
            Type = EntityType.PLAYER,
            DefinitionId = "player_warrior",
            DisplayName = "Test Warrior"
        };
        
        // Assert
        Assert.Equal("test-entity", entity.EntityId);
        Assert.Equal(EntityType.PLAYER, entity.Type);
        Assert.Equal("player_warrior", entity.DefinitionId);
        Assert.Equal("Test Warrior", entity.DisplayName);
        Assert.Empty(entity.Components);
        Assert.Null(entity.Controller);
    }
    
    [Fact]
    public void AddComponent_ShouldAddComponentToEntity()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        var component = new StatsComponent(strength: 15);
        
        // Act
        var newEntity = entity.AddComponent(component);
        
        // Assert
        Assert.True(newEntity.HasComponent<StatsComponent>());
        Assert.NotNull(newEntity.GetComponent<StatsComponent>());
        Assert.Equal(15, newEntity.GetComponent<StatsComponent>()!.Strength);
    }
    
    [Fact]
    public void AddComponent_ShouldReplaceExistingComponent()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        var component1 = new StatsComponent(strength: 10);
        var component2 = new StatsComponent(strength: 20);
        
        // Act
        var entity1 = entity.AddComponent(component1);
        var entity2 = entity1.AddComponent(component2);
        
        // Assert
        Assert.Equal(20, entity2.GetComponent<StatsComponent>()!.Strength);
    }
    
    [Fact]
    public void RemoveComponent_ShouldRemoveComponentFromEntity()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        var component = new StatsComponent();
        var entityWithComponent = entity.AddComponent(component);
        
        // Act
        var entityWithoutComponent = entityWithComponent.RemoveComponent<StatsComponent>();
        
        // Assert
        Assert.False(entityWithoutComponent.HasComponent<StatsComponent>());
        Assert.Null(entityWithoutComponent.GetComponent<StatsComponent>());
    }
    
    [Fact]
    public void RemoveComponent_ShouldReturnSameEntity_WhenComponentNotPresent()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        
        // Act
        var result = entity.RemoveComponent<StatsComponent>();
        
        // Assert
        Assert.Equal(entity, result);
    }
    
    [Fact]
    public void GetComponent_ShouldReturnNull_WhenComponentNotPresent()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        
        // Act
        var component = entity.GetComponent<StatsComponent>();
        
        // Assert
        Assert.Null(component);
    }
    
    [Fact]
    public void HasComponent_ShouldReturnFalse_WhenComponentNotPresent()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        
        // Act
        var hasComponent = entity.HasComponent<StatsComponent>();
        
        // Assert
        Assert.False(hasComponent);
    }
    
    [Fact]
    public void Entity_ShouldBeImmutable_WhenAddingComponent()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        var component = new StatsComponent();
        
        // Act
        var newEntity = entity.AddComponent(component);
        
        // Assert
        Assert.NotSame(entity, newEntity);
        Assert.False(entity.HasComponent<StatsComponent>());
        Assert.True(newEntity.HasComponent<StatsComponent>());
    }
    
    [Fact]
    public void Entity_CanHaveMultipleComponents()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        var statsComp = new StatsComponent(strength: 15);
        var inventoryComp = new InventoryComponent();
        
        // Act
        var entity1 = entity.AddComponent(statsComp);
        var entity2 = entity1.AddComponent(inventoryComp);
        
        // Assert
        Assert.True(entity2.HasComponent<StatsComponent>());
        Assert.True(entity2.HasComponent<InventoryComponent>());
        Assert.Equal(2, entity2.Components.Count);
    }
    
    [Fact]
    public void UpdateComponent_ShouldUpdateExistingComponent()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        var component1 = new StatsComponent(strength: 10);
        var entityWithComp = entity.AddComponent(component1);
        var component2 = new StatsComponent(strength: 20);
        
        // Act
        var updatedEntity = entityWithComp.UpdateComponent(component2);
        
        // Assert
        Assert.Equal(20, updatedEntity.GetComponent<StatsComponent>()!.Strength);
    }
    
    [Fact]
    public void UpdateComponent_ShouldThrow_WhenComponentNotPresent()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        var component = new StatsComponent();
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => entity.UpdateComponent(component));
    }
    
    [Fact]
    public void AddComponent_ShouldThrow_WhenComponentIsNull()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test" };
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => entity.AddComponent<StatsComponent>(null!));
    }
    
    [Fact]
    public void Entity_CanHaveController()
    {
        // Arrange
        var controller = new PlayerController("player-ctrl");
        
        // Act
        var entity = new Core.Entity.Entity
        {
            EntityId = "test",
            Controller = controller
        };
        
        // Assert
        Assert.NotNull(entity.Controller);
        Assert.Equal("player-ctrl", entity.Controller.ControllerId);
        Assert.Equal(EntityControllerType.PLAYER_INPUT, entity.Controller.Type);
    }
    
    [Fact]
    public void Entity_WithRecordSyntax_ShouldCreateCopy()
    {
        // Arrange
        var entity = new Core.Entity.Entity
        {
            EntityId = "test",
            Type = EntityType.PLAYER,
            DisplayName = "Original"
        };
        
        // Act
        var copy = entity with { DisplayName = "Modified" };
        
        // Assert
        Assert.Equal("test", copy.EntityId);
        Assert.Equal(EntityType.PLAYER, copy.Type);
        Assert.Equal("Modified", copy.DisplayName);
        Assert.Equal("Original", entity.DisplayName);
    }
}
