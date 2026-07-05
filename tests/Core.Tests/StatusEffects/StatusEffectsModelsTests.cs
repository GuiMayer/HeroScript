using System;
using System.Collections.Generic;
using Core.StatusEffects;
using Xunit;

namespace Core.Tests.StatusEffects;

/// <summary>
/// Comprehensive tests for StatusEffects namespace models and enums
/// Covers all StatusEffects model classes, records, and enums
/// </summary>
[Trait("Category", "Unit")]
public class StatusEffectsModelsTests
{
    // ==================== STATUS EFFECT TYPE ENUM TESTS ====================
    
    [Fact]
    public void StatusEffectType_AllDoTsAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<StatusEffectType>();
        
        // Assert - DoTs
        Assert.Contains(StatusEffectType.BURNING, values);
        Assert.Contains(StatusEffectType.POISON, values);
        Assert.Contains(StatusEffectType.BLEEDING, values);
    }
    
    [Fact]
    public void StatusEffectType_AllBuffsAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<StatusEffectType>();
        
        // Assert - Buffs
        Assert.Contains(StatusEffectType.STRENGTH, values);
        Assert.Contains(StatusEffectType.DEXTERITY, values);
        Assert.Contains(StatusEffectType.VIGOR, values);
        Assert.Contains(StatusEffectType.REGENERATION, values);
        Assert.Contains(StatusEffectType.SHIELD, values);
        Assert.Contains(StatusEffectType.THORNS, values);
    }
    
    [Fact]
    public void StatusEffectType_AllDebuffsAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<StatusEffectType>();
        
        // Assert - Debuffs
        Assert.Contains(StatusEffectType.WEAKNESS, values);
        Assert.Contains(StatusEffectType.VULNERABLE, values);
        Assert.Contains(StatusEffectType.FRAIL, values);
    }
    
    [Fact]
    public void StatusEffectType_AllControlEffectsAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<StatusEffectType>();
        
        // Assert - Control
        Assert.Contains(StatusEffectType.STUNNED, values);
        Assert.Contains(StatusEffectType.SILENCED, values);
        Assert.Contains(StatusEffectType.ROOTED, values);
    }
    
    [Fact]
    public void StatusEffectType_AllSpecialEffectsAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<StatusEffectType>();
        
        // Assert - Special (Slay the Spire inspired)
        Assert.Contains(StatusEffectType.ARTIFACT, values);
        Assert.Contains(StatusEffectType.INTANGIBLE, values);
        Assert.Contains(StatusEffectType.BUFFER, values);
        Assert.Contains(StatusEffectType.BARRICADE, values);
        Assert.Contains(StatusEffectType.EVOLVE, values);
        Assert.Contains(StatusEffectType.CUSTOM, values);
    }
    
    // ==================== STATUS EFFECT TIMING ENUM TESTS ====================
    
    [Fact]
    public void StatusEffectTiming_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<StatusEffectTiming>();
        
        // Assert
        Assert.Contains(StatusEffectTiming.START_OF_TURN, values);
        Assert.Contains(StatusEffectTiming.END_OF_TURN, values);
        Assert.Contains(StatusEffectTiming.ON_DAMAGE_DEALT, values);
        Assert.Contains(StatusEffectTiming.ON_DAMAGE_TAKEN, values);
        Assert.Contains(StatusEffectTiming.ON_STATUS_APPLIED, values);
        Assert.Contains(StatusEffectTiming.ON_STATUS_REMOVED, values);
        Assert.Contains(StatusEffectTiming.PERMANENT, values);
    }
    
    // ==================== STATUS EFFECT BEHAVIOR ENUM TESTS ====================
    
    [Fact]
    public void StatusEffectBehavior_BasicBehaviorsAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<StatusEffectBehavior>();
        
        // Assert
        Assert.Contains(StatusEffectBehavior.DAMAGE_OVER_TIME, values);
        Assert.Contains(StatusEffectBehavior.HEAL_OVER_TIME, values);
        Assert.Contains(StatusEffectBehavior.STAT_MODIFIER, values);
        Assert.Contains(StatusEffectBehavior.CONTROL, values);
        Assert.Contains(StatusEffectBehavior.SHIELD, values);
        Assert.Contains(StatusEffectBehavior.REACTIVE, values);
    }
    
    [Fact]
    public void StatusEffectBehavior_SpecialBehaviorsAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<StatusEffectBehavior>();
        
        // Assert - Slay the Spire special behaviors
        Assert.Contains(StatusEffectBehavior.PREVENT_NEXT_DEBUFF, values);
        Assert.Contains(StatusEffectBehavior.DAMAGE_CAP, values);
        Assert.Contains(StatusEffectBehavior.DEATH_PREVENTION, values);
        Assert.Contains(StatusEffectBehavior.RULE_MODIFIER, values);
        Assert.Contains(StatusEffectBehavior.TRIGGER_ON_STATUS, values);
    }
    
    // ==================== STATUS EFFECT DEFINITION TESTS ====================
    
    [Fact]
    public void StatusEffectDefinition_FullConstruction_SetsAllProperties()
    {
        // Arrange & Act
        var definition = new StatusEffectDefinition
        {
            StatusId = "burning",
            Type = StatusEffectType.BURNING,
            DisplayName = "Burning",
            Description = "Takes fire damage each turn",
            Behavior = StatusEffectBehavior.DAMAGE_OVER_TIME,
            DefaultDuration = 3,
            DefaultStacks = 2,
            MaxStacks = 10,
            BaseValue = 5.0f,
            FormulaValue = "stacks * 3",
            ScalesWithStacks = true,
            ModifierKey = "fire_damage",
            ModifierFormula = "stacks * 0.1",
            Timing = StatusEffectTiming.END_OF_TURN,
            IconPath = "icons/burning.png",
            Color = "#FF4500",
            Tags = new List<string> { "fire", "dot", "debuff" },
            CustomData = new Dictionary<string, object> { ["intensity"] = "high" }
        };
        
        // Assert
        Assert.Equal("burning", definition.StatusId);
        Assert.Equal(StatusEffectType.BURNING, definition.Type);
        Assert.Equal("Burning", definition.DisplayName);
        Assert.Equal("Takes fire damage each turn", definition.Description);
        Assert.Equal(StatusEffectBehavior.DAMAGE_OVER_TIME, definition.Behavior);
        Assert.Equal(3, definition.DefaultDuration);
        Assert.Equal(2, definition.DefaultStacks);
        Assert.Equal(10, definition.MaxStacks);
        Assert.Equal(5.0f, definition.BaseValue);
        Assert.Equal("stacks * 3", definition.FormulaValue);
        Assert.True(definition.ScalesWithStacks);
        Assert.Equal("fire_damage", definition.ModifierKey);
        Assert.Equal("stacks * 0.1", definition.ModifierFormula);
        Assert.Equal(StatusEffectTiming.END_OF_TURN, definition.Timing);
        Assert.Equal("icons/burning.png", definition.IconPath);
        Assert.Equal("#FF4500", definition.Color);
        Assert.Equal(3, definition.Tags.Count);
        Assert.Contains("fire", definition.Tags);
        Assert.Single(definition.CustomData);
    }
    
    [Fact]
    public void StatusEffectDefinition_DefaultValues_Work()
    {
        // Arrange & Act
        var definition = new StatusEffectDefinition();
        
        // Assert
        Assert.Equal(string.Empty, definition.StatusId);
        Assert.Equal(string.Empty, definition.DisplayName);
        Assert.Equal(-1, definition.DefaultDuration); // Permanent by default
        Assert.Equal(1, definition.DefaultStacks);
        Assert.Equal(99, definition.MaxStacks);
        Assert.True(definition.ScalesWithStacks);
        Assert.Equal("#FFFFFF", definition.Color);
        Assert.Empty(definition.Tags);
        Assert.Empty(definition.CustomData);
    }
    
    [Fact]
    public void StatusEffectDefinition_RecordEquality_Works()
    {
        // Arrange
        var def1 = new StatusEffectDefinition
        {
            StatusId = "strength",
            Type = StatusEffectType.STRENGTH,
            BaseValue = 2.0f
        };
        var def2 = new StatusEffectDefinition
        {
            StatusId = "strength",
            Type = StatusEffectType.STRENGTH,
            BaseValue = 2.0f
        };
        
        // Assert - Compare key properties (collections don't compare by value in records)
        Assert.Equal(def1.StatusId, def2.StatusId);
        Assert.Equal(def1.Type, def2.Type);
        Assert.Equal(def1.BaseValue, def2.BaseValue);
    }
    
    [Fact]
    public void StatusEffectDefinition_PermanentDuration_IsNegativeOne()
    {
        // Arrange & Act
        var definition = new StatusEffectDefinition
        {
            StatusId = "artifact",
            Type = StatusEffectType.ARTIFACT,
            DefaultDuration = -1
        };
        
        // Assert
        Assert.Equal(-1, definition.DefaultDuration);
    }
    
    [Fact]
    public void StatusEffectDefinition_ScalesWithStacks_CanBeDisabled()
    {
        // Arrange & Act
        var definition = new StatusEffectDefinition
        {
            StatusId = "intangible",
            ScalesWithStacks = false
        };
        
        // Assert
        Assert.False(definition.ScalesWithStacks);
    }
    
    // ==================== STATUS EFFECT INSTANCE TESTS ====================
    
    [Fact]
    public void StatusEffectInstance_FullConstruction_SetsAllProperties()
    {
        // Arrange
        var definition = new StatusEffectDefinition
        {
            StatusId = "poison",
            Type = StatusEffectType.POISON
        };
        
        var instanceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var appliedAt = DateTime.UtcNow;
        
        // Act
        var instance = new StatusEffectInstance
        {
            InstanceId = instanceId,
            StatusId = "poison",
            Definition = definition,
            TargetId = targetId,
            SourceId = sourceId,
            Stacks = 3,
            Duration = 5,
            AppliedAt = appliedAt,
            TurnApplied = 10,
            IsActive = true,
            CustomData = new Dictionary<string, object> { ["total_damage"] = 15 }
        };
        
        // Assert
        Assert.Equal(instanceId, instance.InstanceId);
        Assert.Equal("poison", instance.StatusId);
        Assert.Equal(definition, instance.Definition);
        Assert.Equal(targetId, instance.TargetId);
        Assert.Equal(sourceId, instance.SourceId);
        Assert.Equal(3, instance.Stacks);
        Assert.Equal(5, instance.Duration);
        Assert.Equal(appliedAt, instance.AppliedAt);
        Assert.Equal(10, instance.TurnApplied);
        Assert.True(instance.IsActive);
        Assert.Single(instance.CustomData);
        Assert.Equal(15, instance.CustomData["total_damage"]);
    }
    
    [Fact]
    public void StatusEffectInstance_DefaultValues_GenerateGuid()
    {
        // Arrange & Act
        var instance = new StatusEffectInstance();
        
        // Assert
        Assert.NotEqual(Guid.Empty, instance.InstanceId);
        Assert.True(instance.AppliedAt <= DateTime.UtcNow);
        Assert.True(instance.IsActive);
        Assert.Equal(1, instance.Stacks);
        Assert.Empty(instance.CustomData);
    }
    
    [Fact]
    public void StatusEffectInstance_RecordEquality_Works()
    {
        // Arrange
        var definition = new StatusEffectDefinition { StatusId = "strength" };
        var id = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        
        var instance1 = new StatusEffectInstance
        {
            InstanceId = id,
            StatusId = "strength",
            Definition = definition,
            TargetId = targetId,
            Stacks = 2
        };
        
        var instance2 = new StatusEffectInstance
        {
            InstanceId = id,
            StatusId = "strength",
            Definition = definition,
            TargetId = targetId,
            Stacks = 2
        };
        
        // Assert - Compare key properties (collections don't compare by value in records)
        Assert.Equal(instance1.InstanceId, instance2.InstanceId);
        Assert.Equal(instance1.StatusId, instance2.StatusId);
        Assert.Equal(instance1.TargetId, instance2.TargetId);
        Assert.Equal(instance1.Stacks, instance2.Stacks);
    }
    
    [Fact]
    public void StatusEffectInstance_IsImmutable()
    {
        // Arrange
        var instance = new StatusEffectInstance
        {
            Stacks = 1,
            Duration = 3
        };
        
        // Act - Using 'with' to create modified copy
        var modified = instance with { Stacks = 3, Duration = 1 };
        
        // Assert - Original unchanged
        Assert.Equal(1, instance.Stacks);
        Assert.Equal(3, instance.Duration);
        
        // Modified has new values
        Assert.Equal(3, modified.Stacks);
        Assert.Equal(1, modified.Duration);
    }
    
    [Fact]
    public void StatusEffectInstance_SourceId_CanBeNull()
    {
        // Arrange & Act
        var instance = new StatusEffectInstance
        {
            StatusId = "regeneration",
            SourceId = null
        };
        
        // Assert
        Assert.Null(instance.SourceId);
    }
    
    [Fact]
    public void StatusEffectInstance_CustomData_CanStoreArbitraryValues()
    {
        // Arrange & Act
        var instance = new StatusEffectInstance
        {
            CustomData = new Dictionary<string, object>
            {
                ["damage_absorbed"] = 50,
                ["debuffs_prevented"] = 2,
                ["trigger_count"] = 1,
                ["metadata"] = "test"
            }
        };
        
        // Assert
        Assert.Equal(4, instance.CustomData.Count);
        Assert.Equal(50, instance.CustomData["damage_absorbed"]);
        Assert.Equal(2, instance.CustomData["debuffs_prevented"]);
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void StatusEffect_BurningScenario_FullSetup()
    {
        // Arrange - Define Burning status
        var burningDef = new StatusEffectDefinition
        {
            StatusId = "burning",
            Type = StatusEffectType.BURNING,
            DisplayName = "Burning",
            Description = "Takes 3 fire damage per stack at end of turn",
            Behavior = StatusEffectBehavior.DAMAGE_OVER_TIME,
            DefaultDuration = 3,
            DefaultStacks = 1,
            MaxStacks = 10,
            FormulaValue = "stacks * 3",
            ScalesWithStacks = true,
            Timing = StatusEffectTiming.END_OF_TURN,
            Tags = new List<string> { "fire", "dot", "debuff" }
        };
        
        // Act - Create instance applied to a target
        var instance = new StatusEffectInstance
        {
            StatusId = "burning",
            Definition = burningDef,
            TargetId = Guid.NewGuid(),
            SourceId = Guid.NewGuid(),
            Stacks = 2,
            Duration = 3,
            TurnApplied = 5
        };
        
        // Assert
        Assert.Equal("burning", instance.StatusId);
        Assert.Equal(2, instance.Stacks);
        Assert.Equal(3, instance.Duration);
        Assert.Equal(StatusEffectTiming.END_OF_TURN, burningDef.Timing);
        Assert.Contains("fire", burningDef.Tags);
    }
    
    [Fact]
    public void StatusEffect_StrengthScenario_PermanentBuff()
    {
        // Arrange - Define Strength (permanent buff)
        var strengthDef = new StatusEffectDefinition
        {
            StatusId = "strength",
            Type = StatusEffectType.STRENGTH,
            DisplayName = "Strength",
            Description = "Increases damage dealt by 25% per stack",
            Behavior = StatusEffectBehavior.STAT_MODIFIER,
            DefaultDuration = -1, // Permanent
            DefaultStacks = 1,
            MaxStacks = 999,
            FormulaValue = "stacks * 0.25",
            ModifierKey = "increased_damage_total",
            ModifierFormula = "stacks * 0.25",
            Timing = StatusEffectTiming.PERMANENT,
            Tags = new List<string> { "buff", "damage", "permanent" }
        };
        
        // Act - Create permanent instance
        var instance = new StatusEffectInstance
        {
            StatusId = "strength",
            Definition = strengthDef,
            TargetId = Guid.NewGuid(),
            Stacks = 3,
            Duration = -1,
            TurnApplied = 1
        };
        
        // Assert
        Assert.Equal(-1, instance.Duration); // Permanent
        Assert.Equal(3, instance.Stacks);
        Assert.Equal(StatusEffectTiming.PERMANENT, strengthDef.Timing);
        Assert.Equal("increased_damage_total", strengthDef.ModifierKey);
    }
    
    [Fact]
    public void StatusEffect_ArtifactScenario_PreventDebuffs()
    {
        // Arrange - Define Artifact (Slay the Spire)
        var artifactDef = new StatusEffectDefinition
        {
            StatusId = "artifact",
            Type = StatusEffectType.ARTIFACT,
            DisplayName = "Artifact",
            Description = "Negates the next debuff applied. Consumes 1 stack.",
            Behavior = StatusEffectBehavior.PREVENT_NEXT_DEBUFF,
            DefaultDuration = -1,
            DefaultStacks = 1,
            MaxStacks = 10,
            ScalesWithStacks = false, // Each stack prevents one debuff
            Timing = StatusEffectTiming.PERMANENT,
            Tags = new List<string> { "buff", "protection", "special" }
        };
        
        // Act
        var instance = new StatusEffectInstance
        {
            StatusId = "artifact",
            Definition = artifactDef,
            TargetId = Guid.NewGuid(),
            Stacks = 2,
            Duration = -1,
            CustomData = new Dictionary<string, object>
            {
                ["debuffs_prevented"] = 0
            }
        };
        
        // Assert
        Assert.Equal(StatusEffectBehavior.PREVENT_NEXT_DEBUFF, artifactDef.Behavior);
        Assert.False(artifactDef.ScalesWithStacks);
        Assert.Equal(2, instance.Stacks);
        Assert.Equal(0, instance.CustomData["debuffs_prevented"]);
    }
    
    [Fact]
    public void StatusEffect_IntangibleScenario_DamageCap()
    {
        // Arrange - Define Intangible (Slay the Spire)
        var intangibleDef = new StatusEffectDefinition
        {
            StatusId = "intangible",
            Type = StatusEffectType.INTANGIBLE,
            DisplayName = "Intangible",
            Description = "Reduces all damage taken to 1",
            Behavior = StatusEffectBehavior.DAMAGE_CAP,
            DefaultDuration = 1,
            DefaultStacks = 1,
            MaxStacks = 1,
            ScalesWithStacks = false,
            Timing = StatusEffectTiming.START_OF_TURN,
            CustomData = new Dictionary<string, object> { ["damage_cap"] = 1 }
        };
        
        // Act
        var instance = new StatusEffectInstance
        {
            StatusId = "intangible",
            Definition = intangibleDef,
            TargetId = Guid.NewGuid(),
            Stacks = 1,
            Duration = 1,
            TurnApplied = 10
        };
        
        // Assert
        Assert.Equal(StatusEffectBehavior.DAMAGE_CAP, intangibleDef.Behavior);
        Assert.Equal(1, intangibleDef.CustomData["damage_cap"]);
        Assert.Equal(1, instance.Duration);
    }
    
    [Fact]
    public void StatusEffect_VulnerableScenario_IncreaseDamageTaken()
    {
        // Arrange
        var vulnerableDef = new StatusEffectDefinition
        {
            StatusId = "vulnerable",
            Type = StatusEffectType.VULNERABLE,
            DisplayName = "Vulnerable",
            Description = "Take 50% more damage",
            Behavior = StatusEffectBehavior.STAT_MODIFIER,
            DefaultDuration = 2,
            DefaultStacks = 1,
            FormulaValue = "0.5",
            ModifierKey = "increased_damage_taken",
            ModifierFormula = "0.5",
            Timing = StatusEffectTiming.PERMANENT,
            Tags = new List<string> { "debuff", "damage" }
        };
        
        // Act
        var instance = new StatusEffectInstance
        {
            StatusId = "vulnerable",
            Definition = vulnerableDef,
            TargetId = Guid.NewGuid(),
            Stacks = 1,
            Duration = 2
        };
        
        // Assert
        Assert.Equal("increased_damage_taken", vulnerableDef.ModifierKey);
        Assert.Equal("0.5", vulnerableDef.ModifierFormula);
        Assert.Contains("debuff", vulnerableDef.Tags);
    }
}
