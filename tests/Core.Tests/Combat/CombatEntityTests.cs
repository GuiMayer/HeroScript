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
                    Boundary = ResourceThresholdBoundary.AtMinimum,
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

        var resourceState = new EntityResourceState
        {
            EntityId = entityId,
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
}
