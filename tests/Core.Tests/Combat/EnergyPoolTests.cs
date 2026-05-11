using Core.Combat;
using Core.Combat.Models;
using Xunit;

namespace Core.Tests.Combat;

public class EnergyPoolTests
{
    [Fact]
    public void Spend_WithSufficientEnergy_ShouldSucceed()
    {
        // Arrange
        var pool = new EnergyPool { Current = 5, Maximum = 10 };

        // Act
        var newPool = pool.Spend(3);

        // Assert
        Assert.Equal(2, newPool.Current);
        Assert.Equal(10, newPool.Maximum);
    }

    [Fact]
    public void Spend_WithInsufficientEnergy_ShouldThrow()
    {
        // Arrange
        var pool = new EnergyPool { Current = 2, Maximum = 10 };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => pool.Spend(5));
    }

    [Fact]
    public void Gain_BelowMaximum_ShouldIncrease()
    {
        // Arrange
        var pool = new EnergyPool { Current = 5, Maximum = 10 };

        // Act
        var newPool = pool.Gain(3);

        // Assert
        Assert.Equal(8, newPool.Current);
    }

    [Fact]
    public void Gain_AboveMaximum_ShouldCapAtMaximum()
    {
        // Arrange
        var pool = new EnergyPool { Current = 8, Maximum = 10 };

        // Act
        var newPool = pool.Gain(5);

        // Assert
        Assert.Equal(10, newPool.Current);
    }

    [Fact]
    public void CanAfford_WithSufficientEnergy_ShouldReturnTrue()
    {
        // Arrange
        var pool = new EnergyPool { Current = 5, Maximum = 10 };

        // Act
        var canAfford = pool.CanAfford(3);

        // Assert
        Assert.True(canAfford);
    }

    [Fact]
    public void CanAfford_WithInsufficientEnergy_ShouldReturnFalse()
    {
        // Arrange
        var pool = new EnergyPool { Current = 2, Maximum = 10 };

        // Act
        var canAfford = pool.CanAfford(5);

        // Assert
        Assert.False(canAfford);
    }

    [Fact]
    public void Reset_ShouldSetCurrentToMaximum()
    {
        // Arrange
        var pool = new EnergyPool { Current = 3, Maximum = 10 };

        // Act
        var newPool = pool.Reset();

        // Assert
        Assert.Equal(10, newPool.Current);
    }
}
