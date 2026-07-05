using System;
using Core.Combat.TurnPhase;
using Core.Events;
using Core.Events.Domain;
using Xunit;

namespace Core.Tests.Events;

/// <summary>
/// Comprehensive tests for Phase and Energy domain events
/// Covers turn phase management and energy system events
/// </summary>
[Trait("Category", "Unit")]
public class PhaseAndEnergyEventsTests
{
    // ==================== TURN PHASE ENUM TESTS ====================
    
    [Fact]
    public void TurnPhase_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<TurnPhase>();
        
        // Assert
        Assert.Contains(TurnPhase.NONE, values);
        Assert.Contains(TurnPhase.UNTAP, values);
        Assert.Contains(TurnPhase.UPKEEP, values);
        Assert.Contains(TurnPhase.DRAW, values);
        Assert.Contains(TurnPhase.STANDBY, values);
        Assert.Contains(TurnPhase.MAIN_1, values);
        Assert.Contains(TurnPhase.BATTLE_START, values);
        Assert.Contains(TurnPhase.BATTLE_DECLARE_ATTACKERS, values);
        Assert.Contains(TurnPhase.BATTLE_DECLARE_BLOCKERS, values);
        Assert.Contains(TurnPhase.BATTLE_DAMAGE, values);
        Assert.Contains(TurnPhase.BATTLE_END, values);
        Assert.Contains(TurnPhase.MAIN_2, values);
        Assert.Contains(TurnPhase.END, values);
        Assert.Contains(TurnPhase.CLEANUP, values);
    }
    
    [Fact]
    public void TurnPhase_HasCorrectCount()
    {
        // Arrange & Act
        var values = Enum.GetValues<TurnPhase>();
        
        // Assert - 14 total phases
        Assert.Equal(14, values.Length);
    }
    
    // ==================== PHASE STARTED EVENT TESTS ====================
    
    [Fact]
    public void PhaseStartedEvent_DefaultConstruction_SetsDefaults()
    {
        // Arrange & Act
        var evt = new PhaseStartedEvent();
        
        // Assert
        Assert.Equal(Guid.Empty, evt.CombatId);
        Assert.Equal(TurnPhase.NONE, evt.Phase);
        Assert.Equal(string.Empty, evt.ActivePlayerId);
        Assert.Equal(0, evt.Turn);
    }
    
    [Fact]
    public void PhaseStartedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        
        // Act
        var evt = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.MAIN_1,
            ActivePlayerId = "player_hero",
            Turn = 3
        };
        
        // Assert
        Assert.Equal(combatId, evt.CombatId);
        Assert.Equal(TurnPhase.MAIN_1, evt.Phase);
        Assert.Equal("player_hero", evt.ActivePlayerId);
        Assert.Equal(3, evt.Turn);
    }
    
    [Fact]
    public void PhaseStartedEvent_BattlePhase_SetsCorrectly()
    {
        // Arrange & Act
        var evt = new PhaseStartedEvent
        {
            CombatId = Guid.NewGuid(),
            Phase = TurnPhase.BATTLE_DECLARE_ATTACKERS,
            ActivePlayerId = "player1",
            Turn = 1
        };
        
        // Assert
        Assert.Equal(TurnPhase.BATTLE_DECLARE_ATTACKERS, evt.Phase);
    }
    
    // ==================== PHASE ENDED EVENT TESTS ====================
    
    [Fact]
    public void PhaseEndedEvent_DefaultConstruction_SetsDefaults()
    {
        // Arrange & Act
        var evt = new PhaseEndedEvent();
        
        // Assert
        Assert.Equal(Guid.Empty, evt.CombatId);
        Assert.Equal(TurnPhase.NONE, evt.Phase);
        Assert.Null(evt.NextPhase);
        Assert.Equal(0, evt.Turn);
        Assert.Equal(0, evt.DurationMs);
    }
    
    [Fact]
    public void PhaseEndedEvent_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        
        // Act
        var evt = new PhaseEndedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.DRAW,
            NextPhase = TurnPhase.MAIN_1,
            Turn = 5,
            DurationMs = 1250
        };
        
        // Assert
        Assert.Equal(combatId, evt.CombatId);
        Assert.Equal(TurnPhase.DRAW, evt.Phase);
        Assert.Equal(TurnPhase.MAIN_1, evt.NextPhase);
        Assert.Equal(5, evt.Turn);
        Assert.Equal(1250, evt.DurationMs);
    }
    
    [Fact]
    public void PhaseEndedEvent_LastPhase_NextPhaseIsNull()
    {
        // Arrange & Act
        var evt = new PhaseEndedEvent
        {
            Phase = TurnPhase.CLEANUP,
            NextPhase = null,
            Turn = 10
        };
        
        // Assert
        Assert.Equal(TurnPhase.CLEANUP, evt.Phase);
        Assert.Null(evt.NextPhase);
    }
    
    // ==================== ENERGY CHANGED EVENT TESTS ====================
    
    [Fact]
    public void EnergyChangedEvent_DefaultConstruction_SetsDefaults()
    {
        // Arrange & Act
        var evt = new EnergyChangedEvent();
        
        // Assert
        Assert.Equal(nameof(EnergyChangedEvent), evt.EventType);
        Assert.Equal(EventCategory.COMBAT, evt.Category);
        Assert.Equal(EventSeverity.DEBUG, evt.Severity);
        Assert.Equal("EnergyPool", evt.Subject);
        Assert.Equal("changed", evt.Verb);
    }
    
    [Fact]
    public void EnergyChangedEvent_EnergyGain_PositiveDelta()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        
        // Act
        var evt = new EnergyChangedEvent
        {
            CombatId = combatId,
            OldEnergy = 1,
            NewEnergy = 3,
            Delta = 2,
            Reason = "turn_start"
        };
        
        // Assert
        Assert.Equal(combatId, evt.CombatId);
        Assert.Equal(1, evt.OldEnergy);
        Assert.Equal(3, evt.NewEnergy);
        Assert.Equal(2, evt.Delta);
        Assert.Equal("turn_start", evt.Reason);
    }
    
    [Fact]
    public void EnergyChangedEvent_EnergySpent_NegativeDelta()
    {
        // Arrange & Act
        var evt = new EnergyChangedEvent
        {
            CombatId = Guid.NewGuid(),
            OldEnergy = 3,
            NewEnergy = 1,
            Delta = -2,
            Reason = "card_played"
        };
        
        // Assert
        Assert.Equal(3, evt.OldEnergy);
        Assert.Equal(1, evt.NewEnergy);
        Assert.Equal(-2, evt.Delta);
        Assert.Equal("card_played", evt.Reason);
    }
    
    [Fact]
    public void EnergyChangedEvent_NoChange_ZeroDelta()
    {
        // Arrange & Act
        var evt = new EnergyChangedEvent
        {
            OldEnergy = 2,
            NewEnergy = 2,
            Delta = 0,
            Reason = "no_change"
        };
        
        // Assert
        Assert.Equal(2, evt.OldEnergy);
        Assert.Equal(2, evt.NewEnergy);
        Assert.Equal(0, evt.Delta);
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void Scenario_MagicStyleTurnFlow_AllPhases()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var turn = 1;
        
        // Act - Simulate Magic: The Gathering turn structure
        var untapStart = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.UNTAP,
            ActivePlayerId = "player1",
            Turn = turn
        };
        
        var untapEnd = new PhaseEndedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.UNTAP,
            NextPhase = TurnPhase.UPKEEP,
            Turn = turn,
            DurationMs = 100
        };
        
        var upkeepStart = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.UPKEEP,
            ActivePlayerId = "player1",
            Turn = turn
        };
        
        var upkeepEnd = new PhaseEndedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.UPKEEP,
            NextPhase = TurnPhase.DRAW,
            Turn = turn,
            DurationMs = 150
        };
        
        var drawStart = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.DRAW,
            ActivePlayerId = "player1",
            Turn = turn
        };
        
        // Assert
        Assert.Equal(TurnPhase.UNTAP, untapStart.Phase);
        Assert.Equal(TurnPhase.UPKEEP, untapEnd.NextPhase);
        Assert.Equal(TurnPhase.UPKEEP, upkeepStart.Phase);
        Assert.Equal(TurnPhase.DRAW, upkeepEnd.NextPhase);
        Assert.Equal(TurnPhase.DRAW, drawStart.Phase);
        Assert.Equal(combatId, untapStart.CombatId);
        Assert.Equal(combatId, drawStart.CombatId);
    }
    
    [Fact]
    public void Scenario_BattlePhaseFlow_CompleteSequence()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var turn = 3;
        
        // Act - Complete battle phase sequence
        var battleStart = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.BATTLE_START,
            ActivePlayerId = "attacker",
            Turn = turn
        };
        
        var declareAttackers = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.BATTLE_DECLARE_ATTACKERS,
            ActivePlayerId = "attacker",
            Turn = turn
        };
        
        var declareBlockers = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.BATTLE_DECLARE_BLOCKERS,
            ActivePlayerId = "defender",
            Turn = turn
        };
        
        var damage = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.BATTLE_DAMAGE,
            ActivePlayerId = "attacker",
            Turn = turn
        };
        
        var battleEnd = new PhaseEndedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.BATTLE_END,
            NextPhase = TurnPhase.MAIN_2,
            Turn = turn,
            DurationMs = 5000
        };
        
        // Assert
        Assert.Equal(TurnPhase.BATTLE_START, battleStart.Phase);
        Assert.Equal(TurnPhase.BATTLE_DECLARE_ATTACKERS, declareAttackers.Phase);
        Assert.Equal(TurnPhase.BATTLE_DECLARE_BLOCKERS, declareBlockers.Phase);
        Assert.Equal("defender", declareBlockers.ActivePlayerId);
        Assert.Equal(TurnPhase.BATTLE_DAMAGE, damage.Phase);
        Assert.Equal(TurnPhase.BATTLE_END, battleEnd.Phase);
        Assert.Equal(TurnPhase.MAIN_2, battleEnd.NextPhase);
    }
    
    [Fact]
    public void Scenario_EnergyManagement_TurnCycle()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        
        // Act - Turn start: gain energy
        var gainEnergy = new EnergyChangedEvent
        {
            CombatId = combatId,
            OldEnergy = 0,
            NewEnergy = 3,
            Delta = 3,
            Reason = "turn_start"
        };
        
        // Play card 1: spend 1 energy
        var playCard1 = new EnergyChangedEvent
        {
            CombatId = combatId,
            OldEnergy = 3,
            NewEnergy = 2,
            Delta = -1,
            Reason = "card_strike"
        };
        
        // Play card 2: spend 2 energy
        var playCard2 = new EnergyChangedEvent
        {
            CombatId = combatId,
            OldEnergy = 2,
            NewEnergy = 0,
            Delta = -2,
            Reason = "card_bash"
        };
        
        // Turn end: reset energy
        var resetEnergy = new EnergyChangedEvent
        {
            CombatId = combatId,
            OldEnergy = 0,
            NewEnergy = 0,
            Delta = 0,
            Reason = "turn_end"
        };
        
        // Assert
        Assert.Equal(3, gainEnergy.Delta);
        Assert.Equal(-1, playCard1.Delta);
        Assert.Equal(-2, playCard2.Delta);
        Assert.Equal(0, resetEnergy.Delta);
        Assert.Equal("turn_start", gainEnergy.Reason);
        Assert.Equal("card_bash", playCard2.Reason);
    }
    
    [Fact]
    public void Scenario_SlayTheSpireStyleTurn_SimplePhases()
    {
        // Arrange - Slay the Spire doesn't use complex phases
        var combatId = Guid.NewGuid();
        var turn = 1;
        
        // Act - Player turn starts
        var turnStart = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.DRAW,
            ActivePlayerId = "hero",
            Turn = turn
        };
        
        // Gain energy
        var gainEnergy = new EnergyChangedEvent
        {
            CombatId = combatId,
            OldEnergy = 0,
            NewEnergy = 3,
            Delta = 3,
            Reason = "turn_start"
        };
        
        // Main phase
        var mainPhase = new PhaseStartedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.MAIN_1,
            ActivePlayerId = "hero",
            Turn = turn
        };
        
        // End turn
        var endPhase = new PhaseEndedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.END,
            NextPhase = null,
            Turn = turn,
            DurationMs = 15000
        };
        
        // Assert
        Assert.Equal(TurnPhase.DRAW, turnStart.Phase);
        Assert.Equal(3, gainEnergy.NewEnergy);
        Assert.Equal(TurnPhase.MAIN_1, mainPhase.Phase);
        Assert.Equal(TurnPhase.END, endPhase.Phase);
        Assert.Null(endPhase.NextPhase);
    }
    
    [Fact]
    public void Scenario_PhaseTimingTracking_DurationMeasurement()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        
        // Act - Track phase durations
        var quickPhase = new PhaseEndedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.UNTAP,
            NextPhase = TurnPhase.UPKEEP,
            Turn = 1,
            DurationMs = 50 // Very fast automated phase
        };
        
        var thinkingPhase = new PhaseEndedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.MAIN_1,
            NextPhase = TurnPhase.BATTLE_START,
            Turn = 1,
            DurationMs = 23000 // Player thinking
        };
        
        var combatPhase = new PhaseEndedEvent
        {
            CombatId = combatId,
            Phase = TurnPhase.BATTLE_DAMAGE,
            NextPhase = TurnPhase.BATTLE_END,
            Turn = 1,
            DurationMs = 1500 // Combat resolution
        };
        
        // Assert
        Assert.True(quickPhase.DurationMs < 100);
        Assert.True(thinkingPhase.DurationMs > 20000);
        Assert.InRange(combatPhase.DurationMs, 1000, 2000);
    }
    
    [Fact]
    public void Scenario_EnergyPotionUse_ExtraEnergy()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        
        // Act - Use energy potion mid-turn
        var beforePotion = new EnergyChangedEvent
        {
            CombatId = combatId,
            OldEnergy = 0,
            NewEnergy = 0,
            Delta = 0,
            Reason = "empty"
        };
        
        var potionUsed = new EnergyChangedEvent
        {
            CombatId = combatId,
            OldEnergy = 0,
            NewEnergy = 2,
            Delta = 2,
            Reason = "energy_potion"
        };
        
        // Assert
        Assert.Equal(0, beforePotion.NewEnergy);
        Assert.Equal(2, potionUsed.NewEnergy);
        Assert.Equal("energy_potion", potionUsed.Reason);
    }
}
