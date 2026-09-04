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
    public void WithState_ShouldReplaceReducedResourceState()
    {
        // Arrange
        var component = CreateComponentWithHealth(100);
        var reduced = component.ResourceState.Apply(
        [
            new ResolvedResourceMutation
            {
                MutationId = "test:set-health",
                ResourceId = "health",
                Operation = ResourceMutationOperation.Set,
                Value = 50
            }
        ]);

        // Act
        Assert.True(reduced.IsSuccess, reduced.IsFailure ? reduced.Error : null);
        var updatedComponent = component.WithState(reduced.Value.State);
        
        // Assert
        Assert.Equal(50, updatedComponent.GetResource("health")!.Current);
        Assert.Equal(100, component.GetResource("health")!.Current); // Original unchanged
    }
    
    [Fact]
    public void IsAlive_ShouldReturnTrue_WhenDefeatPolicyIsNotReached()
    {
        // Arrange
        var component = CreateComponentWithHealth(50);
        
        // Act
        var isAlive = component.IsAlive();
        
        // Assert
        Assert.True(isAlive);
    }
    
    [Fact]
    public void IsAlive_ShouldReturnFalse_WhenConfiguredDefeatPolicyIsReached()
    {
        // Arrange
        var component = CreateComponentWithHealth(0);
        
        // Act
        var isAlive = component.IsAlive();
        
        // Assert
        Assert.False(isAlive);
    }

    [Fact]
    public void IsAlive_DoesNotInferDefeatFromVitalCategory()
    {
        var definition = new ResourceDefinition
        {
            ResourceId = "stability",
            DisplayName = "Stability",
            Category = ResourceCategory.VITAL
        };
        var component = Component("stability", 0, definition);

        Assert.True(component.IsAlive());
    }

    [Fact]
    public void IsAlive_UsesDefeatPolicyOnArbitraryResource()
    {
        var definition = new ResourceDefinition
        {
            ResourceId = "morale",
            DisplayName = "Morale",
            Category = ResourceCategory.SPECIAL,
            ThresholdPolicies =
            [
                new ResourceThresholdPolicy
                {
                    PolicyId = "surrender",
                    Comparison = ResourceThresholdComparison.LessThanOrEqual,
                    ThresholdSource = ResourceThresholdSource.Minimum,
                    Consequence = ResourceThresholdConsequence.DefeatOwner
                }
            ]
        };
        var component = Component("morale", 0, definition);

        Assert.False(component.IsAlive());
    }
    
    [Fact]
    public void WithState_ShouldReplaceReducedResourceBatch()
    {
        // Arrange
        var component = CreateComponentWithHealthAndEnergy(100, 10);
        var reduced = component.ResourceState.Apply(
        [
            new ResolvedResourceMutation
            {
                MutationId = "test:set-health",
                ResourceId = "health",
                Operation = ResourceMutationOperation.Set,
                Value = 50
            },
            new ResolvedResourceMutation
            {
                MutationId = "test:set-energy",
                ResourceId = "energy",
                Operation = ResourceMutationOperation.Set,
                Value = 5
            }
        ]);
        
        // Act
        Assert.True(reduced.IsSuccess, reduced.IsFailure ? reduced.Error : null);
        var updatedComponent = component.WithState(reduced.Value.State);
        
        // Assert
        Assert.Equal(50, updatedComponent.GetResource("health")!.Current);
        Assert.Equal(5, updatedComponent.GetResource("energy")!.Current);
        Assert.Equal(100, component.GetResource("health")!.Current);
        Assert.Equal(10, component.GetResource("energy")!.Current);
    }

    [Fact]
    public void WithState_ShouldRejectDifferentOwner()
    {
        var component = CreateComponentWithHealth(100);
        var otherOwner = component.ResourceState with { OwnerId = "other" };

        var error = Assert.Throws<InvalidOperationException>(() => component.WithState(otherOwner));

        Assert.Contains("owner cannot change", error.Message);
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
            DefaultMax = 100,
            ThresholdPolicies =
            [
                new ResourceThresholdPolicy
                {
                    PolicyId = "defeat",
                    Comparison = ResourceThresholdComparison.LessThanOrEqual,
                    ThresholdSource = ResourceThresholdSource.Minimum,
                    Consequence = ResourceThresholdConsequence.DefeatOwner
                }
            ]
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

    private static ResourceComponent Component(
        string resourceId,
        float current,
        ResourceDefinition definition) => new(new ResourceSet
        {
            OwnerId = "test",
            Resources = new Dictionary<string, ResourcePool>
            {
                [resourceId] = new ResourcePool
                {
                    ResourceId = resourceId,
                    Current = current,
                    Minimum = 0,
                    Maximum = 100,
                    Definition = definition
                }
            }
        });
    
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
