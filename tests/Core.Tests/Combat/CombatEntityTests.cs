using Core.Combat;
using Xunit;

namespace Core.Tests.Combat;

public class CombatEntityTests
{
    [Fact]
    public void TakeDamage_ShouldReduceHp()
    {
        // Arrange
        var entity = new CombatEntity
        {
            EntityId = "test-1",
            CurrentHp = 100,
            MaxHp = 100
        };

        // Act
        var newEntity = entity.TakeDamage(30);

        // Assert
        Assert.Equal(70, newEntity.CurrentHp);
        Assert.True(newEntity.IsAlive);
    }

    [Fact]
    public void TakeDamage_BelowZero_ShouldCapAtZero()
    {
        // Arrange
        var entity = new CombatEntity
        {
            EntityId = "test-1",
            CurrentHp = 20,
            MaxHp = 100
        };

        // Act
        var newEntity = entity.TakeDamage(50);

        // Assert
        Assert.Equal(0, newEntity.CurrentHp);
        Assert.False(newEntity.IsAlive);
    }

    [Fact]
    public void Heal_ShouldIncreaseHp()
    {
        // Arrange
        var entity = new CombatEntity
        {
            EntityId = "test-1",
            CurrentHp = 50,
            MaxHp = 100
        };

        // Act
        var newEntity = entity.Heal(30);

        // Assert
        Assert.Equal(80, newEntity.CurrentHp);
    }

    [Fact]
    public void Heal_AboveMaximum_ShouldCapAtMaximum()
    {
        // Arrange
        var entity = new CombatEntity
        {
            EntityId = "test-1",
            CurrentHp = 90,
            MaxHp = 100
        };

        // Act
        var newEntity = entity.Heal(50);

        // Assert
        Assert.Equal(100, newEntity.CurrentHp);
    }

    [Fact]
    public void IsAlive_WithPositiveHp_ShouldReturnTrue()
    {
        // Arrange
        var entity = new CombatEntity { CurrentHp = 1, MaxHp = 100 };

        // Assert
        Assert.True(entity.IsAlive);
    }

    [Fact]
    public void IsAlive_WithZeroHp_ShouldReturnFalse()
    {
        // Arrange
        var entity = new CombatEntity { CurrentHp = 0, MaxHp = 100 };

        // Assert
        Assert.False(entity.IsAlive);
    }
}
