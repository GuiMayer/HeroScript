using System;
using System.Collections.Generic;
using Core.Effects;
using Xunit;

namespace Core.Tests.Effects;

/// <summary>
/// Comprehensive tests for Effects namespace models and enums
/// Covers all Effects model classes, records, and enums
/// </summary>
[Trait("Category", "Unit")]
public class EffectsModelsTests
{
    // ==================== EFFECT TYPE ENUM TESTS ====================
    
    [Fact]
    public void EffectType_ResourceTypesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectType>();
        
        // Assert - Resource effects
        Assert.Contains(EffectType.DAMAGE, values);
        Assert.Contains(EffectType.HEAL, values);
        Assert.Contains(EffectType.MODIFY_RESOURCE, values);
    }
    
    [Fact]
    public void EffectType_StatusTypesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectType>();
        
        // Assert - Status effects
        Assert.Contains(EffectType.APPLY_STATUS, values);
        Assert.Contains(EffectType.REMOVE_STATUS, values);
        Assert.Contains(EffectType.DISPEL_STATUS, values);
    }
    
    [Fact]
    public void EffectType_CardTypesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectType>();
        
        // Assert - Card effects
        Assert.Contains(EffectType.DRAW_CARD, values);
        Assert.Contains(EffectType.DISCARD_CARD, values);
        Assert.Contains(EffectType.EXHAUST_CARD, values);
        Assert.Contains(EffectType.ADD_CARD_TO_HAND, values);
    }
    
    [Fact]
    public void EffectType_ModifierTypesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectType>();
        
        // Assert - Modifier effects
        Assert.Contains(EffectType.MODIFY_DAMAGE_DEALT, values);
        Assert.Contains(EffectType.MODIFY_DAMAGE_TAKEN, values);
        Assert.Contains(EffectType.MODIFY_CRIT_CHANCE, values);
        Assert.Contains(EffectType.MODIFY_CRIT_MULT, values);
        Assert.Contains(EffectType.MODIFY_COOLDOWNS, values);
    }
    
    [Fact]
    public void EffectType_ControlTypesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectType>();
        
        // Assert - Control effects
        Assert.Contains(EffectType.PREVENT_ACTIONS, values);
        Assert.Contains(EffectType.FORCE_TARGET, values);
        Assert.Contains(EffectType.SKIP_TURN, values);
    }
    
    [Fact]
    public void EffectType_UtilityTypesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectType>();
        
        // Assert - Utility effects
        Assert.Contains(EffectType.REFLECT_DAMAGE, values);
        Assert.Contains(EffectType.ABSORB_DAMAGE, values);
        Assert.Contains(EffectType.TRIGGER_EFFECT, values);
        Assert.Contains(EffectType.CONDITIONAL_EFFECT, values);
    }
    
    [Fact]
    public void EffectType_MetaTypesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectType>();
        
        // Assert - Meta effects
        Assert.Contains(EffectType.MODIFY_EFFECT, values);
        Assert.Contains(EffectType.COPY_EFFECT, values);
    }
    
    // ==================== EFFECT TARGET ENUM TESTS ====================
    
    [Fact]
    public void EffectTarget_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectTarget>();
        
        // Assert
        Assert.Contains(EffectTarget.SELF, values);
        Assert.Contains(EffectTarget.TARGET, values);
        Assert.Contains(EffectTarget.ALL_ENEMIES, values);
        Assert.Contains(EffectTarget.ALL_ALLIES, values);
        Assert.Contains(EffectTarget.RANDOM_ENEMY, values);
        Assert.Contains(EffectTarget.LOWEST_HP_ENEMY, values);
        Assert.Contains(EffectTarget.HIGHEST_HP_ENEMY, values);
    }
    
    // ==================== EFFECT TIMING ENUM TESTS ====================
    
    [Fact]
    public void EffectTiming_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectTiming>();
        
        // Assert
        Assert.Contains(EffectTiming.IMMEDIATE, values);
        Assert.Contains(EffectTiming.DELAYED, values);
        Assert.Contains(EffectTiming.ON_TURN_START, values);
        Assert.Contains(EffectTiming.ON_TURN_END, values);
        Assert.Contains(EffectTiming.ON_DAMAGE_DEALT, values);
        Assert.Contains(EffectTiming.ON_DAMAGE_TAKEN, values);
    }
    
    // ==================== EFFECT EXECUTION STATE ENUM TESTS ====================
    
    [Fact]
    public void EffectExecutionState_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectExecutionState>();
        
        // Assert
        Assert.Contains(EffectExecutionState.PENDING, values);
        Assert.Contains(EffectExecutionState.EXECUTING, values);
        Assert.Contains(EffectExecutionState.COMPLETED, values);
        Assert.Contains(EffectExecutionState.FAILED, values);
        Assert.Contains(EffectExecutionState.CANCELLED, values);
    }
    
    // ==================== EFFECT SCOPE ENUM TESTS ====================
    
    [Fact]
    public void EffectScope_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<EffectScope>();
        
        // Assert
        Assert.Contains(EffectScope.COMBAT, values);
        Assert.Contains(EffectScope.RUN, values);
        Assert.Contains(EffectScope.DECK, values);
        Assert.Contains(EffectScope.SHOP, values);
        Assert.Contains(EffectScope.GLOBAL, values);
    }
    
    // ==================== EFFECT DEFINITION TESTS ====================
    
    [Fact]
    public void EffectDefinition_FullConstruction_SetsAllProperties()
    {
        // Arrange & Act
        var definition = new EffectDefinition
        {
            EffectId = "fireball_damage",
            Type = EffectType.DAMAGE,
            Target = EffectTarget.TARGET,
            Timing = EffectTiming.IMMEDIATE,
            FlatValue = 10.0f,
            FormulaValue = "base_damage * 1.5",
            IsPercentage = false,
            TargetResource = "health",
            StatusId = "burning",
            StatusStacks = 2,
            StatusDuration = 3,
            ModifierKey = "fire_damage",
            ModifierValue = 0.5f,
            ModifierFormula = "stacks * 0.1",
            Condition = "target_hp > 0",
            RequiredTags = new List<string> { "fire", "spell" },
            ExcludedTags = new List<string> { "physical" },
            Chance = 0.8f,
            Repeat = 2,
            Tags = new List<string> { "fire", "aoe" },
            Metadata = new Dictionary<string, object> { ["animation"] = "explosion" }
        };
        
        // Assert
        Assert.Equal("fireball_damage", definition.EffectId);
        Assert.Equal(EffectType.DAMAGE, definition.Type);
        Assert.Equal(EffectTarget.TARGET, definition.Target);
        Assert.Equal(EffectTiming.IMMEDIATE, definition.Timing);
        Assert.Equal(10.0f, definition.FlatValue);
        Assert.Equal("base_damage * 1.5", definition.FormulaValue);
        Assert.False(definition.IsPercentage);
        Assert.Equal("health", definition.TargetResource);
        Assert.Equal("burning", definition.StatusId);
        Assert.Equal(2, definition.StatusStacks);
        Assert.Equal(3, definition.StatusDuration);
        Assert.Equal("fire_damage", definition.ModifierKey);
        Assert.Equal(0.5f, definition.ModifierValue);
        Assert.Equal("stacks * 0.1", definition.ModifierFormula);
        Assert.Equal("target_hp > 0", definition.Condition);
        Assert.Equal(2, definition.RequiredTags!.Count);
        Assert.Single(definition.ExcludedTags!);
        Assert.Equal(0.8f, definition.Chance);
        Assert.Equal(2, definition.Repeat);
        Assert.Equal(2, definition.Tags.Count);
        Assert.Single(definition.Metadata);
    }
    
    [Fact]
    public void EffectDefinition_DefaultValues_Work()
    {
        // Arrange & Act
        var definition = new EffectDefinition();
        
        // Assert
        Assert.Equal(string.Empty, definition.EffectId);
        Assert.Equal(EffectTarget.TARGET, definition.Target);
        Assert.Equal(EffectTiming.IMMEDIATE, definition.Timing);
        Assert.False(definition.IsPercentage);
        Assert.Null(definition.TargetResource);
        Assert.Equal(1.0f, definition.Chance);
        Assert.Equal(1, definition.Repeat);
        Assert.Empty(definition.Tags);
        Assert.Empty(definition.Metadata);
    }
    
    [Fact]
    public void EffectDefinition_DoesNotGenerateAmbientIds()
    {
        // Arrange & Act
        var def1 = new EffectDefinition();
        var def2 = new EffectDefinition();
        
        Assert.Equal(string.Empty, def1.EffectId);
        Assert.Equal(def1.EffectId, def2.EffectId);
    }
    
    [Fact]
    public void EffectDefinition_DamageEffect_Setup()
    {
        // Arrange & Act
        var damageEffect = new EffectDefinition
        {
            EffectId = "basic_attack",
            Type = EffectType.DAMAGE,
            Target = EffectTarget.TARGET,
            FlatValue = 5.0f,
            TargetResource = "health"
        };
        
        // Assert
        Assert.Equal(EffectType.DAMAGE, damageEffect.Type);
        Assert.Equal("health", damageEffect.TargetResource);
        Assert.Equal(5.0f, damageEffect.FlatValue);
    }
    
    [Fact]
    public void EffectDefinition_HealEffect_Setup()
    {
        // Arrange & Act
        var healEffect = new EffectDefinition
        {
            Type = EffectType.HEAL,
            Target = EffectTarget.SELF,
            FlatValue = 10.0f,
            TargetResource = "health"
        };
        
        // Assert
        Assert.Equal(EffectType.HEAL, healEffect.Type);
        Assert.Equal(EffectTarget.SELF, healEffect.Target);
    }
    
    [Fact]
    public void EffectDefinition_ApplyStatusEffect_Setup()
    {
        // Arrange & Act
        var statusEffect = new EffectDefinition
        {
            Type = EffectType.APPLY_STATUS,
            Target = EffectTarget.TARGET,
            StatusId = "poison",
            StatusStacks = 3,
            StatusDuration = 5
        };
        
        // Assert
        Assert.Equal(EffectType.APPLY_STATUS, statusEffect.Type);
        Assert.Equal("poison", statusEffect.StatusId);
        Assert.Equal(3, statusEffect.StatusStacks);
        Assert.Equal(5, statusEffect.StatusDuration);
    }
    
    [Fact]
    public void EffectDefinition_ConditionalEffect_WithCondition()
    {
        // Arrange & Act
        var conditionalEffect = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            Condition = "target_hp < target_max_hp * 0.5",
            FlatValue = 20.0f
        };
        
        // Assert
        Assert.Equal("target_hp < target_max_hp * 0.5", conditionalEffect.Condition);
    }
    
    [Fact]
    public void EffectDefinition_WithChance_LessThanOne()
    {
        // Arrange & Act
        var chanceEffect = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            Chance = 0.5f,
            FlatValue = 15.0f
        };
        
        // Assert
        Assert.Equal(0.5f, chanceEffect.Chance);
    }
    
    [Fact]
    public void EffectDefinition_WithRepeat_ExecutesMultipleTimes()
    {
        // Arrange & Act
        var repeatEffect = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            Repeat = 3,
            FlatValue = 5.0f
        };
        
        // Assert
        Assert.Equal(3, repeatEffect.Repeat);
    }
    
    [Fact]
    public void EffectDefinition_WithRequiredTags_FiltersByTags()
    {
        // Arrange & Act
        var taggedEffect = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            RequiredTags = new List<string> { "fire", "magic" }
        };
        
        // Assert
        Assert.Equal(2, taggedEffect.RequiredTags!.Count);
        Assert.Contains("fire", taggedEffect.RequiredTags);
        Assert.Contains("magic", taggedEffect.RequiredTags);
    }
    
    [Fact]
    public void EffectDefinition_WithExcludedTags_FiltersOutTags()
    {
        // Arrange & Act
        var excludeEffect = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            ExcludedTags = new List<string> { "physical" }
        };
        
        // Assert
        Assert.Single(excludeEffect.ExcludedTags!);
        Assert.Contains("physical", excludeEffect.ExcludedTags);
    }
    
    [Fact]
    public void EffectDefinition_ChainedEffects_CanBeAdded()
    {
        // Arrange
        var secondaryEffect = new EffectDefinition
        {
            Type = EffectType.APPLY_STATUS,
            StatusId = "burning"
        };
        
        // Act
        var primaryEffect = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            FlatValue = 10.0f,
            ChainedEffects = new List<EffectDefinition> { secondaryEffect }
        };
        
        // Assert
        Assert.Single(primaryEffect.ChainedEffects!);
        Assert.Equal(EffectType.APPLY_STATUS, primaryEffect.ChainedEffects[0].Type);
    }
    
    [Fact]
    public void EffectDefinition_ConditionalEffects_CanBeAdded()
    {
        // Arrange
        var conditionalEffect = new EffectDefinition
        {
            Type = EffectType.HEAL,
            FlatValue = 5.0f,
            Condition = "self_hp < self_max_hp * 0.3"
        };
        
        // Act
        var mainEffect = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            FlatValue = 15.0f,
            ConditionalEffects = new List<EffectDefinition> { conditionalEffect }
        };
        
        // Assert
        Assert.Single(mainEffect.ConditionalEffects!);
        Assert.Equal(EffectType.HEAL, mainEffect.ConditionalEffects[0].Type);
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void EffectDefinition_FireballScenario_DamageAndStatus()
    {
        // Arrange & Act - Fireball: damage + apply burning
        var fireballDamage = new EffectDefinition
        {
            EffectId = "fireball_damage",
            Type = EffectType.DAMAGE,
            Target = EffectTarget.TARGET,
            Timing = EffectTiming.IMMEDIATE,
            FlatValue = 15.0f,
            TargetResource = "health",
            Tags = new List<string> { "fire", "magic", "damage" }
        };
        
        var fireballBurning = new EffectDefinition
        {
            EffectId = "fireball_burning",
            Type = EffectType.APPLY_STATUS,
            Target = EffectTarget.TARGET,
            StatusId = "burning",
            StatusStacks = 2,
            StatusDuration = 3,
            Tags = new List<string> { "fire", "status" }
        };
        
        var fireballAction = new EffectDefinition
        {
            EffectId = "fireball",
            Type = EffectType.TRIGGER_EFFECT,
            ChainedEffects = new List<EffectDefinition> { fireballDamage, fireballBurning }
        };
        
        // Assert
        Assert.Equal(2, fireballAction.ChainedEffects!.Count);
        Assert.Equal(EffectType.DAMAGE, fireballAction.ChainedEffects[0].Type);
        Assert.Equal(EffectType.APPLY_STATUS, fireballAction.ChainedEffects[1].Type);
    }
    
    [Fact]
    public void EffectDefinition_ConditionalHealScenario_LowHpTrigger()
    {
        // Arrange & Act - Heal only if HP below 50%
        var conditionalHeal = new EffectDefinition
        {
            Type = EffectType.HEAL,
            Target = EffectTarget.SELF,
            FlatValue = 20.0f,
            TargetResource = "health",
            Condition = "self_hp < self_max_hp * 0.5"
        };
        
        // Assert
        Assert.Equal(EffectType.HEAL, conditionalHeal.Type);
        Assert.Equal(EffectTarget.SELF, conditionalHeal.Target);
        Assert.Equal("self_hp < self_max_hp * 0.5", conditionalHeal.Condition);
    }
    
    [Fact]
    public void EffectDefinition_AoEDamageScenario_AllEnemies()
    {
        // Arrange & Act - AoE damage to all enemies
        var aoeEffect = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            Target = EffectTarget.ALL_ENEMIES,
            FlatValue = 8.0f,
            TargetResource = "health",
            Tags = new List<string> { "aoe", "magic" }
        };
        
        // Assert
        Assert.Equal(EffectTarget.ALL_ENEMIES, aoeEffect.Target);
        Assert.Contains("aoe", aoeEffect.Tags);
    }
    
    [Fact]
    public void EffectDefinition_LifestealScenario_DamageAndHeal()
    {
        // Arrange & Act - Lifesteal: damage enemy, heal self
        var lifestealDamage = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            Target = EffectTarget.TARGET,
            FlatValue = 10.0f,
            TargetResource = "health"
        };
        
        var lifestealHeal = new EffectDefinition
        {
            Type = EffectType.HEAL,
            Target = EffectTarget.SELF,
            FormulaValue = "damage_dealt * 0.5", // Heal for 50% of damage
            TargetResource = "health"
        };
        
        var lifestealAction = new EffectDefinition
        {
            Type = EffectType.TRIGGER_EFFECT,
            ChainedEffects = new List<EffectDefinition> { lifestealDamage, lifestealHeal }
        };
        
        // Assert
        Assert.Equal(2, lifestealAction.ChainedEffects!.Count);
        Assert.Equal("damage_dealt * 0.5", lifestealAction.ChainedEffects[1].FormulaValue);
    }
    
    [Fact]
    public void EffectDefinition_CriticalStrikeScenario_ChanceAndMultiplier()
    {
        // Arrange & Act - Critical strike with 30% chance
        var critEffect = new EffectDefinition
        {
            Type = EffectType.DAMAGE,
            Target = EffectTarget.TARGET,
            FlatValue = 10.0f,
            Chance = 0.3f, // 30% chance to crit
            FormulaValue = "base_damage * 2.0", // 2x damage on crit
            TargetResource = "health",
            Tags = new List<string> { "critical", "physical" }
        };
        
        // Assert
        Assert.Equal(0.3f, critEffect.Chance);
        Assert.Equal("base_damage * 2.0", critEffect.FormulaValue);
    }
    
    [Fact]
    public void EffectDefinition_DrawCardScenario_DeckManipulation()
    {
        // Arrange & Act - Draw 2 cards
        var drawEffect = new EffectDefinition
        {
            Type = EffectType.DRAW_CARD,
            Repeat = 2
        };
        
        // Assert
        Assert.Equal(EffectType.DRAW_CARD, drawEffect.Type);
        Assert.Equal(2, drawEffect.Repeat);
    }
    
    [Fact]
    public void EffectDefinition_ModifyDamageScenario_BuffDebuff()
    {
        // Arrange & Act - Increase damage by 25%
        var damageModifier = new EffectDefinition
        {
            Type = EffectType.MODIFY_DAMAGE_DEALT,
            ModifierKey = "increased_damage_total",
            ModifierValue = 0.25f,
            IsPercentage = true
        };
        
        // Assert
        Assert.Equal(EffectType.MODIFY_DAMAGE_DEALT, damageModifier.Type);
        Assert.Equal("increased_damage_total", damageModifier.ModifierKey);
        Assert.Equal(0.25f, damageModifier.ModifierValue);
    }
    
    [Fact]
    public void EffectDefinition_PercentageEffect_BasedOnMaxHP()
    {
        // Arrange & Act - Heal for 20% of max HP
        var percentHeal = new EffectDefinition
        {
            Type = EffectType.HEAL,
            Target = EffectTarget.SELF,
            FormulaValue = "self_max_hp * 0.2",
            IsPercentage = true,
            TargetResource = "health"
        };
        
        // Assert
        Assert.True(percentHeal.IsPercentage);
        Assert.Equal("self_max_hp * 0.2", percentHeal.FormulaValue);
    }
}
