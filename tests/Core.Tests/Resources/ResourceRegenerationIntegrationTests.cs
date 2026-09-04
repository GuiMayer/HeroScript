using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Resources;

/// <summary>
/// Integration tests for resource regeneration system.
/// Tests the complete flow using AmountPerTurn (no formula dependencies).
/// </summary>
public class ResourceRegenerationIntegrationTests
{
    private readonly Mock<IMathEngine> _mockMathEngine;
    private readonly ILogger _logger;
    private readonly IEventBus _eventBus;
    private readonly IResourceRegenerationProcessor _processor;

    public ResourceRegenerationIntegrationTests()
    {
        _mockMathEngine = new Mock<IMathEngine>();
        _logger = new Mock<ILogger>().Object;
        _eventBus = new EventBus(_logger);
        _processor = new ResourceRegenerationProcessor(_mockMathEngine.Object, _logger, _eventBus);
    }

    [Fact]
    public void ProcessRegeneration_EnergyRegenerates3AtStartTurn()
    {
        // Arrange
        var energyDef = new ResourceDefinition
        {
            ResourceId = "energy",
            DisplayName = "Energy",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = 3,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = 3,
                Timing = RegenerationTiming.START_TURN
            }
        };

        var energyPool = new ResourcePool
        {
            ResourceId = "energy",
            Current = 0,
            Maximum = 10,
            Minimum = 0,
            Definition = energyDef
        };

