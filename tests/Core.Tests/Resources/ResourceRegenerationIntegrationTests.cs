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
    private readonly Mock<IRevisionedRuntimeFormulaEvaluator> _mockFormulaEvaluator;
    private readonly ILogger _logger;
    private readonly IEventBus _eventBus;
    private readonly IResourceRegenerationProcessor _processor;

    public ResourceRegenerationIntegrationTests()
    {
        _mockFormulaEvaluator = new Mock<IRevisionedRuntimeFormulaEvaluator>();
        _logger = new Mock<ILogger>().Object;
        _eventBus = new EventBus(_logger);
        _processor = new ResourceRegenerationProcessor(_mockFormulaEvaluator.Object);
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
        Assert.Equal(3f, result.Value.State.Resources["energy"].Current);
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
        Assert.Equal(55f, result.Value.State.Resources["mana"].Current);
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
        Assert.Equal(0f, result.Value.State.Resources["block"].Current);
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
        Assert.Equal(50f, result.Value.State.Resources["health"].Current);
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
        Assert.Equal(0f, result.Value.State.Resources["energy"].Current); // Should not change
    }



    [Fact]
    public void ProcessRegeneration_ReturnsMutationRecordsWithoutPublishingBeforeCommit()
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
        Assert.Null(publishedEvent);
        var record = Assert.Single(result.Value.Records);
        Assert.Equal("energy", record.ResourceId);
        Assert.Equal(0f, record.PreviousValue);
        Assert.Equal(3f, record.CurrentValue);
        Assert.Equal(RegenerationTiming.START_TURN, result.Value.Timing);
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
        Assert.Equal(3f, result.Value.State.Resources["energy"].Current);
        Assert.Equal(55f, result.Value.State.Resources["mana"].Current);
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
        Assert.Equal(10f, result.Value.State.Resources["energy"].Current); // Should cap at max
    }

    [Fact]
    public void ProcessRegeneration_FormulaUsesPinnedRevisionAndGenericSnapshotVariables()
    {
        const string revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        _mockFormulaEvaluator
            .Setup(evaluator => evaluator.EvaluateAtRevision(
                "stamina_regeneration",
                revision,
                It.Is<Dictionary<string, float>>(variables =>
                    variables["turn"] == 4 &&
                    variables["current"] == 2 &&
                    variables["maximum"] == 10 &&
                    variables["resources.focus.current"] == 5 &&
                    variables["resources.stamina.current"] == 2),
                0f))
            .Returns(Result<float>.Success(4));
        var stamina = new ResourceDefinition
        {
            ResourceId = "stamina",
            DisplayName = "Stamina",
            DefaultMax = 10,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                Timing = RegenerationTiming.START_TURN,
                Formula = "stamina_regeneration"
            }
        };
        var focus = new ResourceDefinition
        {
            ResourceId = "focus",
            DisplayName = "Focus",
            DefaultMax = 10
        };
        var state = new ResourceSet
        {
            OwnerId = "actor",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["stamina"] = new()
                {
                    ResourceId = "stamina",
                    Current = 2,
                    Maximum = 10,
                    Definition = stamina
                },
                ["focus"] = new()
                {
                    ResourceId = "focus",
                    Current = 5,
                    Maximum = 10,
                    Definition = focus
                }
            }
        };

        var result = _processor.ProcessRegeneration(
            state,
            RegenerationTiming.START_TURN,
            new ResourceRegenerationContext
            {
                ContentRevision = revision,
                Variables = new Dictionary<string, float>
                {
                    ["turn"] = 4,
                    ["current"] = 999
                }
            });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(6, result.Value.State.Current("stamina"));
        _mockFormulaEvaluator.VerifyAll();
    }

    [Fact]
    public void ProcessRegeneration_InvalidFormulaRejectsWholeBatchWithoutFallback()
    {
        _mockFormulaEvaluator
            .Setup(evaluator => evaluator.Evaluate(
                "broken_regeneration",
                It.IsAny<Dictionary<string, float>>(),
                0f))
            .Returns(Result<float>.Failure("formula failed"));
        var fixedDefinition = new ResourceDefinition
        {
            ResourceId = "a_fixed",
            DisplayName = "Fixed",
            DefaultMax = 10,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                Timing = RegenerationTiming.START_TURN,
                AmountPerTurn = 3
            }
        };
        var formulaDefinition = new ResourceDefinition
        {
            ResourceId = "z_formula",
            DisplayName = "Formula",
            DefaultMax = 10,
            Regeneration = new RegenerationConfig
            {
                Enabled = true,
                Timing = RegenerationTiming.START_TURN,
                AmountPerTurn = 9,
                Formula = "broken_regeneration"
            }
        };
        var state = new ResourceSet
        {
            OwnerId = "actor",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["a_fixed"] = new()
                {
                    ResourceId = "a_fixed",
                    Current = 1,
                    Maximum = 10,
                    Definition = fixedDefinition
                },
                ["z_formula"] = new()
                {
                    ResourceId = "z_formula",
                    Current = 1,
                    Maximum = 10,
                    Definition = formulaDefinition
                }
            }
        };

        var result = _processor.ProcessRegeneration(state, RegenerationTiming.START_TURN);

        Assert.True(result.IsFailure);
        Assert.Contains("formula failed", result.Error);
        Assert.Equal(1, state.Current("a_fixed"));
        Assert.Equal(1, state.Current("z_formula"));
    }


}
