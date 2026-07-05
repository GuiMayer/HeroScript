using System;
using System.Collections.Generic;
using Core.Entity;
using Core.Entity.Components;
using Core.Combat.Models;
using Xunit;

namespace Core.Tests.Entity;

/// <summary>
/// Comprehensive tests for Entity namespace core models
/// Covers Entity, EntityType, IComponent, ComponentBase
/// </summary>
[Trait("Category", "Unit")]
public class EntityTests
{
    // ==================== ENTITY TYPE ENUM TESTS ====================
    
    [Fact]
    public void EntityType_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EntityType>();
        
        // Assert
        Assert.Contains(EntityType.PLAYER, values);
        Assert.Contains(EntityType.COMPANION, values);
        Assert.Contains(EntityType.ENEMY, values);
        Assert.Contains(EntityType.NPC, values);
        Assert.Equal(4, values.Length);
    }
    
    // ==================== ENTITY TESTS ====================
    
    [Fact]
    public void Entity_DefaultConstruction_SetsDefaultValues()
    {
        // Arrange & Act
        var entity = new Core.Entity.Entity();
        
        // Assert
        Assert.Equal(string.Empty, entity.EntityId);
        Assert.Equal(string.Empty, entity.DefinitionId);
        Assert.Equal(string.Empty, entity.DisplayName);
        Assert.Empty(entity.Components);
        Assert.Null(entity.Controller);
    }
    
    [Fact]
    public void Entity_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var components = new Dictionary<Type, IComponent>();
        
        // Act
        var entity = new Core.Entity.Entity
        {
            EntityId = "hero_001",
            Type = EntityType.PLAYER,
            DefinitionId = "hero_warrior",
            DisplayName = "Brave Warrior",
            Components = components
        };
        
        // Assert
        Assert.Equal("hero_001", entity.EntityId);
        Assert.Equal(EntityType.PLAYER, entity.Type);
        Assert.Equal("hero_warrior", entity.DefinitionId);
        Assert.Equal("Brave Warrior", entity.DisplayName);
        Assert.Empty(entity.Components);
    }
    
    [Fact]
    public void Entity_GetComponent_ReturnsComponentWhenExists()
    {
        // Arrange
        var resourceState = new EntityResourceState();
        var resourceComponent = new ResourceComponent(resourceState);
        var components = new Dictionary<Type, IComponent>
        {
            [typeof(ResourceComponent)] = resourceComponent
        };
        var entity = new Core.Entity.Entity { Components = components };
        
        // Act
        var retrieved = entity.GetComponent<ResourceComponent>();
        
        // Assert
        Assert.NotNull(retrieved);
        Assert.Same(resourceComponent, retrieved);
    }
    
    [Fact]
    public void Entity_GetComponent_ReturnsNullWhenNotExists()
    {
        // Arrange
        var entity = new Core.Entity.Entity();
        
        // Act
        var retrieved = entity.GetComponent<ResourceComponent>();
        
        // Assert
        Assert.Null(retrieved);
    }
    
    [Fact]
    public void Entity_HasComponent_ReturnsTrueWhenExists()
    {
        // Arrange
        var resourceState = new EntityResourceState();
        var resourceComponent = new ResourceComponent(resourceState);
        var components = new Dictionary<Type, IComponent>
        {
            [typeof(ResourceComponent)] = resourceComponent
        };
        var entity = new Core.Entity.Entity { Components = components };
        
        // Act
        var hasIt = entity.HasComponent<ResourceComponent>();
        
        // Assert
        Assert.True(hasIt);
    }
    
    [Fact]
    public void Entity_HasComponent_ReturnsFalseWhenNotExists()
    {
        // Arrange
        var entity = new Core.Entity.Entity();
        
        // Act
        var hasIt = entity.HasComponent<ResourceComponent>();
        
        // Assert
        Assert.False(hasIt);
    }
    
    [Fact]
    public void Entity_AddComponent_AddsNewComponent()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test_entity" };
        var resourceState = new EntityResourceState();
        var component = new ResourceComponent(resourceState);
        
        // Act
        var newEntity = entity.AddComponent(component);
        
        // Assert
        Assert.True(newEntity.HasComponent<ResourceComponent>());
        Assert.NotNull(newEntity.GetComponent<ResourceComponent>());
        
        // Original entity unchanged (immutable)
        Assert.False(entity.HasComponent<ResourceComponent>());
    }
    
    [Fact]
    public void Entity_AddComponent_ReplacesExistingComponent()
    {
        // Arrange
        var resourceState1 = new EntityResourceState();
        var component1 = new ResourceComponent(resourceState1);
        var components = new Dictionary<Type, IComponent>
        {
            [typeof(ResourceComponent)] = component1
        };
        var entity = new Core.Entity.Entity 
        { 
            EntityId = "test_entity",
            Components = components 
        };
        
        var resourceState2 = new EntityResourceState();
        var component2 = new ResourceComponent(resourceState2);
        
        // Act
        var newEntity = entity.AddComponent(component2);
        
        // Assert
        Assert.True(newEntity.HasComponent<ResourceComponent>());
        var retrieved = newEntity.GetComponent<ResourceComponent>();
        Assert.Same(component2, retrieved);
        Assert.NotSame(component1, retrieved);
    }
    
    [Fact]
    public void Entity_AddComponent_ThrowsOnNull()
    {
        // Arrange
        var entity = new Core.Entity.Entity();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            entity.AddComponent<ResourceComponent>(null!));
    }
    
    [Fact]
    public void Entity_RemoveComponent_RemovesExistingComponent()
    {
        // Arrange
        var resourceState = new EntityResourceState();
        var component = new ResourceComponent(resourceState);
        var components = new Dictionary<Type, IComponent>
        {
            [typeof(ResourceComponent)] = component
        };
        var entity = new Core.Entity.Entity 
        { 
            EntityId = "test_entity",
            Components = components 
        };
        
        // Act
        var newEntity = entity.RemoveComponent<ResourceComponent>();
        
        // Assert
        Assert.False(newEntity.HasComponent<ResourceComponent>());
        
        // Original entity unchanged (immutable)
        Assert.True(entity.HasComponent<ResourceComponent>());
    }
    
    [Fact]
    public void Entity_RemoveComponent_ReturnsUnchangedWhenNotExists()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "test_entity" };
        
        // Act
        var newEntity = entity.RemoveComponent<ResourceComponent>();
        
        // Assert
        Assert.Same(entity, newEntity);
    }
    
    [Fact]
    public void Entity_IsImmutable_WithExpression()
    {
        // Arrange
        var entity = new Core.Entity.Entity
        {
            EntityId = "original",
            DisplayName = "Original Name"
        };
        
        // Act
        var modified = entity with { DisplayName = "Modified Name" };
        
        // Assert
        Assert.Equal("Original Name", entity.DisplayName);
        Assert.Equal("Modified Name", modified.DisplayName);
        Assert.Equal("original", entity.EntityId);
        Assert.Equal("original", modified.EntityId);
    }
    
    [Fact]
    public void Entity_MultipleComponents_CanCoexist()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "multi_component" };
        
        // Act
        var resourceState = new EntityResourceState();
        var withResource = entity.AddComponent(new ResourceComponent(resourceState));
        var withStats = withResource.AddComponent(new StatsComponent());
        var withInventory = withStats.AddComponent(new InventoryComponent());
        
        // Assert
        Assert.True(withInventory.HasComponent<ResourceComponent>());
        Assert.True(withInventory.HasComponent<StatsComponent>());
        Assert.True(withInventory.HasComponent<InventoryComponent>());
        Assert.Equal(3, withInventory.Components.Count);
    }
    
    // ==================== COMPONENT BASE TESTS ====================
    
    [Fact]
    public void ComponentBase_DefaultState_IsEnabled()
    {
        // Arrange & Act
        var component = new TestComponent();
        
        // Assert
        Assert.True(component.IsEnabled);
        Assert.Equal(string.Empty, component.ComponentId);
    }
    
    [Fact]
    public void ComponentBase_Initialize_SetsOwnerAndId()
    {
        // Arrange
        var component = new TestComponent();
        var entity = new Core.Entity.Entity { EntityId = "test_entity" };
        
        // Act
        component.Initialize(entity);
        
        // Assert
        Assert.Equal("test_entity_TestComponent", component.ComponentId);
        Assert.True(component.OwnerIsSet);
    }
    
    [Fact]
    public void ComponentBase_Initialize_ThrowsOnNullEntity()
    {
        // Arrange
        var component = new TestComponent();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => component.Initialize(null!));
    }
    
    [Fact]
    public void ComponentBase_OnAttached_SetsOwner()
    {
        // Arrange
        var component = new TestComponent();
        var entity = new Core.Entity.Entity { EntityId = "test_entity" };
        
        // Act
        component.OnAttached(entity);
        
        // Assert
        Assert.True(component.OwnerIsSet);
    }
    
    [Fact]
    public void ComponentBase_OnAttached_ThrowsOnNullEntity()
    {
        // Arrange
        var component = new TestComponent();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => component.OnAttached(null!));
    }
    
    [Fact]
    public void ComponentBase_OnDetached_ClearsOwner()
    {
        // Arrange
        var component = new TestComponent();
        var entity = new Core.Entity.Entity { EntityId = "test_entity" };
        component.OnAttached(entity);
        
        // Act
        component.OnDetached(entity);
        
        // Assert
        Assert.False(component.OwnerIsSet);
    }
    
    [Fact]
    public void ComponentBase_Enable_SetsEnabledTrue()
    {
        // Arrange
        var component = new TestComponent();
        component.Disable();
        
        // Act
        component.Enable();
        
        // Assert
        Assert.True(component.IsEnabled);
    }
    
    [Fact]
    public void ComponentBase_Disable_SetsEnabledFalse()
    {
        // Arrange
        var component = new TestComponent();
        
        // Act
        component.Disable();
        
        // Assert
        Assert.False(component.IsEnabled);
    }
    
    [Fact]
    public void ComponentBase_EnableDisable_CanToggle()
    {
        // Arrange
        var component = new TestComponent();
        
        // Act & Assert - Multiple toggles
        Assert.True(component.IsEnabled);
        
        component.Disable();
        Assert.False(component.IsEnabled);
        
        component.Enable();
        Assert.True(component.IsEnabled);
        
        component.Disable();
        Assert.False(component.IsEnabled);
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void Entity_PlayerScenario_FullSetup()
    {
        // Arrange - Create a player entity with multiple components
        var player = new Core.Entity.Entity
        {
            EntityId = "player_001",
            Type = EntityType.PLAYER,
            DefinitionId = "hero_warrior",
            DisplayName = "Brave Knight"
        };
        
        // Act - Add components
        var resourceState = new EntityResourceState();
        var withResource = player.AddComponent(new ResourceComponent(resourceState));
        var withStats = withResource.AddComponent(new StatsComponent());
        var withInventory = withStats.AddComponent(new InventoryComponent());
        
        // Assert
        Assert.Equal(EntityType.PLAYER, withInventory.Type);
        Assert.Equal("Brave Knight", withInventory.DisplayName);
        Assert.Equal(3, withInventory.Components.Count);
        Assert.True(withInventory.HasComponent<ResourceComponent>());
        Assert.True(withInventory.HasComponent<StatsComponent>());
        Assert.True(withInventory.HasComponent<InventoryComponent>());
    }
    
    [Fact]
    public void Entity_EnemyScenario_MinimalSetup()
    {
        // Arrange - Create a simple enemy
        var enemy = new Core.Entity.Entity
        {
            EntityId = "goblin_001",
            Type = EntityType.ENEMY,
            DefinitionId = "enemy_goblin",
            DisplayName = "Goblin Warrior"
        };
        
        // Act - Add only resource component
        var resourceState = new EntityResourceState();
        var withResource = enemy.AddComponent(new ResourceComponent(resourceState));
        
        // Assert
        Assert.Equal(EntityType.ENEMY, withResource.Type);
        Assert.Single(withResource.Components);
        Assert.True(withResource.HasComponent<ResourceComponent>());
    }
    
    [Fact]
    public void Entity_ComponentLifecycle_AttachDetach()
    {
        // Arrange
        var entity = new Core.Entity.Entity { EntityId = "lifecycle_test" };
        var resourceState = new EntityResourceState();
        var component = new ResourceComponent(resourceState);
        
        // Act - Attach
        var withComponent = entity.AddComponent(component);
        Assert.True(withComponent.HasComponent<ResourceComponent>());
        
        // Act - Detach
        var withoutComponent = withComponent.RemoveComponent<ResourceComponent>();
        Assert.False(withoutComponent.HasComponent<ResourceComponent>());
        
        // Assert - Original unchanged
        Assert.False(entity.HasComponent<ResourceComponent>());
    }
    
    [Fact]
    public void Entity_ImmutabilityChain_PreservesOriginal()
    {
        // Arrange
        var original = new Core.Entity.Entity
        {
            EntityId = "immutable_test",
            DisplayName = "Original"
        };
        
        // Act - Chain modifications
        var step1 = original with { DisplayName = "Step 1" };
        var resourceState = new EntityResourceState();
        var step2 = step1.AddComponent(new ResourceComponent(resourceState));
        var step3 = step2 with { DisplayName = "Step 3" };
        
        // Assert - Each step is independent
        Assert.Equal("Original", original.DisplayName);
        Assert.False(original.HasComponent<ResourceComponent>());
        
        Assert.Equal("Step 1", step1.DisplayName);
        Assert.False(step1.HasComponent<ResourceComponent>());
        
        Assert.Equal("Step 1", step2.DisplayName);
        Assert.True(step2.HasComponent<ResourceComponent>());
        
        Assert.Equal("Step 3", step3.DisplayName);
        Assert.True(step3.HasComponent<ResourceComponent>());
    }
}

/// <summary>
/// Test component for ComponentBase tests
/// </summary>
internal class TestComponent : ComponentBase
{
    public bool OwnerIsSet => Owner != null;
    
    public new void Enable() => base.Enable();
    public new void Disable() => base.Disable();
}
