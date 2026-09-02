using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Text.Json;
using Core.Run;
using Xunit;

namespace Core.Tests.Run;

/// <summary>
/// Comprehensive tests for Run namespace models
/// Covers DeckState, CardConsumeDestination, RunState, RunDefinition, RunMapNodeDefinition
/// </summary>
[Trait("Category", "Unit")]
public class RunModelsTests
{
    // ==================== CARD CONSUME DESTINATION ENUM TESTS ====================
    
    [Fact]
    public void CardConsumeDestination_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<CardConsumeDestination>();
        
        // Assert
        Assert.Contains(CardConsumeDestination.None, values);
        Assert.Contains(CardConsumeDestination.Discard, values);
        Assert.Contains(CardConsumeDestination.Exhaust, values);
        Assert.Equal(3, values.Length);
    }
    
    // ==================== DECK STATE TESTS ====================
    
    [Fact]
    public void DeckState_DefaultConstruction_InitializesEmptyCollections()
    {
        // Arrange & Act
        var deckState = new DeckState();
        
        // Assert
        Assert.Empty(deckState.DrawPile);
        Assert.Empty(deckState.Hand);
        Assert.Empty(deckState.DiscardPile);
        Assert.Empty(deckState.ExhaustPile);
    }
    
    [Fact]
    public void DeckState_FullConstruction_SetsAllPiles()
    {
        // Arrange & Act
        var deckState = new DeckState
        {
            DrawPile = new List<string> { "card1", "card2", "card3" },
            Hand = new List<string> { "card4", "card5" },
            DiscardPile = new List<string> { "card6" },
            ExhaustPile = new List<string> { "card7" }
        };
        
        // Assert
        Assert.Equal(3, deckState.DrawPile.Count);
        Assert.Equal(2, deckState.Hand.Count);
        Assert.Single(deckState.DiscardPile);
        Assert.Single(deckState.ExhaustPile);
    }
    
    [Fact]
    public void DeckState_IsRecord_SupportsWithExpression()
    {
        // Arrange
        var original = new DeckState
        {
            DrawPile = new List<string> { "card1", "card2" },
            Hand = new List<string> { "card3" }
        };
        
        // Act
        var modified = original with 
        { 
            Hand = new List<string> { "card3", "card4" } 
        };
        
        // Assert
        Assert.Equal(2, original.DrawPile.Count);
        Assert.Single(original.Hand);
        Assert.Equal(2, modified.DrawPile.Count);
        Assert.Equal(2, modified.Hand.Count);
    }
    
    [Fact]
    public void DeckState_DrawPile_CanContainMultipleCards()
    {
        // Arrange & Act
        var deckState = new DeckState
        {
            DrawPile = new List<string> 
            { 
                "strike", "defend", "bash", "strike", "defend" 
            }
        };
        
        // Assert
        Assert.Equal(5, deckState.DrawPile.Count);
        Assert.Equal("strike", deckState.DrawPile[0]);
        Assert.Equal("bash", deckState.DrawPile[2]);
    }
    
    [Fact]
    public void DeckState_Hand_RepresentsActiveCards()
    {
        // Arrange & Act
        var deckState = new DeckState
        {
            Hand = new List<string> { "card1", "card2", "card3", "card4", "card5" }
        };
        
        // Assert
        Assert.Equal(5, deckState.Hand.Count);
    }
    
    [Fact]
    public void DeckState_DiscardPile_AccumulatesPlayedCards()
    {
        // Arrange & Act
        var deckState = new DeckState
        {
            DiscardPile = new List<string> { "played1", "played2", "played3" }
        };
        
        // Assert
        Assert.Equal(3, deckState.DiscardPile.Count);
    }
    
    [Fact]
    public void DeckState_ExhaustPile_StoresExhaustedCards()
    {
        // Arrange & Act
        var deckState = new DeckState
        {
            ExhaustPile = new List<string> { "exhausted1", "exhausted2" }
        };
        
        // Assert
        Assert.Equal(2, deckState.ExhaustPile.Count);
    }
    
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
        Assert.Equal(0, runState.Gold);
        Assert.Equal(0, runState.PowerPoints);
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
        var deckState = new DeckState { Hand = new List<string> { "card1" } };
        
        // Act
        var runState = new RunState
        {
            RunId = runId,
            Sequence = 5,
            ConfigName = "hard_mode",
            PlayerEntityId = "hero_001",
            Gold = 150,
            PowerPoints = 3,
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
        Assert.Equal(150, runState.Gold);
        Assert.Equal(3, runState.PowerPoints);
        Assert.Equal("node_boss", runState.CurrentNodeId);
        Assert.Single(runState.Deck.Hand);
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
    public void RunState_Gold_ChangesByReplacement()
    {
        // Arrange
        var runState = new RunState { Gold = 100 };
        
        // Act
        var updated = runState with { Gold = 150 };
        
        // Assert
        Assert.Equal(100, runState.Gold);
        Assert.Equal(150, updated.Gold);
    }
    
    [Fact]
    public void RunState_PowerPoints_ChangesByReplacement()
    {
        // Arrange
        var runState = new RunState { PowerPoints = 2 };
        
        // Act
        var updated = runState with { PowerPoints = 5 };
        
        // Assert
        Assert.Equal(2, runState.PowerPoints);
        Assert.Equal(5, updated.PowerPoints);
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
            Gold = 100
        };
        
        // Act
        var modified = original with { Gold = 200 };
        
        // Assert
        Assert.Equal(100, original.Gold);
        Assert.Equal(200, modified.Gold);
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
        Assert.Equal(0, runDef.StartingGold);
        Assert.Equal(0, runDef.StartingPowerPoints);
        Assert.Equal(5, runDef.StartingHandSize);
        Assert.Empty(runDef.StartingDeck);
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
            StartingGold = 100,
            StartingPowerPoints = 3,
            StartingHandSize = 6,
            StartingDeck = new List<string> { "strike", "strike", "defend", "bash" },
            MapNodes = new List<RunMapNodeDefinition>
            {
                new() { NodeId = "start", NodeType = "rest" }
            },
            Metadata = new Dictionary<string, object> { ["difficulty"] = 1 }
        };
        
        // Assert
        Assert.Equal("ironclad_run", runDef.RunId);
        Assert.Equal(100, runDef.StartingGold);
        Assert.Equal(3, runDef.StartingPowerPoints);
        Assert.Equal(6, runDef.StartingHandSize);
        Assert.Equal(4, runDef.StartingDeck.Count);
        Assert.Single(runDef.MapNodes);
        Assert.Single(runDef.Metadata);
    }
    
    [Fact]
    public void RunDefinition_StartingHandSize_DefaultsToFive()
    {
        // Arrange & Act
        var runDef = new RunDefinition();
        
        // Assert
        Assert.Equal(5, runDef.StartingHandSize);
    }
    
    [Fact]
    public void RunDefinition_StartingDeck_CanContainDuplicates()
    {
        // Arrange & Act
        var runDef = new RunDefinition
        {
            StartingDeck = new List<string> 
            { 
                "strike", "strike", "strike", "strike", "strike",
                "defend", "defend", "defend", "defend", "defend"
            }
        };
        
        // Assert
        Assert.Equal(10, runDef.StartingDeck.Count);
        Assert.Equal(5, runDef.StartingDeck.Count(c => c == "strike"));
        Assert.Equal(5, runDef.StartingDeck.Count(c => c == "defend"));
    }
    
    [Fact]
    public void RunDefinition_IsRecord_SupportsWithExpression()
    {
        // Arrange
        var original = new RunDefinition
        {
            RunId = "original",
            StartingGold = 100
        };
        
        // Act
        var modified = original with { StartingGold = 200 };
        
        // Assert
        Assert.Equal(100, original.StartingGold);
        Assert.Equal(200, modified.StartingGold);
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
        Assert.Equal("combat", node.NodeType);
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
            NodeType = "shop",
            NextNodeIds = new List<string> { "node_combat_5", "node_rest_2" },
            Metadata = new Dictionary<string, object> { ["shopkeeper"] = "merchant" }
        };
        
        // Assert
        Assert.Equal("node_shop_1", node.NodeId);
        Assert.Equal("shop", node.NodeType);
        Assert.Equal(2, node.NextNodeIds.Count);
        Assert.Contains("node_combat_5", node.NextNodeIds);
        Assert.Contains("node_rest_2", node.NextNodeIds);
        Assert.Single(node.Metadata);
    }
    
    [Fact]
    public void RunMapNodeDefinition_NodeType_DefaultsToCombat()
    {
        // Arrange & Act
        var node = new RunMapNodeDefinition();
        
        // Assert
        Assert.Equal("combat", node.NodeType);
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
            NodeType = "combat"
        };
        
        // Act
        var modified = original with { NodeType = "elite" };
        
        // Assert
        Assert.Equal("combat", original.NodeType);
        Assert.Equal("elite", modified.NodeType);
        Assert.Equal("node_1", modified.NodeId);
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void DeckState_CombatScenario_TypicalCardFlow()
    {
        // Arrange - Start of combat
        var startDeck = new DeckState
        {
            DrawPile = new List<string> 
            { 
                "strike", "defend", "bash", "strike", "defend",
                "strike", "defend", "strike", "defend", "bash"
            },
            Hand = new List<string>(),
            DiscardPile = new List<string>(),
            ExhaustPile = new List<string>()
        };
        
        // Act - Draw 5 cards
        var afterDraw = startDeck with
        {
            DrawPile = startDeck.DrawPile.Skip(5).ToList(),
            Hand = startDeck.DrawPile.Take(5).ToList()
        };
        
        // Assert
        Assert.Equal(5, afterDraw.DrawPile.Count);
        Assert.Equal(5, afterDraw.Hand.Count);
    }
    
    [Fact]
    public void RunState_ProgressionScenario_GoldAndPowerPoints()
    {
        // Arrange - Start of run
        var runState = new RunState
        {
            Gold = 100,
            PowerPoints = 0,
            CurrentNodeId = "node_start"
        };
        
        // Act - Win combat, gain gold and PP
        var progressed = runState with
        {
            Gold = runState.Gold + 50,
            PowerPoints = runState.PowerPoints + 1,
            CurrentNodeId = "node_shop_1"
        };
        
        // Assert
        Assert.Equal(100, runState.Gold);
        Assert.Equal(150, progressed.Gold);
        Assert.Equal(1, progressed.PowerPoints);
        Assert.Equal("node_shop_1", progressed.CurrentNodeId);
    }
    
    [Fact]
    public void RunDefinition_IroncladStarterDeck_Typical()
    {
        // Arrange & Act - Ironclad starting deck
        var ironcladRun = new RunDefinition
        {
            RunId = "ironclad_ascension_0",
            StartingGold = 99,
            StartingPowerPoints = 0,
            StartingHandSize = 5,
            StartingDeck = new List<string>
            {
                "strike", "strike", "strike", "strike", "strike",
                "defend", "defend", "defend", "defend", 
                "bash"
            }
        };
        
        // Assert
        Assert.Equal(10, ironcladRun.StartingDeck.Count);
        Assert.Equal(5, ironcladRun.StartingDeck.Count(c => c == "strike"));
        Assert.Equal(4, ironcladRun.StartingDeck.Count(c => c == "defend"));
        Assert.Single(ironcladRun.StartingDeck, c => c == "bash");
    }
    
    [Fact]
    public void RunMapNodeDefinition_LinearPath_SimpleMap()
    {
        // Arrange & Act - Simple linear path
        var node1 = new RunMapNodeDefinition
        {
            NodeId = "start",
            NodeType = "combat",
            NextNodeIds = new List<string> { "node2" }
        };
        
        var node2 = new RunMapNodeDefinition
        {
            NodeId = "node2",
            NodeType = "rest",
            NextNodeIds = new List<string> { "node3" }
        };
        
        var node3 = new RunMapNodeDefinition
        {
            NodeId = "node3",
            NodeType = "boss",
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
            NodeType = "event",
            NextNodeIds = new List<string> { "combat_path", "shop_path", "rest_path" }
        };
        
        // Assert - Player has 3 choices
        Assert.Equal(3, fork.NextNodeIds.Count);
    }
    
    [Fact]
    public void DeckState_ExhaustScenario_PermanentRemoval()
    {
        // Arrange - Deck with exhaust mechanic
        var deckState = new DeckState
        {
            Hand = new List<string> { "strike", "defend", "finisher", "bash" },
            ExhaustPile = new List<string>()
        };
        
        // Act - Play finisher (exhausts on use)
        var afterExhaust = deckState with
        {
            Hand = new List<string> { "strike", "defend", "bash" },
            ExhaustPile = new List<string> { "finisher" }
        };
        
        // Assert
        Assert.Equal(3, afterExhaust.Hand.Count);
        Assert.Single(afterExhaust.ExhaustPile);
        Assert.Contains("finisher", afterExhaust.ExhaustPile);
    }
}
