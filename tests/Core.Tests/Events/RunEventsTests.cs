using System;
using System.Collections.Generic;
using Core.Events;
using Core.Events.Domain;
using Core.Resources;
using Core.CardZones;
using Xunit;

namespace Core.Tests.Events;

/// <summary>
/// Comprehensive tests for Run-related domain events
/// Covers all run progression events: resources, cards, shop, deck, rewards
/// </summary>
[Trait("Category", "Unit")]
public class RunEventsTests
{
    // ==================== RUN STARTED EVENT TESTS ====================
    
    [Fact]
    public void RunStartedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var evt = new RunStartedEvent(
            runId: runId,
            configName: "ironclad_ascension_5",
            playerEntityId: "player_ironclad",
            startingResources: new Dictionary<string, float>
            {
                ["gold"] = 99,
                ["power_points"] = 0
            }
        );
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal("ironclad_ascension_5", evt.ConfigName);
        Assert.Equal("player_ironclad", evt.PlayerEntityId);
        Assert.Equal(99, evt.StartingResources["gold"]);
        Assert.Equal(0, evt.StartingResources["power_points"]);
        Assert.Equal(nameof(RunStartedEvent), evt.EventType);
        Assert.Equal(EventCategory.RUN, evt.Category);
        Assert.Equal(EventSeverity.INFO, evt.Severity);
        Assert.Equal("player_ironclad", evt.Subject);
        Assert.Equal("run_started", evt.Verb);
        Assert.Equal(runId.ToString(), evt.Target);
    }
    
    [Fact]
    public void RunStartedEvent_PayloadContainsAllData()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var resources = new Dictionary<string, float>
        {
            ["gold"] = 50,
            ["power_points"] = 10
        };
        var evt = new RunStartedEvent(runId, "base_config", "hero", resources);
        
        // Assert
        Assert.Equal(4, evt.Payload.Count);
        Assert.Equal(runId, evt.Payload["runId"]);
        Assert.Equal("base_config", evt.Payload["configName"]);
        Assert.Equal("hero", evt.Payload["playerEntityId"]);
        Assert.Equal(resources, evt.Payload["startingResources"]);
    }
    
    // ==================== RUN ENDED EVENT TESTS ====================
    
    [Fact]
    public void RunEndedEvent_Victory_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var evt = new RunEndedEvent(runId, "victory");
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal("victory", evt.Outcome);
        Assert.Equal(nameof(RunEndedEvent), evt.EventType);
        Assert.Equal(EventCategory.RUN, evt.Category);
        Assert.Equal(EventSeverity.INFO, evt.Severity);
        Assert.Equal(runId.ToString(), evt.Subject);
        Assert.Equal("run_ended", evt.Verb);
        Assert.Equal("victory", evt.Target);
    }
    
    [Fact]
    public void RunEndedEvent_PayloadContainsOutcome()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var evt = new RunEndedEvent(runId, "defeat");
        
        // Assert
        Assert.Equal(2, evt.Payload.Count);
        Assert.Equal(runId, evt.Payload["runId"]);
        Assert.Equal("defeat", evt.Payload["outcome"]);
    }
    
    // ==================== ECONOMY CHANGED EVENT TESTS ====================
    
    [Fact]
    public void RunResourceChangedEvent_Gain_CalculatesDelta()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var evt = new RunResourceChangedEvent(
            runId, "gold", ResourceValueField.Current, ResourceMutationOperation.Add, 100, 150);
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal("gold", evt.ResourceId);
        Assert.Equal(100, evt.OldValue);
        Assert.Equal(150, evt.NewValue);
        Assert.Equal(50, evt.ValueDelta);
        Assert.Equal(nameof(RunResourceChangedEvent), evt.EventType);
        Assert.Equal(EventCategory.RUN, evt.Category);
        Assert.Equal("run_resource_changed", evt.Verb);
        Assert.Equal("gold", evt.Target);
    }
    
    [Fact]
    public void RunResourceChangedEvent_Loss_HasNegativeDelta()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var evt = new RunResourceChangedEvent(
            runId, "power_points", ResourceValueField.Current, ResourceMutationOperation.Subtract, 50, 30);
        
        // Assert
        Assert.Equal("power_points", evt.ResourceId);
        Assert.Equal(50, evt.OldValue);
        Assert.Equal(30, evt.NewValue);
        Assert.Equal(-20, evt.ValueDelta);
    }
    
    [Fact]
    public void RunResourceChangedEvent_PayloadIncludesDelta()
    {
        // Arrange & Act
        var evt = new RunResourceChangedEvent(
            Guid.NewGuid(), "gold", ResourceValueField.Current, ResourceMutationOperation.Add, 75, 125);
        
        // Assert
        Assert.Equal(7, evt.Payload.Count);
        Assert.Equal(75f, evt.Payload["oldValue"]);
        Assert.Equal(125f, evt.Payload["newValue"]);
        Assert.Equal(50f, evt.Payload["delta"]);
    }
    
    [Fact]
    public void CardZonesTransitionedEvent_PreservesPurposeFreeFlowEvidence()
    {
        var runId = Guid.NewGuid();
        var steps = new[]
        {
            new CardZoneFlowStepRecord
            {
                FlowId = "prepare",
                StepId = "move",
                Operation = CardZoneOperation.Move,
                SourceAddress = "$run::hero::inventory",
                TargetAddress = "$run::hero::prepared",
                StateHash = "after"
            }
        };

        var evt = new CardZonesTransitionedEvent(
            runId, "run.started", "topology-hash", steps);

        Assert.Equal(runId, evt.RunId);
        Assert.Equal("run.started", evt.Boundary);
        Assert.Equal("topology-hash", evt.TopologyHash);
        Assert.Equal("prepare", Assert.Single(evt.Steps).FlowId);
        Assert.Equal(nameof(CardZonesTransitionedEvent), evt.EventType);
        Assert.Equal("card_zones_transitioned", evt.Verb);
        Assert.Equal(1, evt.Payload["count"]);
    }
    
    // ==================== REWARD GENERATED EVENT TESTS ====================
    
    [Fact]
    public void RewardGeneratedEvent_ThreeCards_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var selectionId = Guid.NewGuid();
        var offeredCards = new List<string> { "anger", "battle_trance", "bloodletting" };
        
        // Act
        var evt = new RewardGeneratedEvent(runId, selectionId, offeredCards);
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal(selectionId, evt.SelectionId);
        Assert.Equal(3, evt.OfferedCardIds.Count);
        Assert.Contains("anger", evt.OfferedCardIds);
        Assert.Equal(nameof(RewardGeneratedEvent), evt.EventType);
        Assert.Equal("reward_generated", evt.Verb);
        Assert.Equal(selectionId.ToString(), evt.Target);
    }
    
    [Fact]
    public void RewardGeneratedEvent_PayloadContainsAllCards()
    {
        // Arrange
        var offeredCards = new List<string> { "card1", "card2", "card3" };
        
        // Act
        var evt = new RewardGeneratedEvent(Guid.NewGuid(), Guid.NewGuid(), offeredCards);
        
        // Assert
        Assert.Equal(3, evt.Payload.Count);
        Assert.IsType<string[]>(evt.Payload["offeredCardIds"]);
        var payloadCards = (string[])evt.Payload["offeredCardIds"];
        Assert.Equal(3, payloadCards.Length);
    }
    
    // ==================== CARD REWARD PICKED EVENT TESTS ====================
    
    [Fact]
    public void CardRewardPickedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var selectionId = Guid.NewGuid();
        
        // Act
        var evt = new CardRewardPickedEvent(runId, selectionId, "immolate");
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal(selectionId, evt.SelectionId);
        Assert.Equal("immolate", evt.PickedCardId);
        Assert.Equal(nameof(CardRewardPickedEvent), evt.EventType);
        Assert.Equal("card_reward_picked", evt.Verb);
        Assert.Equal("immolate", evt.Target);
    }
    
    // ==================== CARD REWARD REROLLED EVENT TESTS ====================
    
    [Fact]
    public void CardRewardRerolledEvent_WithCost_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var selectionId = Guid.NewGuid();
        
        // Act
        var evt = new CardRewardRerolledEvent(runId, selectionId, 50);
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal(selectionId, evt.SelectionId);
        Assert.Equal(50, evt.Cost);
        Assert.Equal(nameof(CardRewardRerolledEvent), evt.EventType);
        Assert.Equal("card_reward_rerolled", evt.Verb);
    }
    
    // ==================== CARD DECOMPOSED EVENT TESTS ====================
    
    [Fact]
    public void CardDecomposedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var evt = new CardDecomposedEvent(runId, "strike", 25);
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal("strike", evt.CardId);
        Assert.Equal(25, evt.PowerPointsGained);
        Assert.Equal(nameof(CardDecomposedEvent), evt.EventType);
        Assert.Equal("card_decomposed", evt.Verb);
        Assert.Equal("strike", evt.Target);
    }
    
    [Fact]
    public void CardDecomposedEvent_PayloadContainsPowerPoints()
    {
        // Arrange & Act
        var evt = new CardDecomposedEvent(Guid.NewGuid(), "curse", 10);
        
        // Assert
        Assert.Equal(3, evt.Payload.Count);
        Assert.Equal(10, evt.Payload["powerPointsGained"]);
    }
    
    // ==================== SHOP OPENED EVENT TESTS ====================
    
    [Fact]
    public void ShopOpenedEvent_WithItems_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var itemIds = new List<string> { "potion_health", "relic_anchor", "card_remove" };
        
        // Act
        var evt = new ShopOpenedEvent(runId, "merchant_act1", itemIds);
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal("merchant_act1", evt.ShopId);
        Assert.Equal(3, evt.ItemIds.Count);
        Assert.Contains("relic_anchor", evt.ItemIds);
        Assert.Equal(nameof(ShopOpenedEvent), evt.EventType);
        Assert.Equal("shop_opened", evt.Verb);
        Assert.Equal("merchant_act1", evt.Target);
    }
    
    [Fact]
    public void ShopOpenedEvent_PayloadContainsItemArray()
    {
        // Arrange
        var itemIds = new List<string> { "item1", "item2" };
        
        // Act
        var evt = new ShopOpenedEvent(Guid.NewGuid(), "shop", itemIds);
        
        // Assert
        Assert.IsType<string[]>(evt.Payload["itemIds"]);
    }
    
    // ==================== SHOP ITEM PURCHASED EVENT TESTS ====================
    
    [Fact]
    public void ShopItemPurchasedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var evt = new ShopItemPurchasedEvent(runId, "merchant_act1", "potion_health", 50);
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal("merchant_act1", evt.ShopId);
        Assert.Equal("potion_health", evt.ItemId);
        Assert.Equal(50, evt.GoldSpent);
        Assert.Equal(nameof(ShopItemPurchasedEvent), evt.EventType);
        Assert.Equal("shop_item_purchased", evt.Verb);
        Assert.Equal("potion_health", evt.Target);
    }
    
    // ==================== SHOP REROLLED EVENT TESTS ====================
    
    [Fact]
    public void ShopRerolledEvent_WithCost_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var evt = new ShopRerolledEvent(runId, "merchant_act2", 25);
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal("merchant_act2", evt.ShopId);
        Assert.Equal(25, evt.GoldSpent);
        Assert.Equal(nameof(ShopRerolledEvent), evt.EventType);
        Assert.Equal("shop_rerolled", evt.Verb);
    }
    
    // ==================== PREPARATION APPLIED EVENT TESTS ====================
    
    [Fact]
    public void PreparationAppliedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act
        var evt = new PreparationAppliedEvent(runId, "rest_heal");
        
        // Assert
        Assert.Equal(runId, evt.RunId);
        Assert.Equal("rest_heal", evt.PreparationId);
        Assert.Equal(nameof(PreparationAppliedEvent), evt.EventType);
        Assert.Equal("preparation_applied", evt.Verb);
        Assert.Equal("rest_heal", evt.Target);
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void Scenario_RunProgression_StartToEnd()
    {
        // Arrange - Run starts
        var runId = Guid.NewGuid();
        var startEvent = new RunStartedEvent(
            runId, 
            "ironclad_default", 
            "player_ironclad", 
            new Dictionary<string, float>
            {
                ["gold"] = 99,
                ["power_points"] = 0
            }
        );
        
        // Act - Gain gold from combat
        var goldGainEvent = new RunResourceChangedEvent(
            runId, "gold", ResourceValueField.Current, ResourceMutationOperation.Add, 99, 149);
        
        // Gain power points from card decompose
        var ppGainEvent = new RunResourceChangedEvent(
            runId, "power_points", ResourceValueField.Current, ResourceMutationOperation.Add, 0, 25);
        
        // Run ends
        var endEvent = new RunEndedEvent(runId, "victory");
        
        // Assert
        Assert.Equal(runId, startEvent.RunId);
        Assert.Equal(runId, goldGainEvent.RunId);
        Assert.Equal(runId, ppGainEvent.RunId);
        Assert.Equal(runId, endEvent.RunId);
        Assert.Equal(50, goldGainEvent.ValueDelta);
        Assert.Equal(25, ppGainEvent.ValueDelta);
        Assert.Equal("victory", endEvent.Outcome);
    }
    
    [Fact]
    public void Scenario_CardRewardFlow_GeneratePickAdd()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var selectionId = Guid.NewGuid();
        
        // Act - Reward generated
        var rewardEvent = new RewardGeneratedEvent(
            runId, 
            selectionId,
            new List<string> { "anger", "armaments", "battle_trance" }
        );
        
        // Player picks a card
        var pickEvent = new CardRewardPickedEvent(runId, selectionId, "armaments");
        
        // Assert
        Assert.Equal(selectionId, rewardEvent.SelectionId);
        Assert.Equal(selectionId, pickEvent.SelectionId);
        Assert.Equal("armaments", pickEvent.PickedCardId);
    }
    
    [Fact]
    public void Scenario_ShopTransaction_BuyAndReroll()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var shopItems = new List<string> { "potion_health", "relic_bag", "card_remove" };
        
        // Act - Shop opens
        var openEvent = new ShopOpenedEvent(runId, "merchant", shopItems);
        
        // Buy item
        var purchaseEvent = new ShopItemPurchasedEvent(runId, "merchant", "potion_health", 50);
        
        // Lose gold
        var goldLossEvent = new RunResourceChangedEvent(
            runId, "gold", ResourceValueField.Current, ResourceMutationOperation.Subtract, 100, 50);
        
        // Reroll shop
        var rerollEvent = new ShopRerolledEvent(runId, "merchant", 25);
        
        // Assert
        Assert.Equal("merchant", openEvent.ShopId);
        Assert.Equal(3, openEvent.ItemIds.Count);
        Assert.Equal(50, purchaseEvent.GoldSpent);
        Assert.Equal(-50, goldLossEvent.ValueDelta);
        Assert.Equal(25, rerollEvent.GoldSpent);
    }
    
    [Fact]
    public void Scenario_CardDecomposition_GainPowerPoints()
    {
        // Arrange
        var runId = Guid.NewGuid();
        
        // Act - Decompose strike card
        var decomposeEvent = new CardDecomposedEvent(runId, "strike", 25);
        
        // Gain power points
        var ppEvent = new RunResourceChangedEvent(
            runId, "power_points", ResourceValueField.Current, ResourceMutationOperation.Add, 0, 25);
        
        // Assert
        Assert.Equal("strike", decomposeEvent.CardId);
        Assert.Equal(25, decomposeEvent.PowerPointsGained);
        Assert.Equal(25, ppEvent.ValueDelta);
        Assert.Equal("power_points", ppEvent.ResourceId);
    }
}
