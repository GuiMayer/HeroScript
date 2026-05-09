using Core.Combat;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat;

public class ActionAffordabilityServiceTests
{
    private readonly ActionAffordabilityService _service;

    public ActionAffordabilityServiceTests()
    {
        _service = new ActionAffordabilityService();
    }

    private IReadOnlyDictionary<string, ResourcePool> CreateMockResources(float energy = 100f, float mana = 50f)
    {
        var energyDef = new ResourceDefinition
        {
            ResourceId = "energy",
            DisplayName = "Energy",
            Category = ResourceCategory.TACTICAL,
            DefaultMax = 100,
            CanBeNegative = false,
            CanExceedMax = false,
            Tags = new List<string>()
        };

        var manaDef = new ResourceDefinition
        {
            ResourceId = "mana",
            DisplayName = "Mana",
            Category = ResourceCategory.TACTICAL,
            DefaultMax = 100,
            CanBeNegative = false,
            CanExceedMax = false,
            Tags = new List<string>()
        };

        return new Dictionary<string, ResourcePool>
        {
            ["energy"] = new ResourcePool
            {
                ResourceId = "energy",
                Current = energy,
                Maximum = 100,
                Minimum = 0,
                Definition = energyDef
            },
            ["mana"] = new ResourcePool
            {
                ResourceId = "mana",
                Current = mana,
                Maximum = 100,
                Minimum = 0,
                Definition = manaDef
            }
        };
    }

    [Fact]
    public void GetAffordableActions_WithSufficientResources_ReturnsAffordableActions()
    {
        // Arrange
        var resources = CreateMockResources(energy: 100f, mana: 50f);
        var actions = new List<ActionDefinition>
        {
            new ActionDefinition
            {
                ActionId = "affordable_action",
                Costs = new ActionCosts
                {
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "energy", Amount = 30f }
                    }
                }
            },
            new ActionDefinition
            {
                ActionId = "expensive_action",
                Costs = new ActionCosts
                {
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "energy", Amount = 150f }
                    }
                }
            }
        };

        // Act
        var affordableActions = _service.GetAffordableActions(actions, resources).ToList();

        // Assert
        Assert.Single(affordableActions);
        Assert.Equal("affordable_action", affordableActions[0].ActionId);
    }

    [Fact]
    public void GetCostOptions_ReturnsCorrectCostInformation()
    {
        // Arrange
        var resources = CreateMockResources(energy: 100f, mana: 50f);
        var action = new ActionDefinition
        {
            ActionId = "test_action",
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new ResourceCost { ResourceId = "energy", Amount = 30f, AllowOverdraft = false }
                },
                AlternativeCosts = new List<AlternativeCostOption>
                {
                    new AlternativeCostOption
                    {
                        OptionId = "mana_option",
                        Description = "Use mana instead",
                        Costs = new List<ResourceCost>
                        {
                            new ResourceCost { ResourceId = "mana", Amount = 20f, AllowOverdraft = false }
                        }
                    }
                }
            }
        };

        // Act
        var costOptions = _service.GetCostOptions(action, resources);

        // Assert
        Assert.Equal("test_action", costOptions.ActionId);
        Assert.Single(costOptions.NormalCosts);
        Assert.Equal("energy", costOptions.NormalCosts[0].ResourceId);
        Assert.Equal(30f, costOptions.NormalCosts[0].Amount);
        Assert.Single(costOptions.AlternativeOptions);
        Assert.Equal("mana_option", costOptions.AlternativeOptions[0].OptionId);
        Assert.True(costOptions.AlternativeOptions[0].Affordable);
    }

    [Fact]
    public void CanAfford_WithSufficientResources_ReturnsTrue()
    {
        // Arrange
        var resources = CreateMockResources(energy: 100f, mana: 50f);
        var action = new ActionDefinition
        {
            ActionId = "test_action",
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new ResourceCost { ResourceId = "energy", Amount = 30f }
                }
            }
        };

        // Act
        var result = _service.CanAfford(action, resources);

        // Assert
        Assert.Equal("test_action", result.ActionId);
        Assert.True(result.CanAfford);
        Assert.Null(result.Error);
    }

    [Fact]
    public void CanAfford_WithInsufficientResources_ReturnsFalse()
    {
        // Arrange
        var resources = CreateMockResources(energy: 10f, mana: 5f);
        var action = new ActionDefinition
        {
            ActionId = "test_action",
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new ResourceCost { ResourceId = "energy", Amount = 30f }
                }
            }
        };

        // Act
        var result = _service.CanAfford(action, resources);

        // Assert
        Assert.Equal("test_action", result.ActionId);
        Assert.False(result.CanAfford);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void CanAfford_WithAffordableAlternative_ReturnsAlternativeOptionId()
    {
        // Arrange
        var resources = CreateMockResources(energy: 10f, mana: 50f);
        var action = new ActionDefinition
        {
            ActionId = "test_action",
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new ResourceCost { ResourceId = "energy", Amount = 30f }
                },
                AlternativeCosts = new List<AlternativeCostOption>
                {
                    new AlternativeCostOption
                    {
                        OptionId = "mana_option",
                        Description = "Use mana instead",
                        Costs = new List<ResourceCost>
                        {
                            new ResourceCost { ResourceId = "mana", Amount = 20f }
                        }
                    }
                }
            }
        };

        // Act
        var result = _service.CanAfford(action, resources);

        // Assert
        Assert.Equal("test_action", result.ActionId);
        Assert.False(result.CanAfford); // Normal cost not affordable
        Assert.Single(result.AffordableOptionIds);
        Assert.Equal("mana_option", result.AffordableOptionIds[0]);
    }
}
