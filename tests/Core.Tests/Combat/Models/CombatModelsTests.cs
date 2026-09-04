using System;
using System.Collections.Generic;
using Core.Combat.Models;
using Core.Effects;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat.Models;

/// <summary>
/// Comprehensive tests for Combat/Models namespace
/// Covers all model classes, records, and enums
/// </summary>
[Trait("Category", "Unit")]
public class CombatModelsTests
{
    // ==================== RESOURCE COST TESTS ====================
    
    [Fact]
    public void ResourceCost_Construction_SetsProperties()
    {
        // Arrange & Act
        var cost = new ResourceCost
        {
            ResourceId = "energy",
            Amount = 10.5f,
            Formula = "level * 2",
            AllowOverdraft = true
        };
        
        // Assert
        Assert.Equal("energy", cost.ResourceId);
        Assert.Equal(10.5f, cost.Amount);
        Assert.Equal("level * 2", cost.Formula);
        Assert.True(cost.AllowOverdraft);
    }
    
    [Fact]
    public void ResourceCost_DefaultValues_Work()
    {
        // Arrange & Act
        var cost = new ResourceCost();
        
        // Assert
        Assert.Equal(string.Empty, cost.ResourceId);
        Assert.Equal(0f, cost.Amount);
        Assert.Null(cost.Formula);
        Assert.False(cost.AllowOverdraft);
    }
    
    [Fact]
    public void ResourceCost_RecordEquality_Works()
    {
        // Arrange
        var cost1 = new ResourceCost { ResourceId = "mana", Amount = 5 };
        var cost2 = new ResourceCost { ResourceId = "mana", Amount = 5 };
        var cost3 = new ResourceCost { ResourceId = "mana", Amount = 10 };
        
        // Assert
        Assert.Equal(cost1, cost2);
        Assert.NotEqual(cost1, cost3);
    }
    
    // ==================== ACTION COSTS TESTS ====================
    
    [Fact]
    public void ActionCosts_EmptyCosts_CanAfford()
    {
        // Arrange
        var costs = new ActionCosts();
        var resources = new Dictionary<string, ResourcePool>();
        
        // Act
        var canAfford = costs.CanAfford(resources);
        
        // Assert
        Assert.True(canAfford);
    }
    
    [Fact]
    public void ActionCosts_WithSufficientResources_CanAfford()
    {
        // Arrange
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new() { ResourceId = "energy", Amount = 5 }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>
        {
            ["energy"] = new ResourcePool
            {
                ResourceId = "energy",
                Current = 10,
                Maximum = 10
            }
        };
        
        // Act
        var canAfford = costs.CanAfford(resources);
        
        // Assert
        Assert.True(canAfford);
    }
    
    [Fact]
    public void ActionCosts_WithInsufficientResources_CannotAfford()
    {
        // Arrange
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new() { ResourceId = "energy", Amount = 15 }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>
        {
            ["energy"] = new ResourcePool
            {
                ResourceId = "energy",
                Current = 10,
                Maximum = 10
            }
        };
        
        // Act
        var canAfford = costs.CanAfford(resources);
        
        // Assert
        Assert.False(canAfford);
    }
    
    [Fact]
    public void ActionCosts_WithMissingResource_CannotAfford()
    {
        // Arrange
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new() { ResourceId = "mana", Amount = 5 }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>();
        
        // Act
        var canAfford = costs.CanAfford(resources);
        
        // Assert
        Assert.False(canAfford);
    }
    
    [Fact]
    public void ActionCosts_GetAffordabilityError_ReturnsMissingResourceMessage()
    {
        // Arrange
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new() { ResourceId = "nonexistent", Amount = 5 }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>();
        
        // Act
        var error = costs.GetAffordabilityError(resources);
        
        // Assert
        Assert.NotNull(error);
        Assert.Contains("Resource not found", error);
        Assert.Contains("nonexistent", error);
    }
    
