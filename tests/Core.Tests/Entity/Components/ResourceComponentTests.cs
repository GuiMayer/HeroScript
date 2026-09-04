using Core.Combat;
using Core.Combat.Models;
using Core.Entity.Components;
using Core.Resources;
using Xunit;

namespace Core.Tests.Entity.Components;

public class ResourceComponentTests
{
    [Fact]
    public void ResourceComponent_ShouldBeCreated_WithResourceState()
    {
        // Arrange
        var healthDef = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultCurrent = 100,
            DefaultMax = 100
        };
        
        var healthPool = new ResourcePool
        {
            ResourceId = "health",
            Current = 100,
            Maximum = 100,
            Definition = healthDef
        };
        
        var resourceState = new ResourceSet
        {
            OwnerId = "test",
            Resources = new Dictionary<string, ResourcePool> { ["health"] = healthPool }
        };
        
        // Act
        var component = new ResourceComponent(resourceState);
        
        // Assert
        Assert.NotNull(component.ResourceState);
        Assert.Equal("test", component.ResourceState.OwnerId);
    }
    
    [Fact]
    public void GetResource_ShouldReturnResource_WhenExists()
    {
        // Arrange
        var component = CreateComponentWithHealth(100);
        
        // Act
        var health = component.GetResource("health");
        
        // Assert
        Assert.NotNull(health);
        Assert.Equal(100, health.Current);
    }
    
    [Fact]
    public void GetResource_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var component = CreateComponentWithHealth(100);
        
        // Act
        var mana = component.GetResource("mana");
        
        // Assert
        Assert.Null(mana);
    }
    
    [Fact]
    public void UpdateResource_ShouldUpdateResource()
    {
        // Arrange
        var component = CreateComponentWithHealth(100);
        var health = component.GetResource("health")!;
        var newHealth = health.Set(50);
        
        // Act
        var updatedComponent = component.UpdateResource("health", newHealth);
        
        // Assert
        Assert.Equal(50, updatedComponent.GetResource("health")!.Current);
        Assert.Equal(100, component.GetResource("health")!.Current); // Original unchanged
    }
    
    [Fact]
    public void GetVitalResource_ShouldReturnFirstVitalResource()
    {
        // Arrange
        var component = CreateComponentWithHealth(100);
        
        // Act
        var vital = component.GetVitalResource();
        
        // Assert
        Assert.NotNull(vital);
        Assert.Equal("health", vital.ResourceId);
    }
    
    [Fact]
    public void IsAlive_ShouldReturnTrue_WhenVitalResourceAboveZero()
    {
        // Arrange
        var component = CreateComponentWithHealth(50);
        
        // Act
        var isAlive = component.IsAlive();
        
        // Assert
        Assert.True(isAlive);
    }
    
    [Fact]
    public void IsAlive_ShouldReturnFalse_WhenVitalResourceAtZero()
    {
        // Arrange
        var component = CreateComponentWithHealth(0);
        
        // Act
        var isAlive = component.IsAlive();
        
        // Assert
        Assert.False(isAlive);
    }
    
    [Fact]
    public void UpdateResources_ShouldUpdateMultipleResources()
    {
        // Arrange
        var component = CreateComponentWithHealthAndEnergy(100, 10);
        var health = component.GetResource("health")!.Set(50);
        var energy = component.GetResource("energy")!.Set(5);
        
        var updates = new Dictionary<string, ResourcePool>
        {
            ["health"] = health,
            ["energy"] = energy
        };
        
        // Act
        var updatedComponent = component.UpdateResources(updates);
        
        // Assert
        Assert.Equal(50, updatedComponent.GetResource("health")!.Current);
        Assert.Equal(5, updatedComponent.GetResource("energy")!.Current);
    }
    
    [Fact]
    public void ResourceComponent_ShouldThrow_WhenResourceStateIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ResourceComponent(null!));
    }
    
    private ResourceComponent CreateComponentWithHealth(float health)
    {
        var healthDef = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultCurrent = 100,
            DefaultMax = 100
        };
        
        var healthPool = new ResourcePool
        {
            ResourceId = "health",
            Current = health,
            Maximum = 100,
            Definition = healthDef
        };
        
        var resourceState = new ResourceSet
        {
            OwnerId = "test",
            Resources = new Dictionary<string, ResourcePool> { ["health"] = healthPool }
        };
        
        return new ResourceComponent(resourceState);
    }
    
    private ResourceComponent CreateComponentWithHealthAndEnergy(float health, float energy)
    {
        var healthDef = new ResourceDefinition
        {
            ResourceId = "health",
            Category = ResourceCategory.VITAL,
            DefaultCurrent = 100,
            DefaultMax = 100
        };
        
        var energyDef = new ResourceDefinition
        {
            ResourceId = "energy",
            Category = ResourceCategory.TACTICAL,
            DefaultCurrent = 10,
            DefaultMax = 10
        };
        
        var healthPool = new ResourcePool
        {
            ResourceId = "health",
            Current = health,
            Maximum = 100,
            Definition = healthDef
        };
        
        var energyPool = new ResourcePool
        {
            ResourceId = "energy",
            Current = energy,
            Maximum = 10,
            Definition = energyDef
        };
        
        var resourceState = new ResourceSet
        {
            OwnerId = "test",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = healthPool,
                ["energy"] = energyPool
            }
        };
        
        return new ResourceComponent(resourceState);
    }
}
