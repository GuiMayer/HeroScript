using Core.Combat.Models;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat;

public class CombatEntityTests
{
    private static CombatEntity CreateTestEntity(string entityId, float currentHp, float maxHp)
    {
        var healthDef = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultMin = 0,
            DefaultMax = maxHp,
            DefaultCurrent = currentHp,
            CanBeNegative = false
        };

        var healthPool = new ResourcePool
        {
            Definition = healthDef,
            Current = currentHp,
            Maximum = maxHp,
            Minimum = 0
        };

        var resources = new Dictionary<string, ResourcePool>
        {
            ["health"] = healthPool
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
    public void TakeDamage_ShouldReduceHp()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 100, 100);

        // Act
        var newEntity = entity.TakeDamage(30);

        // Assert
        Assert.Equal(70, newEntity.GetResource("health")?.Current);
        Assert.True(newEntity.IsAlive);
    }

    [Fact]
    public void TakeDamage_BelowZero_ShouldCapAtZero()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 20, 100);

        // Act
        var newEntity = entity.TakeDamage(50);

        // Assert
        Assert.Equal(0, newEntity.GetResource("health")?.Current);
        Assert.False(newEntity.IsAlive);
    }

    [Fact]
    public void Heal_ShouldIncreaseHp()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 50, 100);

        // Act
        var newEntity = entity.Heal(30);

        // Assert
        Assert.Equal(80, newEntity.GetResource("health")?.Current);
    }

    [Fact]
    public void Heal_AboveMaximum_ShouldCapAtMaximum()
    {
        // Arrange
        var entity = CreateTestEntity("test-1", 90, 100);

        // Act
        var newEntity = entity.Heal(50);

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
}
