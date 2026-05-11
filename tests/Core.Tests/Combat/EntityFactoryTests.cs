using Core.Combat;
using Core.Combat.Models;
using Core.Logging;
using Xunit;

namespace Core.Tests.Combat;

public class EntityFactoryTests
{
    private readonly EntityFactory _factory;
    private readonly ILogger _logger;

    public EntityFactoryTests()
    {
        _logger = NullLogger.Instance;
        _factory = new EntityFactory(_logger);
    }

    [Fact]
    public void CreateMockEntity_WithDefaultHealth_CreatesEntityWith100Health()
    {
        // Act
        var result = _factory.CreateMockEntity("test_entity");

        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value;
        Assert.NotNull(entity);
        Assert.Equal("test_entity", entity.EntityId);
        Assert.Equal("test_entity", entity.Name);
        Assert.False(entity.IsHero);
        Assert.Equal(100f, entity.CurrentHp);
        Assert.Equal(100f, entity.MaxHp);
    }

    [Fact]
    public void CreateMockEntity_WithCustomHealth_CreatesEntityWithSpecifiedHealth()
    {
        // Act
        var result = _factory.CreateMockEntity("test_entity", 250f);

        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value;
        Assert.NotNull(entity);
        Assert.Equal("test_entity", entity.EntityId);
        Assert.Equal(250f, entity.CurrentHp);
        Assert.Equal(250f, entity.MaxHp);
    }

    [Fact]
    public void CreateMockEntity_CreatesHealthResource()
    {
        // Act
        var result = _factory.CreateMockEntity("test_entity");

        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value;
        Assert.NotNull(entity.ResourceState);
        Assert.NotNull(entity.ResourceState.Resources);
        Assert.True(entity.ResourceState.Resources.ContainsKey("health"));
        
        var healthPool = entity.ResourceState.Resources["health"];
        Assert.Equal("health", healthPool.ResourceId);
        Assert.Equal(100f, healthPool.Current);
        Assert.Equal(100f, healthPool.Maximum);
        Assert.Equal(0f, healthPool.Minimum);
    }

    [Fact]
    public void CreateMockEntity_CreatesValidResourceDefinition()
    {
        // Act
        var result = _factory.CreateMockEntity("test_entity");

        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value;
        var healthPool = entity.ResourceState.Resources["health"];
        Assert.NotNull(healthPool.Definition);
        Assert.Equal("health", healthPool.Definition.ResourceId);
        Assert.Equal("Health", healthPool.Definition.DisplayName);
        Assert.False(healthPool.Definition.CanBeNegative);
        Assert.False(healthPool.Definition.CanExceedMax);
    }

    [Fact]
    public void CreateMockEntity_EntityIsAlive()
    {
        // Act
        var result = _factory.CreateMockEntity("test_entity");

        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value;
        Assert.True(entity.IsAlive);
    }

    [Fact]
    public void CreateMockEntity_WithEmptyId_ReturnsFailure()
    {
        // Act
        var result = _factory.CreateMockEntity("");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be empty", result.Error);
    }

    [Fact]
    public void CreateMockEntity_WithZeroHealth_ReturnsFailure()
    {
        // Act
        var result = _factory.CreateMockEntity("test_entity", 0f);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("greater than zero", result.Error);
    }

    [Fact]
    public void CreateMockEntity_WithNegativeHealth_ReturnsFailure()
    {
        // Act
        var result = _factory.CreateMockEntity("test_entity", -10f);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("greater than zero", result.Error);
    }

    [Fact]
    public void CreateMockEntity_WithExcessiveHealth_ReturnsFailure()
    {
        // Act
        var result = _factory.CreateMockEntity("test_entity", 20000f);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot exceed", result.Error);
    }
}
