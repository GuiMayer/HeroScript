using Core.Combat;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat;

public class AlternativeCostOptionTests
{
    private Dictionary<string, ResourcePool> CreateTestResources()
    {
        var manaDefinition = new ResourceDefinition
        {
            ResourceId = "mana",
            DisplayName = "Mana",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = 5,
            CanBeNegative = false
        };

        var healthDefinition = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultMin = 0,
            DefaultMax = 100,
            DefaultCurrent = 50,
            CanBeNegative = false
        };

        return new Dictionary<string, ResourcePool>
        {
            ["mana"] = new ResourcePool
            {
                Definition = manaDefinition,
                Current = 5,
                Maximum = 10,
                Minimum = 0
            },
            ["health"] = new ResourcePool
            {
                Definition = healthDefinition,
                Current = 50,
                Maximum = 100,
                Minimum = 0
            }
        };
    }

    [Fact]
    public void CanAfford_WithSufficientResources_ShouldReturnTrue()
    {
        // Arrange
        var resources = CreateTestResources();
        var option = new AlternativeCostOption
        {
            OptionId = "mana_cost",
            Description = "Pay 3 mana",
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "mana", Amount = 3, AllowOverdraft = false }
            }
        };

        // Act
        var canAfford = option.CanAfford(resources);

        // Assert
        Assert.True(canAfford);
    }

    [Fact]
    public void CanAfford_WithInsufficientResources_ShouldReturnFalse()
    {
        // Arrange
        var resources = CreateTestResources();
        var option = new AlternativeCostOption
        {
            OptionId = "mana_cost",
            Description = "Pay 10 mana",
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "mana", Amount = 10, AllowOverdraft = false }
            }
        };

        // Act
        var canAfford = option.CanAfford(resources);

        // Assert
        Assert.False(canAfford);
    }

    [Fact]
    public void GetAffordabilityError_WithInsufficientResources_ShouldReturnError()
    {
        // Arrange
        var resources = CreateTestResources();
        var option = new AlternativeCostOption
        {
            OptionId = "mana_cost",
            Description = "Pay 10 mana",
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "mana", Amount = 10, AllowOverdraft = false }
            }
        };

        // Act
        var error = option.GetAffordabilityError(resources);

        // Assert
        Assert.NotNull(error);
        Assert.Contains("Insufficient", error);
        Assert.Contains("Mana", error);
    }

    [Fact]
    public void CanAfford_WithMultipleCosts_ShouldValidateAll()
    {
        // Arrange
        var resources = CreateTestResources();
        var option = new AlternativeCostOption
        {
            OptionId = "combo_cost",
            Description = "Pay 2 mana and 10 health",
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "mana", Amount = 2, AllowOverdraft = false },
                new ResourceCost { ResourceId = "health", Amount = 10, AllowOverdraft = false }
            }
        };

        // Act
        var canAfford = option.CanAfford(resources);

        // Assert
        Assert.True(canAfford); // Has 5 mana and 50 health, can afford 2 mana + 10 health
    }

    [Fact]
    public void CanAfford_WithMultipleCostsOneInsufficient_ShouldReturnFalse()
    {
        // Arrange
        var resources = CreateTestResources();
        var option = new AlternativeCostOption
        {
            OptionId = "combo_cost",
            Description = "Pay 2 mana and 60 health",
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "mana", Amount = 2, AllowOverdraft = false },
                new ResourceCost { ResourceId = "health", Amount = 60, AllowOverdraft = false }
            }
        };

        // Act
        var canAfford = option.CanAfford(resources);

        // Assert
        Assert.False(canAfford); // Has 5 mana but only 50 health
    }

    [Fact]
    public void CanAfford_WithMissingResource_ShouldReturnFalse()
    {
        // Arrange
        var resources = CreateTestResources();
        var option = new AlternativeCostOption
        {
            OptionId = "energy_cost",
            Description = "Pay 3 energy",
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "energy", Amount = 3, AllowOverdraft = false }
            }
        };

        // Act
        var canAfford = option.CanAfford(resources);

        // Assert
        Assert.False(canAfford);
    }

    [Fact]
    public void GetAffordabilityError_WithMissingResource_ShouldReturnResourceNotFoundError()
    {
        // Arrange
        var resources = CreateTestResources();
        var option = new AlternativeCostOption
        {
            OptionId = "energy_cost",
            Description = "Pay 3 energy",
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "energy", Amount = 3, AllowOverdraft = false }
            }
        };

        // Act
        var error = option.GetAffordabilityError(resources);

        // Assert
        Assert.NotNull(error);
        Assert.Contains("Resource not found", error);
        Assert.Contains("energy", error);
    }

    [Fact]
    public void CanAfford_WithAllowOverdraft_ShouldReturnTrue()
    {
        // Arrange
        var resources = CreateTestResources();
        var option = new AlternativeCostOption
        {
            OptionId = "overdraft_cost",
            Description = "Pay 10 mana (overdraft allowed)",
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "mana", Amount = 10, AllowOverdraft = true }
            }
        };

        // Act
        var canAfford = option.CanAfford(resources);

        // Assert
        Assert.True(canAfford); // Overdraft allowed, so can afford even with insufficient mana
    }
}
