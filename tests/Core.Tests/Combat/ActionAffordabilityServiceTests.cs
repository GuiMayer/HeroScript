using Core.Common;
using Core.Combat;
using Core.Combat.Models;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Combat;

public class ActionAffordabilityServiceTests
{
    private readonly ActionAffordabilityService _service;
    private readonly ILogger _logger;
    private readonly Mock<IRuntimeFormulaEvaluator> _formulaEvaluator = new();

    public ActionAffordabilityServiceTests()
    {
        _logger = NullLogger.Instance;
        _service = new ActionAffordabilityService(_logger, new ActionCostEvaluator(_formulaEvaluator.Object));
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
        var result = _service.GetAffordableActions(actions, resources);

        // Assert
        Assert.True(result.IsSuccess);
        var affordableActions = result.Value;
        Assert.Single(affordableActions);
        Assert.Equal("affordable_action", affordableActions[0].ActionId);
    }

    [Fact]
    public void GetCostOptions_WithValidAction_ReturnsOptions()
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
                    new ResourceCost { ResourceId = "energy", Amount = 30f },
                    new ResourceCost { ResourceId = "mana", Amount = 20f }
                },
                AlternativeCosts = new List<AlternativeCostOption>
                {
                    new AlternativeCostOption
                    {
                        OptionId = "alt1",
                        Description = "Alternative cost",
                        Costs = new List<ResourceCost>
                        {
                            new ResourceCost { ResourceId = "energy", Amount = 50f }
                        }
                    }
                }
            }
        };

        // Act
        var result = _service.GetCostOptions(action, resources);

        // Assert
        Assert.True(result.IsSuccess);
        var options = result.Value;
        Assert.Equal("test_action", options.ActionId);
        Assert.Equal(2, options.NormalCosts.Count);
        Assert.Equal("energy", options.NormalCosts[0].ResourceId);
        Assert.Equal(30f, options.NormalCosts[0].Amount);
        Assert.Single(options.AlternativeOptions);
        Assert.Equal("alt1", options.AlternativeOptions[0].OptionId);
    }

    [Fact]
    public void CanAfford_WithSufficientResources_ReturnsTrue()
    {
        // Arrange
        var resources = CreateMockResources(energy: 100f, mana: 50f);
        var action = new ActionDefinition
        {
            ActionId = "affordable_action",
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
        Assert.True(result.IsSuccess);
        var affordability = result.Value;
        Assert.Equal("affordable_action", affordability.ActionId);
        Assert.True(affordability.CanAfford);
    }

    [Fact]
    public void CanAfford_WithInsufficientResources_ReturnsFalse()
    {
        // Arrange
        var resources = CreateMockResources(energy: 10f, mana: 5f);
        var action = new ActionDefinition
        {
            ActionId = "expensive_action",
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new ResourceCost { ResourceId = "energy", Amount = 50f }
                }
            }
        };

        // Act
        var result = _service.CanAfford(action, resources);

        // Assert
        Assert.True(result.IsSuccess);
        var affordability = result.Value;
        Assert.Equal("expensive_action", affordability.ActionId);
        Assert.False(affordability.CanAfford);
    }

    [Fact]
    public void CanAfford_WithFormulaCost_UsesRuntimeFormulaEvaluator()
    {
        var resources = CreateMockResources(energy: 20f, mana: 5f);
        _formulaEvaluator
            .Setup(m => m.Evaluate("mana_current * 2", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Result<float>.Success(10f));
        var action = new ActionDefinition
        {
            ActionId = "dynamic_cost",
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new() { ResourceId = "energy", Amount = 99f, Formula = "mana_current * 2" }
                }
            }
        };

        var result = _service.CanAfford(action, resources);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanAfford);
        _formulaEvaluator.Verify(m => m.Evaluate(
            "mana_current * 2",
            It.Is<Dictionary<string, float>>(vars => vars["mana_current"] == 5f && vars["energy_current"] == 20f),
            0f), Times.AtLeastOnce);
    }

    [Fact]
    public void GetCostOptions_WithFormulaCost_ReturnsResolvedAmount()
    {
        var resources = CreateMockResources(energy: 20f, mana: 5f);
        _formulaEvaluator
            .Setup(m => m.Evaluate("mana_current * 2", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Result<float>.Success(10f));
        var action = new ActionDefinition
        {
            ActionId = "dynamic_cost",
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new() { ResourceId = "energy", Amount = 99f, Formula = "mana_current * 2" }
                }
            }
        };

        var result = _service.GetCostOptions(action, resources);

        Assert.True(result.IsSuccess);
        Assert.Equal(10f, result.Value.NormalCosts.Single().Amount);
    }

    [Fact]
    public void CanAfford_WithAlternativeCosts_ReturnsAffordableOptions()
    {
        // Arrange
        var resources = CreateMockResources(energy: 20f, mana: 100f);
        var action = new ActionDefinition
        {
            ActionId = "flexible_action",
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new ResourceCost { ResourceId = "energy", Amount = 50f }
                },
                AlternativeCosts = new List<AlternativeCostOption>
                {
                    new AlternativeCostOption
                    {
                        OptionId = "mana_option",
                        Description = "Pay with mana",
                        Costs = new List<ResourceCost>
                        {
                            new ResourceCost { ResourceId = "mana", Amount = 30f }
                        }
                    }
                }
            }
        };

        // Act
        var result = _service.CanAfford(action, resources);

        // Assert
        Assert.True(result.IsSuccess);
        var affordability = result.Value;
        Assert.Equal("flexible_action", affordability.ActionId);
        Assert.False(affordability.CanAfford); // Normal cost not affordable
        Assert.Single(affordability.AffordableOptionIds); // But alternative is
        Assert.Equal("mana_option", affordability.AffordableOptionIds[0]);
    }

    [Fact]
    public void GetAffordableActions_WithNullActions_ReturnsFailure()
    {
        // Arrange
        var resources = CreateMockResources();

        // Act
        var result = _service.GetAffordableActions(null!, resources);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be null", result.Error);
    }

    [Fact]
    public void GetCostOptions_WithNullAction_ReturnsFailure()
    {
        // Arrange
        var resources = CreateMockResources();

        // Act
        var result = _service.GetCostOptions(null!, resources);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be null", result.Error);
    }

    [Fact]
    public void CanAfford_WithNullResources_ReturnsFailure()
    {
        // Arrange
        var action = new ActionDefinition
        {
            ActionId = "test_action",
            Costs = new ActionCosts()
        };

        // Act
        var result = _service.CanAfford(action, null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be null", result.Error);
    }
}