    [Fact]
    public void ActionCosts_GetAffordabilityError_ReturnsInsufficientMessage()
    {
        // Arrange
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new() { ResourceId = "energy", Amount = 20 }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>
        {
            ["energy"] = new ResourcePool
            {
                ResourceId = "energy",
                Current = 10,
                Maximum = 10,
                Definition = new ResourceDefinition { DisplayName = "Energy" }
            }
        };
        
        // Act
        var error = costs.GetAffordabilityError(resources);
        
        // Assert
        Assert.NotNull(error);
        Assert.Contains("Insufficient", error);
        Assert.Contains("has 10", error);
        Assert.Contains("needs 20", error);
    }
    
    [Fact]
    public void ActionCosts_WithOverdraft_AllowsNegativeResources()
    {
        // Arrange
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new() { ResourceId = "energy", Amount = 15, AllowOverdraft = true }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>
        {
            ["energy"] = new ResourcePool
            {
                ResourceId = "energy",
                Current = 10,
                Maximum = 10
            }
        };
        
        // Act
        var canAfford = costs.CanAfford(resources);
        
        // Assert
        Assert.True(canAfford);
    }
    
    // ==================== ALTERNATIVE COST OPTION TESTS ====================
    
    [Fact]
    public void AlternativeCostOption_CanAfford_WithSufficientResources()
    {
        // Arrange
        var option = new AlternativeCostOption
        {
            OptionId = "mana_cost",
            Description = "Pay 10 mana",
            Costs = new List<ResourceCost>
            {
                new() { ResourceId = "mana", Amount = 10 }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>
        {
            ["mana"] = new ResourcePool
            {
                ResourceId = "mana",
                Current = 15,
                Maximum = 20
            }
        };
        
        // Act
        var canAfford = option.CanAfford(resources);
        
        // Assert
        Assert.True(canAfford);
    }
    
    [Fact]
    public void AlternativeCostOption_GetAffordabilityError_ReturnsError()
    {
        // Arrange
        var option = new AlternativeCostOption
        {
            OptionId = "health_cost",
            Description = "Pay 10 health",
            Costs = new List<ResourceCost>
            {
                new() { ResourceId = "health", Amount = 10 }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>
        {
            ["health"] = new ResourcePool
            {
                ResourceId = "health",
                Current = 5,
                Maximum = 100,
                Definition = new ResourceDefinition { DisplayName = "Health" }
            }
        };
        
        // Act
        var error = option.GetAffordabilityError(resources);
        
        // Assert
        Assert.NotNull(error);
        Assert.Contains("Insufficient", error);
    }
    
    // ==================== ACTION TYPE ENUM TESTS ====================
    
    [Fact]
    public void ActionType_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<ActionType>();
        
        // Assert
        Assert.Contains(ActionType.BASIC_ATTACK, values);
        Assert.Contains(ActionType.POWER, values);
        Assert.Contains(ActionType.PASS, values);
        Assert.Contains(ActionType.END_TURN, values);
        Assert.Contains(ActionType.TRANSITION_PHASE, values);
        Assert.Contains(ActionType.PASS_PRIORITY, values);
        Assert.Contains(ActionType.DECLARE_ATTACKER, values);
        Assert.Contains(ActionType.DECLARE_BLOCKER, values);
        Assert.Contains(ActionType.PLAY_INSTANT, values);
        Assert.Contains(ActionType.ACTIVATE_ABILITY, values);
    }
    
    // ==================== COMBAT STATUS ENUM TESTS ====================
    
    [Fact]
    public void CombatStatus_AllValuesAreDefined()
    {
        // Arrange & Act
        var values = Enum.GetValues<CombatStatus>();
        
        // Assert
        Assert.Contains(CombatStatus.ACTIVE, values);
        Assert.Contains(CombatStatus.VICTORY, values);
        Assert.Contains(CombatStatus.DEFEAT, values);
        Assert.Contains(CombatStatus.ABANDONED, values);
    }
    
    // ==================== COMBAT ACTION TESTS ====================
    
    [Fact]
    public void CombatAction_Construction_SetsAllProperties()
    {
        // Arrange
        var actionId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;
        
        // Act
        var action = new CombatAction
        {
            ActionId = actionId,
            Timestamp = timestamp,
            Turn = 3,
            ActorId = "hero_1",
            ActionType = ActionType.POWER,
            PowerId = "fireball",
            TargetId = "enemy_1",
            Applications =
            [
                new EffectApplicationRecord
                {
                    EffectInstanceId = "fireball:ward",
                    EffectType = EffectType.MODIFY_RESOURCE,
                    TargetEntityId = "enemy_1",
                    ResourceId = "ward",
                    PreviousValue = 30,
                    CurrentValue = 5
                }
            ]
        };
        
        // Assert
        Assert.Equal(actionId, action.ActionId);
        Assert.Equal(timestamp, action.Timestamp);
        Assert.Equal(3, action.Turn);
        Assert.Equal("hero_1", action.ActorId);
        Assert.Equal(ActionType.POWER, action.ActionType);
        Assert.Equal("fireball", action.PowerId);
        Assert.Equal("enemy_1", action.TargetId);
        var application = Assert.Single(action.Applications);
        Assert.Equal("ward", application.ResourceId);
        Assert.Equal(5, application.CurrentValue);
    }
    
    [Fact]
    public void CombatAction_DefaultValues_AreDeterministicSentinels()
    {
        // Arrange & Act
        var action = new CombatAction();
        
        // Assert
        Assert.Equal(Guid.Empty, action.ActionId);
        Assert.Equal(DateTime.UnixEpoch, action.Timestamp);
    }
    
    [Fact]
    public void CombatAction_BasicAttack_HasCorrectProperties()
    {
        // Arrange & Act
        var action = new CombatAction
        {
            ActorId = "hero",
            ActionType = ActionType.BASIC_ATTACK,
            TargetId = "enemy",
            Applications =
            [
                new EffectApplicationRecord
                {
                    EffectInstanceId = "attack:rage",
                    EffectType = EffectType.MODIFY_RESOURCE,
                    TargetEntityId = "hero",
                    ResourceId = "rage",
                    PreviousValue = 0,
                    CurrentValue = 1
                }
            ]
        };
        
        // Assert
        Assert.Equal(ActionType.BASIC_ATTACK, action.ActionType);
        Assert.Null(action.PowerId);
        Assert.Equal("rage", Assert.Single(action.Applications).ResourceId);
    }
    
    [Fact]
    public void CombatAction_PassAction_HasNullTargetAndPower()
    {
        // Arrange & Act
        var action = new CombatAction
        {
            ActorId = "hero",
            ActionType = ActionType.PASS
        };
        
        // Assert
        Assert.Equal(ActionType.PASS, action.ActionType);
        Assert.Null(action.PowerId);
        Assert.Null(action.TargetId);
    }
    
    // ==================== COMBAT RESULT TESTS ====================
    
    [Fact]
    public void CombatResult_Construction_SetsAllProperties()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var duration = TimeSpan.FromMinutes(5);
        
        // Act
        var result = new CombatResult
        {
            CombatId = combatId,
            Status = CombatStatus.VICTORY,
            TotalTurns = 10,
            TotalActions = 25,
            Duration = duration
        };
        
        // Assert
        Assert.Equal(combatId, result.CombatId);
        Assert.Equal(CombatStatus.VICTORY, result.Status);
        Assert.Equal(10, result.TotalTurns);
        Assert.Equal(25, result.TotalActions);
        Assert.Equal(duration, result.Duration);
    }
    
    [Fact]
    public void CombatResult_RecordEquality_Works()
    {
        // Arrange
        var id = Guid.NewGuid();
        var result1 = new CombatResult
        {
            CombatId = id,
            Status = CombatStatus.VICTORY,
            TotalTurns = 5
        };
        var result2 = new CombatResult
        {
            CombatId = id,
            Status = CombatStatus.VICTORY,
            TotalTurns = 5
        };
        
        // Assert
        Assert.Equal(result1, result2);
    }
    
    // ==================== RESOURCE SET TESTS ====================
    
    [Fact]
    public void ResourceSet_Get_ReturnsCorrectPool()
    {
        // Arrange
        var healthPool = new ResourcePool
        {
            ResourceId = "health",
            Current = 50,
            Maximum = 100
        };
        
        var state = new ResourceSet
        {
            OwnerId = "hero_1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = healthPool
            }
        };
        
        // Act
        var retrieved = state.Get("health");
        
        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("health", retrieved.ResourceId);
        Assert.Equal(50, retrieved.Current);
    }
    
    [Fact]
    public void ResourceSet_Get_ReturnsNullForMissing()
    {
        // Arrange
        var state = new ResourceSet
        {
            OwnerId = "hero_1",
            Resources = new Dictionary<string, ResourcePool>()
        };
        
        // Act
        var retrieved = state.Get("nonexistent");
        
        // Assert
        Assert.Null(retrieved);
    }
    
    [Fact]
    public void ResourceSet_Contains_WorksCorrectly()
    {
        // Arrange
        var state = new ResourceSet
        {
            OwnerId = "hero_1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new ResourcePool { ResourceId = "health" }
            }
        };
        
        // Act & Assert
        Assert.True(state.Contains("health"));
        Assert.False(state.Contains("mana"));
    }
    
