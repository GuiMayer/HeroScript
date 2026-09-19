using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Text.Json;
using Core.Run;
using Core.Resources;
using Xunit;

namespace Core.Tests.Run;

/// <summary>
/// Comprehensive tests for Run namespace models
/// Covers DeckState, RunState, RunDefinition, RunMapNodeDefinition
/// </summary>
[Trait("Category", "Unit")]
public class RunModelsTests
{
    // ==================== RUN STATE TESTS ====================
    
    [Fact]
    public void RunState_DefaultConstruction_InitializesDefaults()
    {
        // Arrange & Act
        var runState = new RunState();
        
        // Assert
        Assert.Equal(Guid.Empty, runState.RunId);
        Assert.Equal(0, runState.Sequence);
        Assert.Equal("default", runState.ConfigName);
        Assert.Equal("player", runState.PlayerEntityId);
        Assert.Empty(runState.ResourceState.Resources);
        Assert.Null(runState.CurrentNodeId);
        Assert.NotNull(runState.Deck);
        Assert.Empty(runState.CardSelections);
        Assert.Empty(runState.Shops);
        Assert.Empty(runState.Preparations);
        Assert.Empty(runState.Metadata);
    }
    
    [Fact]
    public void RunState_DoesNotGenerateAmbientRunId()
    {
        // Arrange & Act
        var run1 = new RunState();
        var run2 = new RunState();
        
        // Assert
        Assert.Equal(Guid.Empty, run1.RunId);
        Assert.Equal(run1.RunId, run2.RunId);
    }
    
    [Fact]
    public void RunState_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var deckState = TestCardZones.FromDefinitions(("cards", new[] { "card1" }));
        