        var entityState = new ResourceSet
        {
            OwnerId = "player1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["energy"] = energyPool
            }
        };

        // Act
        var result = _processor.ProcessRegeneration(entityState, RegenerationTiming.START_TURN);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3f, result.Value.Resources["energy"].Current);
    }

    [Fact]
    public void ProcessRegeneration_ManaRegenerates5AtStartTurn()
    {
        // Arrange
        var manaDef = new ResourceDefinition
        {
            ResourceId = "mana",
            DisplayName = "Mana",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 100,
            DefaultCurrent = 100,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = 5,
                Timing = RegenerationTiming.START_TURN
            }
        };

        var manaPool = new ResourcePool
        {
            ResourceId = "mana",
            Current = 50,
            Maximum = 100,
            Minimum = 0,
            Definition = manaDef
        };

        var entityState = new ResourceSet
        {
            OwnerId = "player1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["mana"] = manaPool
            }
        };

        // Act
        var result = _processor.ProcessRegeneration(entityState, RegenerationTiming.START_TURN);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(55f, result.Value.Resources["mana"].Current);
    }

    [Fact]
    public void ProcessRegeneration_BlockZerosAtEndTurn()
    {
        // Arrange
        var blockDef = new ResourceDefinition
        {
            ResourceId = "block",
            DisplayName = "Block",
            Category = ResourceCategory.TEMPORARY,
            DefaultMin = 0,
            DefaultMax = 999,
            DefaultCurrent = 0,
            CanExceedMax = true,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = -999,
                Timing = RegenerationTiming.END_TURN
            }
        };

        var blockPool = new ResourcePool
        {
            ResourceId = "block",
            Current = 15,
            Maximum = 999,
            Minimum = 0,
            Definition = blockDef
        };

        var entityState = new ResourceSet
        {
            OwnerId = "player1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["block"] = blockPool
            }
        };

        // Act
        var result = _processor.ProcessRegeneration(entityState, RegenerationTiming.END_TURN);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0f, result.Value.Resources["block"].Current);
    }

    [Fact]
    public void ProcessRegeneration_ResourcesWithoutRegenerationDoNotChange()
    {
        // Arrange
        var healthDef = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultMin = 0,
            DefaultMax = 100,
            DefaultCurrent = 100,
            Regeneration = null
        };

        var healthPool = new ResourcePool
        {
            ResourceId = "health",
            Current = 50,
            Maximum = 100,
            Minimum = 0,
            Definition = healthDef
        };

        var entityState = new ResourceSet
        {
            OwnerId = "player1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = healthPool
            }
        };

        // Act
        var result = _processor.ProcessRegeneration(entityState, RegenerationTiming.START_TURN);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(50f, result.Value.Resources["health"].Current);
    }

    [Fact]
    public void ProcessRegeneration_IncorrectTimingDoesNotRegenerate()
    {
        // Arrange
        var energyDef = new ResourceDefinition
        {
            ResourceId = "energy",
            DisplayName = "Energy",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = 3,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = 3,
                Timing = RegenerationTiming.START_TURN
            }
        };

        var energyPool = new ResourcePool
        {
            ResourceId = "energy",
            Current = 0,
            Maximum = 10,
            Minimum = 0,
            Definition = energyDef
        };

        var entityState = new ResourceSet
        {
            OwnerId = "player1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["energy"] = energyPool
            }
        };

        // Act - Call with END_TURN instead of START_TURN
        var result = _processor.ProcessRegeneration(entityState, RegenerationTiming.END_TURN);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0f, result.Value.Resources["energy"].Current); // Should not change
    }



    [Fact]
    public void ProcessRegeneration_EventsArePublished()
    {
        // Arrange
        ResourceRegeneratedEvent? publishedEvent = null;
        _eventBus.Subscribe<ResourceRegeneratedEvent>(evt => publishedEvent = evt);

        var energyDef = new ResourceDefinition
        {
            ResourceId = "energy",
            DisplayName = "Energy",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = 3,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = 3,
                Timing = RegenerationTiming.START_TURN
            }
        };

        var energyPool = new ResourcePool
        {
            ResourceId = "energy",
            Current = 0,
            Maximum = 10,
            Minimum = 0,
            Definition = energyDef
        };

        var entityState = new ResourceSet
        {
            OwnerId = "player1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["energy"] = energyPool
            }
        };

        // Act
        var result = _processor.ProcessRegeneration(entityState, RegenerationTiming.START_TURN);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(publishedEvent);
        Assert.Equal("player1", publishedEvent.OwnerId);
        Assert.Equal("energy", publishedEvent.ResourceId);
        Assert.Equal(0f, publishedEvent.OldValue);
        Assert.Equal(3f, publishedEvent.NewValue);
        Assert.Equal(3f, publishedEvent.Amount);
        Assert.Equal(RegenerationTiming.START_TURN, publishedEvent.Timing);
    }

    [Fact]
    public void ProcessRegeneration_MultipleResourcesRegenerateSimultaneously()
    {
        // Arrange
        var energyDef = new ResourceDefinition
        {
            ResourceId = "energy",
            DisplayName = "Energy",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = 3,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = 3,
                Timing = RegenerationTiming.START_TURN
            }
        };

        var manaDef = new ResourceDefinition
        {
            ResourceId = "mana",
            DisplayName = "Mana",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 100,
            DefaultCurrent = 100,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = 5,
                Timing = RegenerationTiming.START_TURN
            }
        };

        var entityState = new ResourceSet
        {
            OwnerId = "player1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["energy"] = new ResourcePool
                {
                    ResourceId = "energy",
                    Current = 0,
                    Maximum = 10,
                    Minimum = 0,
                    Definition = energyDef
                },
                ["mana"] = new ResourcePool
                {
                    ResourceId = "mana",
                    Current = 50,
                    Maximum = 100,
                    Minimum = 0,
                    Definition = manaDef
                }
            }
        };

        // Act
        var result = _processor.ProcessRegeneration(entityState, RegenerationTiming.START_TURN);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3f, result.Value.Resources["energy"].Current);
        Assert.Equal(55f, result.Value.Resources["mana"].Current);
    }

    [Fact]
    public void ProcessRegeneration_RespectsMinMax()
    {
        // Arrange
        var energyDef = new ResourceDefinition
        {
            ResourceId = "energy",
            DisplayName = "Energy",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = 3,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                AmountPerTurn = 100, // Try to regenerate more than max
                Timing = RegenerationTiming.START_TURN
            }
        };

        var energyPool = new ResourcePool
        {
            ResourceId = "energy",
            Current = 5,
            Maximum = 10,
            Minimum = 0,
            Definition = energyDef
        };

        var entityState = new ResourceSet
        {
            OwnerId = "player1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["energy"] = energyPool
            }
        };

        // Act
        var result = _processor.ProcessRegeneration(entityState, RegenerationTiming.START_TURN);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(10f, result.Value.Resources["energy"].Current); // Should cap at max
    }


}