    [Fact]
    public void ResourceSet_Apply_CreatesNewInstance()
    {
        // Arrange
        var definition = new ResourceDefinition
        {
            ResourceId = "health",
            DefaultMax = 100
        };
        var originalPool = new ResourcePool
        {
            ResourceId = "health",
            Current = 50,
            Maximum = 100,
            Definition = definition
        };
        
        var state = new ResourceSet
        {
            OwnerId = "hero_1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = originalPool
            }
        };
        
        // Act
        var result = state.Apply(
        [
            new ResolvedResourceMutation
            {
                MutationId = "test:set-health",
                ResourceId = "health",
                Operation = ResourceMutationOperation.Set,
                Value = 75
            }
        ]);
        
        // Assert
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(50, state.Get("health")!.Current); // Original unchanged
        Assert.Equal(75, result.Value.State.Get("health")!.Current); // New updated
    }

    [Fact]
    public void ResourceSet_Apply_RejectsMismatchedPoolId()
    {
        var definition = new ResourceDefinition { ResourceId = "mana", DefaultMax = 100 };
        var state = new ResourceSet
        {
            OwnerId = "hero_1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new ResourcePool
                {
                    ResourceId = "mana",
                    Current = 50,
                    Maximum = 100,
                    Definition = definition
                }
            }
        };

        var result = state.Apply(
        [
            new ResolvedResourceMutation
            {
                MutationId = "test:set-health",
                ResourceId = "health",
                Operation = ResourceMutationOperation.Set,
                Value = 75
            }
        ]);

        Assert.True(result.IsFailure);
        Assert.Contains("does not match", result.Error);
    }
    
    [Fact]
    public void ResourceSet_Apply_UpdatesMultipleAtomically()
    {
        // Arrange
        var healthDefinition = new ResourceDefinition { ResourceId = "health", DefaultMax = 100 };
        var energyDefinition = new ResourceDefinition { ResourceId = "energy", DefaultMax = 10 };
        var state = new ResourceSet
        {
            OwnerId = "hero_1",
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new ResourcePool
                {
                    ResourceId = "health",
                    Current = 50,
                    Maximum = 100,
                    Definition = healthDefinition
                },
                ["energy"] = new ResourcePool
                {
                    ResourceId = "energy",
                    Current = 5,
                    Maximum = 10,
                    Definition = energyDefinition
                }
            }
        };
        
        // Act
        var result = state.Apply(
        [
            new ResolvedResourceMutation
            {
                MutationId = "test:set-health",
                ResourceId = "health",
                Operation = ResourceMutationOperation.Set,
                Value = 75
            },
            new ResolvedResourceMutation
            {
                MutationId = "test:set-energy",
                ResourceId = "energy",
                Operation = ResourceMutationOperation.Set,
                Value = 10
            }
        ]);
        
        // Assert
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(75, result.Value.State.Get("health")!.Current);
        Assert.Equal(10, result.Value.State.Get("energy")!.Current);
        
        // Original unchanged
        Assert.Equal(50, state.Get("health")!.Current);
        Assert.Equal(5, state.Get("energy")!.Current);
    }
    
    // ==================== ACTION DEFINITION TESTS ====================
    
    [Fact]
    public void ActionDefinition_FullConstruction_SetsAllProperties()
    {
        // Arrange & Act
        var action = new ActionDefinition
        {
            ActionId = "fireball",
            DisplayName = "Fireball",
            Description = "Launch a ball of fire",
            ActionType = ActionType.POWER,
            RequiresTarget = true,
            MultiTarget = false,
            Cooldown = 2,
            Tags = new List<string> { "fire", "damage", "ranged" }
        };
        
        // Assert
        Assert.Equal("fireball", action.ActionId);
        Assert.Equal("Fireball", action.DisplayName);
        Assert.Equal("Launch a ball of fire", action.Description);
        Assert.Equal(ActionType.POWER, action.ActionType);
        Assert.True(action.RequiresTarget);
        Assert.False(action.MultiTarget);
        Assert.Equal(2, action.Cooldown);
        Assert.Equal(3, action.Tags.Count);
        Assert.Contains("fire", action.Tags);
    }
    
    [Fact]
    public void ActionDefinition_DefaultValues_Work()
    {
        // Arrange & Act
        var action = new ActionDefinition();
        
        // Assert
        Assert.Equal(string.Empty, action.ActionId);
        Assert.True(action.RequiresTarget); // Default is true
        Assert.False(action.MultiTarget); // Default is false
        Assert.Equal(0, action.Cooldown);
        Assert.Empty(action.Tags);
        Assert.Empty(action.Effects);
    }
    
    // ==================== INTEGRATION TESTS ====================
    
    [Fact]
    public void ActionCosts_WithAlternatives_CanAffordAnyOption()
    {
        // Arrange
        var costs = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new()
                {
                    OptionId = "energy_cost",
                    Description = "10 energy",
                    Costs = new List<ResourceCost>
                    {
                        new() { ResourceId = "energy", Amount = 10 }
                    }
                },
                new()
                {
                    OptionId = "health_cost",
                    Description = "5 health",
                    Costs = new List<ResourceCost>
                    {
                        new() { ResourceId = "health", Amount = 5 }
                    }
                }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>
        {
            ["energy"] = new ResourcePool { ResourceId = "energy", Current = 3, Maximum = 10 },
            ["health"] = new ResourcePool { ResourceId = "health", Current = 20, Maximum = 100 }
        };
        
        // Act
        var canAfford = costs.CanAfford(resources);
        var affordableOptions = costs.GetAffordableOptions(resources);
        
        // Assert
        Assert.True(canAfford); // Can afford health option
        Assert.Single(affordableOptions);
        Assert.Equal("health_cost", affordableOptions[0].OptionId);
    }
    
    [Fact]
    public void ActionCosts_GetOption_ReturnsCorrectOption()
    {
        // Arrange
        var costs = new ActionCosts
        {
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new() { OptionId = "option1", Description = "First" },
                new() { OptionId = "option2", Description = "Second" }
            }
        };
        
        // Act
        var option = costs.GetOption("option2");
        
        // Assert
        Assert.NotNull(option);
        Assert.Equal("option2", option.OptionId);
        Assert.Equal("Second", option.Description);
    }
    
    [Fact]
    public void ActionCosts_CanAffordOption_ChecksSpecificOption()
    {
        // Arrange
        var costs = new ActionCosts
        {
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new()
                {
                    OptionId = "expensive",
                    Costs = new List<ResourceCost>
                    {
                        new() { ResourceId = "gold", Amount = 100 }
                    }
                }
            }
        };
        
        var resources = new Dictionary<string, ResourcePool>
        {
            ["gold"] = new ResourcePool { ResourceId = "gold", Current = 50, Maximum = 100 }
        };
        
        // Act
        var canAfford = costs.CanAffordOption("expensive", resources);
        
        // Assert
        Assert.False(canAfford);
    }
}
