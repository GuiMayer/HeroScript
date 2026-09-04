using Core.Combat.Models;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat;

public class CombatEntityTests
{
    private static CombatEntity CreateTestEntity(
        string entityId,
        float currentValue,
        float maximum,
        string resourceId = "health",
        bool defeatsAtMinimum = true)
    {
        var resourceDefinition = new ResourceDefinition
        {
            ResourceId = resourceId,
            DisplayName = resourceId,
            Category = ResourceCategory.VITAL,
            DefaultMin = 0,
            DefaultMax = maximum,
            DefaultCurrent = currentValue,
            CanBeNegative = false,
            ThresholdPolicies = defeatsAtMinimum
                ?
            [
                new ResourceThresholdPolicy
                {
                    PolicyId = "defeat_when_depleted",
                    Comparison = ResourceThresholdComparison.LessThanOrEqual,
                    ThresholdSource = ResourceThresholdSource.Minimum,
                    Consequence = ResourceThresholdConsequence.DefeatOwner
                }
            ]
                : []
        };

        var resourcePool = new ResourcePool
        {
            ResourceId = resourceId,
            Definition = resourceDefinition,
            Current = currentValue,
            Maximum = maximum,
            Minimum = 0
        };

        var resources = new Dictionary<string, ResourcePool>
        {
            [resourceId] = resourcePool
        };

        var resourceState = new ResourceSet
        {
            OwnerId = entityId,
            Resources = resources
        };

        return new CombatEntity
        {
            EntityId = entityId,
            Name = "Test Entity",
            IsHero = false,
            ResourceState = resourceState
        };
    }

    [Fact]
    public void ReduceResource_ShouldReduceSelectedResource()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 100, 100);

        // Act
        var newEntity = entity.ReduceResource("health", 30);

        // Assert
        Assert.Equal(70, newEntity.GetResource("health")?.Current);
        Assert.True(newEntity.IsAlive);
    }

    [Fact]
    public void ReduceResource_BelowMinimum_ShouldCapAndApplyConfiguredDefeat()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 20, 100);

        // Act
        var newEntity = entity.ReduceResource("health", 50);

        // Assert
        Assert.Equal(0, newEntity.GetResource("health")?.Current);
        Assert.False(newEntity.IsAlive);
    }

    [Fact]
    public void IncreaseResource_ShouldIncreaseSelectedResource()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 50, 100);

        // Act
        var newEntity = entity.IncreaseResource("health", 30);

        // Assert
        Assert.Equal(80, newEntity.GetResource("health")?.Current);
    }

    [Fact]
    public void IncreaseResource_AboveMaximum_ShouldCapAtMaximum()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 90, 100);

        // Act
        var newEntity = entity.IncreaseResource("health", 50);

        // Assert
        Assert.Equal(100, newEntity.GetResource("health")?.Current);
    }

    [Fact]
    public void IsAlive_WithPositiveHp_ShouldReturnTrue()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 1, 100);

        // Assert
        Assert.True(entity.IsAlive);
    }

    [Fact]
    public void IsAlive_WithZeroHp_ShouldReturnFalse()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 0, 100);

        // Assert
        Assert.False(entity.IsAlive);
    }

    [Fact]
    public void IsAlive_DoesNotInferDefeatFromHealthNameOrVitalCategory()
    {
        var entity = CreateTestEntity(
            "test-1",
            currentValue: 0,
            maximum: 100,
            resourceId: "health",
            defeatsAtMinimum: false);

        Assert.True(entity.IsAlive);
    }

    [Fact]
    public void IsAlive_CanBeDefeatedByAnyConfiguredResource()
    {
        var entity = CreateTestEntity(
            "test-1",
            currentValue: 0,
            maximum: 10,
            resourceId: "mana",
            defeatsAtMinimum: true);

        Assert.False(entity.IsAlive);
    }

    [Fact]
    public void IsAlive_SupportsAConstantThresholdIndependentOfPoolBounds()
    {
        var definition = new ResourceDefinition
        {
            ResourceId = "morale",
            DisplayName = "Morale",
            DefaultMin = 0,
            DefaultMax = 100,
            ThresholdPolicies =
            [
                new ResourceThresholdPolicy
                {
                    PolicyId = "retreat_below_three",
                    Comparison = ResourceThresholdComparison.LessThan,
                    ThresholdSource = ResourceThresholdSource.Constant,
                    ThresholdValue = 3,
                    Consequence = ResourceThresholdConsequence.DefeatOwner
                }
            ]
        };
        var entity = new CombatEntity
        {
            EntityId = "test-1",
            Name = "Test Entity",
            ResourceState = new ResourceSet
            {
                OwnerId = "test-1",
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["morale"] = new()
                    {
                        ResourceId = "morale",
                        Definition = definition,
                        Current = 2,
                        Minimum = 0,
                        Maximum = 100
                    }
                }
            }
        };

        Assert.False(entity.IsAlive);
    }

    [Fact]
    public void IsAlive_UsesPriorityAndStablePolicyIdToResolveConflicts()
    {
        var definition = new ResourceDefinition
        {
            ResourceId = "focus",
            DisplayName = "Focus",
            DefaultMin = 0,
            DefaultMax = 10,
            ThresholdPolicies =
            [
                new ResourceThresholdPolicy
                {
                    PolicyId = "low_priority_defeat",
                    Comparison = ResourceThresholdComparison.LessThanOrEqual,
                    ThresholdSource = ResourceThresholdSource.Constant,
                    ThresholdValue = 5,
                    Consequence = ResourceThresholdConsequence.DefeatOwner,
                    Priority = 10
                },
                new ResourceThresholdPolicy
                {
                    PolicyId = "high_priority_override",
                    Comparison = ResourceThresholdComparison.Equal,
                    ThresholdSource = ResourceThresholdSource.Constant,
                    ThresholdValue = 5,
                    Consequence = ResourceThresholdConsequence.None,
                    Priority = 100
                }
            ]
        };
        var pool = new ResourcePool
        {
            ResourceId = "focus",
            Definition = definition,
            Current = 5,
            Minimum = 0,
            Maximum = 10
        };
        var entity = new CombatEntity
        {
            EntityId = "test-1",
            Name = "Test Entity",
            ResourceState = new ResourceSet
            {
                OwnerId = "test-1",
                Resources = new Dictionary<string, ResourcePool> { ["focus"] = pool }
            }
        };

        var facts = ResourceThresholdEvaluator.Evaluate(pool);

        Assert.Equal(["high_priority_override", "low_priority_defeat"], facts.Select(fact => fact.PolicyId));
        Assert.True(entity.IsAlive);
    }
}
