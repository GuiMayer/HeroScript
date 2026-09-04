using System;
using System.Collections.Generic;
using Core.Events;
using Core.Events.Domain;
using Core.Effects;
using Xunit;

namespace Core.Tests.Events;

/// <summary>
/// Comprehensive tests for Events namespace - GameEvent base class and Domain events
/// Covers GameEvent, EventCategory, EventSeverity, and multiple domain event types
/// </summary>
[Trait("Category", "Unit")]
public class GameEventTests
{
    // ==================== EVENT SEVERITY ENUM TESTS ====================
    
    [Fact]
    public void EventSeverity_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EventSeverity>();
        
        // Assert
        Assert.Contains(EventSeverity.DEBUG, values);
        Assert.Contains(EventSeverity.INFO, values);
        Assert.Contains(EventSeverity.WARN, values);
        Assert.Contains(EventSeverity.ANOMALY, values);
    }
    
    // ==================== EVENT CATEGORY ENUM TESTS ====================
    
    [Fact]
    public void EventCategory_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EventCategory>();
        
        // Assert
        Assert.Contains(EventCategory.COMBAT, values);
        Assert.Contains(EventCategory.PIPELINE, values);
        Assert.Contains(EventCategory.META, values);
        Assert.Contains(EventCategory.CONFIG, values);
        Assert.Contains(EventCategory.REALITY_BEND, values);
        Assert.Contains(EventCategory.RUN, values);
        Assert.Contains(EventCategory.GAME, values);
    }
    
    // ==================== GAME EVENT BASE CLASS TESTS ====================
    
    [Fact]
    public void GameEvent_DefaultConstruction_UsesDeterministicSentinels()
    {
        // Arrange & Act
        var evt = new GameEvent();
        
        // Assert
        Assert.Equal(Guid.Empty, evt.EventId);
        Assert.Equal(DateTime.UnixEpoch, evt.Timestamp);
    }
    
    [Fact]
    public void GameEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;
        var payload = new Dictionary<string, object> { ["damage"] = 10 };
        var stateBefore = new Dictionary<string, object> { ["hp"] = 100 };
        var stateAfter = new Dictionary<string, object> { ["hp"] = 90 };
        var delta = new Dictionary<string, object> { ["hp_change"] = -10 };
        
        // Act
        var evt = new GameEvent
        {
            EventId = eventId,
            Timestamp = timestamp,
            EventType = "DamageDealt",
            Turn = 5,
            Sequence = 42,
            Category = EventCategory.COMBAT,
            Severity = EventSeverity.INFO,
            Subject = "hero",
            Verb = "dealt_damage",
            Target = "enemy1",
            Payload = payload,
            StateBefore = stateBefore,
            StateAfter = stateAfter,
            Delta = delta
        };
        
        // Assert
        Assert.Equal(eventId, evt.EventId);
        Assert.Equal(timestamp, evt.Timestamp);
        Assert.Equal("DamageDealt", evt.EventType);
        Assert.Equal(5, evt.Turn);
        Assert.Equal(42, evt.Sequence);
        Assert.Equal(EventCategory.COMBAT, evt.Category);
        Assert.Equal(EventSeverity.INFO, evt.Severity);
        Assert.Equal("hero", evt.Subject);
        Assert.Equal("dealt_damage", evt.Verb);
        Assert.Equal("enemy1", evt.Target);
        Assert.Single(payload);
        Assert.Single(stateBefore);
        Assert.Single(stateAfter);
        Assert.Single(delta);
    }
    
    [Fact]
    public void GameEvent_IsImmutable()
    {
        // Arrange
        var evt = new GameEvent
        {
            EventType = "TestEvent",
            Turn = 1
        };
        
        // Act - Using 'with' to create modified copy
        var modified = evt with { Turn = 2 };
        
        // Assert - Original unchanged
        Assert.Equal(1, evt.Turn);
        Assert.Equal(2, modified.Turn);
    }
    
    [Fact]
    public void GameEvent_Payload_CanBeEmpty()
    {
        // Arrange & Act
        var evt = new GameEvent();
        
        // Assert
        Assert.Empty(evt.Payload);
    }
    
    [Fact]
    public void GameEvent_StateFields_CanBeNull()
    {
        // Arrange & Act
        var evt = new GameEvent
        {
            StateBefore = null,
            StateAfter = null,
            Delta = null
        };
        
        // Assert
        Assert.Null(evt.StateBefore);
        Assert.Null(evt.StateAfter);
        Assert.Null(evt.Delta);
    }
    
    // ==================== COMBAT STARTED EVENT TESTS ====================
    
    [Fact]
    public void CombatStartedEvent_DefaultConstruction_SetsDefaults()
    {
        // Arrange & Act
        var evt = new CombatStartedEvent();
        
        // Assert
        Assert.Equal(nameof(CombatStartedEvent), evt.EventType);
        Assert.Equal(EventCategory.COMBAT, evt.Category);
        Assert.Equal(EventSeverity.INFO, evt.Severity);
        Assert.Equal("CombatSystem", evt.Subject);
        Assert.Equal("started", evt.Verb);
        Assert.Equal(Guid.Empty, evt.EventId);
    }
    
    [Fact]
    public void CombatStartedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var enemyIds = new List<string> { "goblin1", "goblin2", "orc1" };
        
        // Act
        var evt = new CombatStartedEvent
        {
            CombatId = combatId,
            HeroId = "hero_ironclad",
            EnemyIds = enemyIds
        };
        
        // Assert
        Assert.Equal(combatId, evt.CombatId);
        Assert.Equal("hero_ironclad", evt.HeroId);
        Assert.Equal(3, evt.EnemyIds.Count);
        Assert.Contains("goblin1", evt.EnemyIds);
    }
    
    // ==================== COMBAT ENDED EVENT TESTS ====================
    
    [Fact]
    public void CombatEndedEvent_DefaultConstruction_SetsDefaults()
    {
        // Arrange & Act
        var evt = new CombatEndedEvent();
        
        // Assert
        Assert.Equal(nameof(CombatEndedEvent), evt.EventType);
        Assert.Equal(EventCategory.COMBAT, evt.Category);
        Assert.Equal(EventSeverity.INFO, evt.Severity);
        Assert.Equal("CombatSystem", evt.Subject);
        Assert.Equal("ended", evt.Verb);
    }
    
    [Fact]
    public void CombatEndedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var duration = TimeSpan.FromMinutes(5);
        
        // Act
        var evt = new CombatEndedEvent
        {
            CombatId = combatId,
            StatusName = "Victory",
            TotalTurns = 15,
            TotalActions = 47,
            Duration = duration
        };
        
        // Assert
        Assert.Equal(combatId, evt.CombatId);
        Assert.Equal("Victory", evt.StatusName);
        Assert.Equal(15, evt.TotalTurns);
        Assert.Equal(47, evt.TotalActions);
        Assert.Equal(duration, evt.Duration);
    }
    
    // ==================== ACTION EXECUTED EVENT TESTS ====================
    
    [Fact]
    public void ActionExecutedEvent_DefaultConstruction_SetsDefaults()
    {
        // Arrange & Act
        var evt = new ActionExecutedEvent();
        
        // Assert
        Assert.Equal(nameof(ActionExecutedEvent), evt.EventType);
        Assert.Equal(EventCategory.COMBAT, evt.Category);
        Assert.Equal(EventSeverity.INFO, evt.Severity);
        Assert.Equal("executed", evt.Verb);
    }
    
    [Fact]
    public void ActionExecutedEvent_AttackAction_FullSetup()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        
        // Act
        var evt = new ActionExecutedEvent
        {
            CombatId = combatId,
            ActionId = actionId,
            ActorId = "hero",
            ActionTypeName = "ATTACK",
            PowerId = "strike",
            TargetId = "enemy1",
            Applications =
            [
                new EffectApplicationRecord
                {
                    EffectInstanceId = "strike:mana",
                    EffectType = EffectType.MODIFY_RESOURCE,
                    TargetEntityId = "hero",
                    ResourceId = "mana",
                    PreviousValue = 3,
                    CurrentValue = 2
                }
            ]
        };
        
        // Assert
        Assert.Equal(combatId, evt.CombatId);
        Assert.Equal(actionId, evt.ActionId);
        Assert.Equal("hero", evt.ActorId);
        Assert.Equal("ATTACK", evt.ActionTypeName);
        Assert.Equal("strike", evt.PowerId);
        Assert.Equal("enemy1", evt.TargetId);
        var application = Assert.Single(evt.Applications);
        Assert.Equal("mana", application.ResourceId);
        Assert.Equal(3, application.PreviousValue);
        Assert.Equal(2, application.CurrentValue);
    }
    
    [Fact]
    public void ActionExecutedEvent_NullableFields_CanBeNull()
    {
        // Arrange & Act
        var evt = new ActionExecutedEvent
        {
            ActorId = "hero",
            ActionTypeName = "PASS",
            PowerId = null,
            TargetId = null
        };
        
        // Assert
        Assert.Null(evt.PowerId);
        Assert.Null(evt.TargetId);
        Assert.Empty(evt.Applications);
    }
    
    // ==================== EFFECT EXECUTED EVENT TESTS ====================
    
    [Fact]
    public void EffectExecutedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["critical"] = true,
            ["multiplier"] = 2.0
        };
        
        // Act
        var evt = new EffectExecutedEvent
        {
            EffectInstanceId = "effect_123",
            EffectType = EffectType.DAMAGE,
            SourceEntityId = "hero",
            TargetEntityId = "enemy1",
            ValueApplied = 25.5f,
            Success = true,
            Metadata = metadata
        };
        
        // Assert
        Assert.Equal("effect_123", evt.EffectInstanceId);
        Assert.Equal(EffectType.DAMAGE, evt.EffectType);
        Assert.Equal("hero", evt.SourceEntityId);
        Assert.Equal("enemy1", evt.TargetEntityId);
        Assert.Equal(25.5f, evt.ValueApplied);
        Assert.True(evt.Success);
        Assert.Equal(2, evt.Metadata.Count);
    }
    
    [Fact]
    public void EffectExecutedEvent_DefaultValues_Work()
    {
        // Arrange & Act
        var evt = new EffectExecutedEvent();
        
        // Assert
        Assert.Equal(string.Empty, evt.EffectInstanceId);
        Assert.Equal(string.Empty, evt.SourceEntityId);
        Assert.Equal(string.Empty, evt.TargetEntityId);
        Assert.Empty(evt.Metadata);
    }
    
    // ==================== STATUS EFFECT EVENTS TESTS ====================
    
    [Fact]
    public void StatusAppliedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        const string targetId = "enemy_1";
        var instanceId = Guid.NewGuid();
        const string sourceId = "hero_1";
        
        // Act
        var evt = new StatusAppliedEvent(
            targetId: targetId,
            statusId: "burning",
            instanceId: instanceId,
            stacks: 3,
            duration: 5,
            sourceId: sourceId
        );
        
        // Assert
        Assert.Equal(targetId, evt.TargetId);
        Assert.Equal("burning", evt.StatusId);
        Assert.Equal(instanceId, evt.InstanceId);
        Assert.Equal(3, evt.Stacks);
        Assert.Equal(5, evt.Duration);
        Assert.Equal(sourceId, evt.SourceId);
        Assert.Equal(nameof(StatusAppliedEvent), evt.EventType);
        Assert.Equal(EventCategory.GAME, evt.Category);
        Assert.Equal(EventSeverity.INFO, evt.Severity);
        Assert.Equal(targetId, evt.Subject);
        Assert.Equal("status_applied", evt.Verb);
        Assert.Equal("burning", evt.Target);
    }
    
    [Fact]
    public void StatusAppliedEvent_PayloadContainsAllData()
    {
        // Arrange
        const string targetId = "enemy_1";
        var instanceId = Guid.NewGuid();
        
        // Act
        var evt = new StatusAppliedEvent(
            targetId: targetId,
            statusId: "poison",
            instanceId: instanceId,
            stacks: 2,
            duration: 3,
            sourceId: null
        );
        
        // Assert
        Assert.Equal(6, evt.Payload.Count);
        Assert.Equal(targetId, evt.Payload["targetId"]);
        Assert.Equal("poison", evt.Payload["statusId"]);
        Assert.Equal(instanceId, evt.Payload["instanceId"]);
        Assert.Equal(2, evt.Payload["stacks"]);
        Assert.Equal("3", evt.Payload["duration"]);
        Assert.Equal(string.Empty, evt.Payload["sourceId"]);
    }
    
    [Fact]
    public void StatusAppliedEvent_PermanentDuration_ShowsInPayload()
    {
        // Arrange & Act
        var evt = new StatusAppliedEvent(
            targetId: "hero_1",
            statusId: "strength",
            instanceId: Guid.NewGuid(),
            stacks: 1,
            duration: null,
            sourceId: null
        );
        
        // Assert
        Assert.Null(evt.Duration);
        Assert.Equal("permanent", evt.Payload["duration"]);
    }
    
    [Fact]
    public void StatusRemovedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        const string targetId = "enemy_1";
        var instanceId = Guid.NewGuid();
        
        // Act
        var evt = new StatusRemovedEvent(
            targetId: targetId,
            statusId: "weakness",
            instanceId: instanceId,
            reason: "dispelled"
        );
        
        // Assert
        Assert.Equal(targetId, evt.TargetId);
        Assert.Equal("weakness", evt.StatusId);
        Assert.Equal(instanceId, evt.InstanceId);
        Assert.Equal("dispelled", evt.Reason);
        Assert.Equal(nameof(StatusRemovedEvent), evt.EventType);
        Assert.Equal("status_removed", evt.Verb);
    }
    
    [Fact]
    public void StatusRemovedEvent_DefaultReason_IsRemoved()
    {
        // Arrange & Act
        var evt = new StatusRemovedEvent(
            targetId: "enemy_1",
            statusId: "vulnerable",
            instanceId: Guid.NewGuid()
        );
        
        // Assert
        Assert.Equal("removed", evt.Reason);
    }
    
    [Fact]
    public void StatusStackChangedEvent_FullConstruction_CalculatesDelta()
    {
        // Arrange
        const string targetId = "hero_1";
        var instanceId = Guid.NewGuid();
        
        // Act
        var evt = new StatusStackChangedEvent(
            targetId: targetId,
            statusId: "strength",
            instanceId: instanceId,
            oldStacks: 2,
            newStacks: 5
        );
        
        // Assert
        Assert.Equal(2, evt.OldStacks);
        Assert.Equal(5, evt.NewStacks);
        Assert.Equal(3, evt.Payload["delta"]); // +3 stacks
        Assert.Equal(nameof(StatusStackChangedEvent), evt.EventType);
        Assert.Equal("status_stacks_changed", evt.Verb);
    }
    
    [Fact]
    public void StatusStackChangedEvent_NegativeDelta_WhenStacksDecrease()
    {
        // Arrange & Act
        var evt = new StatusStackChangedEvent(
            targetId: "enemy_1",
            statusId: "poison",
            instanceId: Guid.NewGuid(),
            oldStacks: 5,
            newStacks: 2
        );
        
        // Assert
        Assert.Equal(-3, evt.Payload["delta"]); // -3 stacks
    }
    
    [Fact]
    public void StatusDurationRefreshedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        const string targetId = "hero_1";
        var instanceId = Guid.NewGuid();
        
        // Act
        var evt = new StatusDurationRefreshedEvent(
            targetId: targetId,
            statusId: "regeneration",
            instanceId: instanceId,
            oldDuration: 1,
            newDuration: 3
        );
        
        // Assert
        Assert.Equal(1, evt.OldDuration);
        Assert.Equal(3, evt.NewDuration);
        Assert.Equal(nameof(StatusDurationRefreshedEvent), evt.EventType);
        Assert.Equal("status_duration_refreshed", evt.Verb);
    }
    
    [Fact]
    public void StatusDurationRefreshedEvent_PermanentDurations_ShowInPayload()
    {
        // Arrange & Act
        var evt = new StatusDurationRefreshedEvent(
            targetId: "hero_1",
            statusId: "artifact",
            instanceId: Guid.NewGuid(),
            oldDuration: null,
            newDuration: null
        );
        
        // Assert
        Assert.Null(evt.OldDuration);
        Assert.Null(evt.NewDuration);
        Assert.Equal("permanent", evt.Payload["oldDuration"]);
        Assert.Equal("permanent", evt.Payload["newDuration"]);
    }
    
    [Fact]
    public void StatusTickProcessedEvent_WithValue_SetsAllProperties()
    {
        // Arrange
        const string targetId = "enemy_1";
        var instanceId = Guid.NewGuid();
        
        // Act
        var evt = new StatusTickProcessedEvent(
            targetId: targetId,
            statusId: "burning",
            instanceId: instanceId,
            remainingDuration: 2,
            valueApplied: 9.0f
        );
        
        // Assert
        Assert.Equal(2, evt.RemainingDuration);
        Assert.Equal(9.0f, evt.ValueApplied);
        Assert.Equal(EventSeverity.DEBUG, evt.Severity);
        Assert.Equal("status_ticked", evt.Verb);
    }
    
    [Fact]
    public void StatusTickProcessedEvent_NoValue_PayloadShowsNone()
    {
        // Arrange & Act
        var evt = new StatusTickProcessedEvent(
            targetId: "enemy_1",
            statusId: "weakness",
            instanceId: Guid.NewGuid(),
            remainingDuration: 3,
            valueApplied: null
        );
        
        // Assert
        Assert.Null(evt.ValueApplied);
        Assert.Equal("none", evt.Payload["valueApplied"]);
    }
    
    [Fact]
    public void StatusExpiredEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        const string targetId = "enemy_1";
        var instanceId = Guid.NewGuid();
        
        // Act
        var evt = new StatusExpiredEvent(
            targetId: targetId,
            statusId: "vulnerable",
            instanceId: instanceId
        );
        
        // Assert
        Assert.Equal(targetId, evt.TargetId);
        Assert.Equal("vulnerable", evt.StatusId);
        Assert.Equal(instanceId, evt.InstanceId);
        Assert.Equal(nameof(StatusExpiredEvent), evt.EventType);
        Assert.Equal("status_expired", evt.Verb);
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void Scenario_CombatFlow_StartToEnd()
    {
        // Arrange - Combat starts
        var combatId = Guid.NewGuid();
        var startEvent = new CombatStartedEvent
        {
            CombatId = combatId,
            HeroId = "ironclad",
            EnemyIds = new List<string> { "jaw_worm" }
        };
        
        // Act - Combat action
        var actionEvent = new ActionExecutedEvent
        {
            CombatId = combatId,
            ActorId = "ironclad",
            ActionTypeName = "ATTACK",
            PowerId = "bash",
            TargetId = "jaw_worm",
            Applications =
            [
                new EffectApplicationRecord
                {
                    EffectInstanceId = "bash:target-resource",
                    EffectType = EffectType.MODIFY_RESOURCE,
                    TargetEntityId = "jaw_worm",
                    ResourceId = "guard",
                    PreviousValue = 20,
                    CurrentValue = 8
                }
            ]
        };
        
        // Combat ends
        var endEvent = new CombatEndedEvent
        {
            CombatId = combatId,
            StatusName = "Victory",
            TotalTurns = 8,
            TotalActions = 23,
            Duration = TimeSpan.FromMinutes(3)
        };
        
        // Assert
        Assert.Equal(combatId, startEvent.CombatId);
        Assert.Equal(combatId, actionEvent.CombatId);
        Assert.Equal(combatId, endEvent.CombatId);
        Assert.Equal("Victory", endEvent.StatusName);
    }
    
    [Fact]
    public void Scenario_StatusEffectLifecycle_ApplyTickExpire()
    {
        // Arrange
        const string targetId = "enemy_1";
        var instanceId = Guid.NewGuid();
        
        // Act - Apply burning
        var applyEvent = new StatusAppliedEvent(
            targetId: targetId,
            statusId: "burning",
            instanceId: instanceId,
            stacks: 3,
            duration: 3,
            sourceId: "hero_1"
        );
        
        // Tick 1 - duration 2 remaining
        var tick1Event = new StatusTickProcessedEvent(
            targetId: targetId,
            statusId: "burning",
            instanceId: instanceId,
            remainingDuration: 2,
            valueApplied: 9.0f // 3 stacks * 3 damage
        );
        
        // Tick 2 - duration 1 remaining
        var tick2Event = new StatusTickProcessedEvent(
            targetId: targetId,
            statusId: "burning",
            instanceId: instanceId,
            remainingDuration: 1,
            valueApplied: 9.0f
        );
        
        // Expire
        var expireEvent = new StatusExpiredEvent(
            targetId: targetId,
            statusId: "burning",
            instanceId: instanceId
        );
        
        // Assert
        Assert.Equal(instanceId, applyEvent.InstanceId);
        Assert.Equal(instanceId, tick1Event.InstanceId);
        Assert.Equal(instanceId, tick2Event.InstanceId);
        Assert.Equal(instanceId, expireEvent.InstanceId);
        Assert.Equal(3, applyEvent.Stacks);
        Assert.Equal(2, tick1Event.RemainingDuration);
        Assert.Equal(1, tick2Event.RemainingDuration);
    }
}