        // Act
        var runState = new RunState
        {
            RunId = runId,
            Sequence = 5,
            ConfigName = "hard_mode",
            PlayerEntityId = "hero_001",
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "credits", Amount = 150 },
                new ResourceAmount { ResourceId = "insight", Amount = 3 }),
            CurrentNodeId = "node_boss",
            Deck = deckState,
            Metadata = ImmutableDictionary<string, JsonElement>.Empty.Add(
                "difficulty",
                JsonSerializer.SerializeToElement("hard"))
        };
        
        // Assert
        Assert.Equal(runId, runState.RunId);
        Assert.Equal(5, runState.Sequence);
        Assert.Equal("hard_mode", runState.ConfigName);
        Assert.Equal("hero_001", runState.PlayerEntityId);
        Assert.Equal(150, runState.ResourceState.Current("credits"));
        Assert.Equal(3, runState.ResourceState.Current("insight"));
        Assert.Equal("node_boss", runState.CurrentNodeId);
        Assert.Single(runState.Deck.GetZoneInstanceIds("cards", TestCardZones.RunOwner));
        Assert.Single(runState.Metadata);
    }
    
    [Fact]
    public void RunState_Sequence_ChangesByReplacement()
    {
        // Arrange
        var runState = new RunState { Sequence = 1 };
        
        // Act
        var updated = runState with { Sequence = 10 };
        
        // Assert
        Assert.Equal(1, runState.Sequence);
        Assert.Equal(10, updated.Sequence);
    }
    
    [Fact]
    public void RunState_ResourceState_ChangesByReplacement()
    {
        // Arrange
        var runState = new RunState
        {
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "credits", Amount = 100 })
        };
        
        // Act
        var updated = runState with
        {
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "credits", Amount = 150 })
        };
        
        // Assert
        Assert.Equal(100, runState.ResourceState.Current("credits"));
        Assert.Equal(150, updated.ResourceState.Current("credits"));
    }
    
    [Fact]
    public void RunState_CustomResource_ChangesByReplacement()
    {
        // Arrange
        var runState = new RunState
        {
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "insight", Amount = 2 })
        };
        
        // Act
        var updated = runState with
        {
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "insight", Amount = 5 })
        };
        
        // Assert
        Assert.Equal(2, runState.ResourceState.Current("insight"));
        Assert.Equal(5, updated.ResourceState.Current("insight"));
    }
    
    [Fact]
    public void RunState_CurrentNodeId_CanBeNull()
    {
        // Arrange & Act
        var runState = new RunState { CurrentNodeId = null };
        
        // Assert
        Assert.Null(runState.CurrentNodeId);
    }
    
    [Fact]
    public void RunState_CurrentNodeId_CanBeSet()
    {
        // Arrange & Act
        var runState = new RunState { CurrentNodeId = "node_combat_1" };
        
        // Assert
        Assert.Equal("node_combat_1", runState.CurrentNodeId);
    }
    
    [Fact]
    public void RunState_IsRecord_SupportsWithExpression()
    {
        // Arrange
        var original = new RunState
        {
            ConfigName = "normal",
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "credits", Amount = 100 })
        };
        
        // Act
        var modified = original with
        {
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "credits", Amount = 200 })
        };
        
        // Assert
        Assert.Equal(100, original.ResourceState.Current("credits"));
        Assert.Equal(200, modified.ResourceState.Current("credits"));
        Assert.Equal("normal", modified.ConfigName);
    }
    
    // ==================== RUN DEFINITION TESTS ====================
    
    [Fact]
    public void RunDefinition_DefaultConstruction_InitializesDefaults()
    {
        // Arrange & Act
        var runDef = new RunDefinition();
        
        // Assert
        Assert.Equal("default_run", runDef.RunId);
        Assert.Empty(runDef.StartingResources);
        Assert.Equal(5, runDef.InitialPlayableCardCount);
        Assert.Empty(runDef.StartingCards);
        Assert.Empty(runDef.MapNodes);
        Assert.Empty(runDef.Metadata);
    }
    
    [Fact]
    public void RunDefinition_FullConstruction_SetsAllProperties()
    {
        // Arrange & Act
        var runDef = new RunDefinition
        {
            RunId = "ironclad_run",
            StartingResources = new Dictionary<string, float>
            {
                ["credits"] = 100,
                ["insight"] = 3
            },
            InitialPlayableCardCount = 6,
            StartingCards = new List<string> { "strike", "strike", "defend", "bash" },
            MapNodes = new List<RunMapNodeDefinition>
            {
                new()
                {
                    NodeId = "start",
                    Activity = new RunActivityDefinition { Type = RunActivityType.CardUpgrade }
                }
            },
            Metadata = new Dictionary<string, object> { ["difficulty"] = 1 }
        };
        
        // Assert
        Assert.Equal("ironclad_run", runDef.RunId);
        Assert.Equal(100, runDef.StartingResources["credits"]);
        Assert.Equal(3, runDef.StartingResources["insight"]);
        Assert.Equal(6, runDef.InitialPlayableCardCount);
        Assert.Equal(4, runDef.StartingCards.Count);
        Assert.Single(runDef.MapNodes);
        Assert.Single(runDef.Metadata);
    }
    
    [Fact]
    public void RunDefinition_InitialPlayableCardCount_DefaultsToFive()
    {
        // Arrange & Act
        var runDef = new RunDefinition();
        
        // Assert
        Assert.Equal(5, runDef.InitialPlayableCardCount);
    }
    
    [Fact]
    public void RunDefinition_StartingCards_CanContainDuplicates()
    {
        // Arrange & Act
        var runDef = new RunDefinition
        {
            StartingCards = new List<string>
            { 
                "strike", "strike", "strike", "strike", "strike",
                "defend", "defend", "defend", "defend", "defend"
            }
        };
        
        // Assert
        Assert.Equal(10, runDef.StartingCards.Count);
        Assert.Equal(5, runDef.StartingCards.Count(c => c == "strike"));
        Assert.Equal(5, runDef.StartingCards.Count(c => c == "defend"));
    }
    
    [Fact]
    public void RunDefinition_IsRecord_SupportsWithExpression()
    {
        // Arrange
        var original = new RunDefinition
        {
            RunId = "original",
            StartingResources = new Dictionary<string, float> { ["credits"] = 100 }
        };
        
        // Act
        var modified = original with
        {
            StartingResources = new Dictionary<string, float> { ["credits"] = 200 }
        };
        
        // Assert
        Assert.Equal(100, original.StartingResources["credits"]);
        Assert.Equal(200, modified.StartingResources["credits"]);
        Assert.Equal("original", modified.RunId);
    }
    
    // ==================== RUN MAP NODE DEFINITION TESTS ====================
    
    [Fact]
    public void RunMapNodeDefinition_DefaultConstruction_InitializesDefaults()
    {
        // Arrange & Act
        var node = new RunMapNodeDefinition();
        
        // Assert
        Assert.Equal(string.Empty, node.NodeId);
        Assert.Equal(RunActivityType.Encounter, node.Activity.Type);
        Assert.Empty(node.NextNodeIds);
        Assert.Empty(node.Metadata);
    }
    
    [Fact]
    public void RunMapNodeDefinition_FullConstruction_SetsAllProperties()
    {
        // Arrange & Act
        var node = new RunMapNodeDefinition
        {
            NodeId = "node_shop_1",
            Activity = new RunActivityDefinition
            {
                Type = RunActivityType.Shop,
                DefinitionId = "basic_shop"
            },
            NextNodeIds = new List<string> { "node_combat_5", "node_rest_2" },
            Metadata = new Dictionary<string, object> { ["shopkeeper"] = "merchant" }
        };
        
        // Assert
        Assert.Equal("node_shop_1", node.NodeId);
        Assert.Equal(RunActivityType.Shop, node.Activity.Type);
        Assert.Equal("basic_shop", node.Activity.DefinitionId);
        Assert.Equal(2, node.NextNodeIds.Count);
        Assert.Contains("node_combat_5", node.NextNodeIds);
        Assert.Contains("node_rest_2", node.NextNodeIds);
        Assert.Single(node.Metadata);
    }
    
    [Fact]
    public void RunMapNodeDefinition_Activity_DefaultsToEncounter()
    {
        // Arrange & Act
        var node = new RunMapNodeDefinition();
        
        // Assert
        Assert.Equal(RunActivityType.Encounter, node.Activity.Type);
    }
    
    [Fact]
    public void RunMapNodeDefinition_NextNodeIds_CanBeEmpty()
    {
        // Arrange & Act
        var node = new RunMapNodeDefinition
        {
            NodeId = "final_boss",
            NextNodeIds = new List<string>()
        };
        
        // Assert
        Assert.Empty(node.NextNodeIds);
    }
    
    [Fact]
    public void RunMapNodeDefinition_NextNodeIds_CanHaveMultiplePaths()
    {
        // Arrange & Act
        var node = new RunMapNodeDefinition
        {
            NodeId = "fork",
            NextNodeIds = new List<string> { "path_a", "path_b", "path_c" }
        };
        
        // Assert
        Assert.Equal(3, node.NextNodeIds.Count);
    }
    
    [Fact]
    public void RunMapNodeDefinition_IsRecord_SupportsWithExpression()
    {
        // Arrange
        var original = new RunMapNodeDefinition
        {
            NodeId = "node_1",
            Activity = new RunActivityDefinition { Type = RunActivityType.Encounter }
        };
        
        // Act
        var modified = original with
        {
            Activity = new RunActivityDefinition
            {
                Type = RunActivityType.Encounter,
                Parameters = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["tier"] = System.Text.Json.JsonSerializer.SerializeToElement("elite")
                }
            }
        };
        
        // Assert
        Assert.Empty(original.Activity.Parameters);
        Assert.Equal("elite", modified.Activity.Parameters["tier"].GetString());
        Assert.Equal("node_1", modified.NodeId);
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void RunState_ProgressionScenario_UsesGenericResources()
    {
        // Arrange - Start of run
        var runState = new RunState
        {
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "credits", Amount = 100 },
                new ResourceAmount { ResourceId = "insight", Amount = 0 }),
            CurrentNodeId = "node_start"
        };
        
        // Act - Win combat, gain gold and PP
        var resources = RunResourceTransitions.Gain(
            runState.ResourceState,
            [
                new ResourceAmount { ResourceId = "credits", Amount = 50 },
                new ResourceAmount { ResourceId = "insight", Amount = 1 }
            ],
            "test-progression").Value.State;
        var progressed = runState with
        {
            ResourceState = resources,
            CurrentNodeId = "node_shop_1"
        };
        
        // Assert
        Assert.Equal(100, runState.ResourceState.Current("credits"));
        Assert.Equal(150, progressed.ResourceState.Current("credits"));
        Assert.Equal(1, progressed.ResourceState.Current("insight"));
        Assert.Equal("node_shop_1", progressed.CurrentNodeId);
    }
    
    [Fact]
    public void RunDefinition_IroncladStarterDeck_Typical()
    {
        // Arrange & Act - Ironclad starting deck
        var ironcladRun = new RunDefinition
        {
            RunId = "ironclad_ascension_0",
            StartingResources = new Dictionary<string, float> { ["gold"] = 99 },
            InitialPlayableCardCount = 5,
            StartingCards = new List<string>
            {
                "strike", "strike", "strike", "strike", "strike",
                "defend", "defend", "defend", "defend", 
                "bash"
            }
        };
        
        // Assert
        Assert.Equal(10, ironcladRun.StartingCards.Count);
        Assert.Equal(5, ironcladRun.StartingCards.Count(c => c == "strike"));
        Assert.Equal(4, ironcladRun.StartingCards.Count(c => c == "defend"));
        Assert.Single(ironcladRun.StartingCards, c => c == "bash");
    }
    
    [Fact]
    public void RunMapNodeDefinition_LinearPath_SimpleMap()
    {
        // Arrange & Act - Simple linear path
        var node1 = new RunMapNodeDefinition
        {
            NodeId = "start",
            Activity = new RunActivityDefinition { Type = RunActivityType.Encounter },
            NextNodeIds = new List<string> { "node2" }
        };
        
        var node2 = new RunMapNodeDefinition
        {
            NodeId = "node2",
            Activity = new RunActivityDefinition { Type = RunActivityType.CardUpgrade },
            NextNodeIds = new List<string> { "node3" }
        };
        
        var node3 = new RunMapNodeDefinition
        {
            NodeId = "node3",
            Activity = new RunActivityDefinition
            {
                Type = RunActivityType.Encounter,
                Parameters = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["tier"] = System.Text.Json.JsonSerializer.SerializeToElement("boss")
                }
            },
            NextNodeIds = new List<string>()
        };
        
        // Assert
        Assert.Single(node1.NextNodeIds);
        Assert.Single(node2.NextNodeIds);
        Assert.Empty(node3.NextNodeIds);
    }
    
    [Fact]
    public void RunMapNodeDefinition_BranchingPath_MultipleChoices()
    {
        // Arrange & Act - Branching paths
        var fork = new RunMapNodeDefinition
        {
            NodeId = "fork_node",
            Activity = new RunActivityDefinition
            {
                Type = RunActivityType.Preparation,
                DefinitionId = "event"
            },
            NextNodeIds = new List<string> { "combat_path", "shop_path", "rest_path" }
        };
        
        // Assert - Player has 3 choices
        Assert.Equal(3, fork.NextNodeIds.Count);
    }
    
}
