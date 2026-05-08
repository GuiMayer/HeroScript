using Core.Combat;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat;

public class ActionCostsTests
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

        var energyDefinition = new ResourceDefinition
        {
            ResourceId = "energy",
            DisplayName = "Energy",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = 3,
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
            },
            ["energy"] = new ResourcePool
            {
                Definition = energyDefinition,
                Current = 3,
                Maximum = 10,
                Minimum = 0
            }
        };
    }

    [Fact]
    public void CanAfford_WithNoAlternatives_ShouldValidateNormalCosts()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "mana", Amount = 3, AllowOverdraft = false }
            },
            AlternativeCosts = new List<AlternativeCostOption>()
        };

        // Act
        var canAfford = costs.CanAfford(resources);

        // Assert
        Assert.True(canAfford);
    }

    [Fact]
    public void CanAfford_WithAlternatives_ShouldReturnTrueIfAnyOptionAffordable()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 10 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 10, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "health_cost",
                    Description = "Pay 10 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "health", Amount = 10, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var canAfford = costs.CanAfford(resources);

        // Assert
        Assert.True(canAfford); // Has 5 mana (insufficient) but 50 health (sufficient)
    }

    [Fact]
    public void CanAfford_WithAlternatives_ShouldReturnFalseIfNoOptionAffordable()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 10 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 10, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "health_cost",
                    Description = "Pay 60 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "health", Amount = 60, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var canAfford = costs.CanAfford(resources);

        // Assert
        Assert.False(canAfford); // Has 5 mana (needs 10) and 50 health (needs 60)
    }

    [Fact]
    public void GetAffordableOptions_ShouldReturnOnlyAffordableOptions()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 10 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 10, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "health_cost",
                    Description = "Pay 10 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "health", Amount = 10, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "energy_cost",
                    Description = "Pay 2 energy",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "energy", Amount = 2, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var affordableOptions = costs.GetAffordableOptions(resources);

        // Assert
        Assert.Equal(2, affordableOptions.Count); // health_cost and energy_cost are affordable
        Assert.Contains(affordableOptions, o => o.OptionId == "health_cost");
        Assert.Contains(affordableOptions, o => o.OptionId == "energy_cost");
        Assert.DoesNotContain(affordableOptions, o => o.OptionId == "mana_cost");
    }

    [Fact]
    public void CanAffordOption_WithValidOption_ShouldReturnTrue()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "health_cost",
                    Description = "Pay 10 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "health", Amount = 10, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var canAfford = costs.CanAffordOption("health_cost", resources);

        // Assert
        Assert.True(canAfford);
    }

    [Fact]
    public void CanAffordOption_WithInvalidOption_ShouldReturnFalse()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 10 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 10, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var canAfford = costs.CanAffordOption("mana_cost", resources);

        // Assert
        Assert.False(canAfford); // Has 5 mana, needs 10
    }

    [Fact]
    public void CanAffordOption_WithNonExistentOption_ShouldReturnFalse()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>()
        };

        // Act
        var canAfford = costs.CanAffordOption("nonexistent", resources);

        // Assert
        Assert.False(canAfford);
    }

    [Fact]
    public void GetAffordabilityError_WithNoAffordableOptions_ShouldReturnError()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 10 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 10, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "health_cost",
                    Description = "Pay 60 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "health", Amount = 60, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var error = costs.GetAffordabilityError(resources);

        // Assert
        Assert.NotNull(error);
        Assert.Contains("Cannot afford any alternative", error);
        Assert.Contains("Pay 10 mana OR Pay 60 health", error);
    }

    [Fact]
    public void GetOption_WithExistingOption_ShouldReturnOption()
    {
        // Arrange
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 3 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 3, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var option = costs.GetOption("mana_cost");

        // Assert
        Assert.NotNull(option);
        Assert.Equal("mana_cost", option.OptionId);
        Assert.Equal("Pay 3 mana", option.Description);
    }

    [Fact]
    public void GetOption_WithNonExistentOption_ShouldReturnNull()
    {
        // Arrange
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>()
        };

        // Act
        var option = costs.GetOption("nonexistent");

        // Assert
        Assert.Null(option);
    }

    [Fact]
    public void CanAfford_WithNormalCostsAndAlternatives_ShouldValidateBoth()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "energy", Amount = 2, AllowOverdraft = false }
            },
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 3 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 3, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var canAfford = costs.CanAfford(resources);

        // Assert
        Assert.True(canAfford); // Has 3 energy (sufficient) and 5 mana (sufficient for alternative)
    }

    [Fact]
    public void CanAfford_WithNormalCostsInsufficientAndAlternatives_ShouldReturnFalse()
    {
        // Arrange
        var resources = CreateTestResources();
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "energy", Amount = 10, AllowOverdraft = false }
            },
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 3 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 3, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var canAfford = costs.CanAfford(resources);

        // Assert
        Assert.False(canAfford); // Normal costs fail (needs 10 energy, has 3)
    }
}
