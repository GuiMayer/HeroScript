using Core.Combat;
using Xunit;

namespace Core.Tests.Combat;

public class EntityFactoryTests
{
    private readonly EntityFactory _factory;

    public EntityFactoryTests()
    {
        _factory = new EntityFactory();
    }

    [Fact]
    public void CreateMockEntity_WithDefaultHealth_CreatesEntityWith100Health()
    {
        // Act
        var entity = _factory.CreateMockEntity("test_entity");

        // Assert
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
        var entity = _factory.CreateMockEntity("test_entity", 250f);

        // Assert
        Assert.NotNull(entity);
        Assert.Equal("test_entity", entity.EntityId);
        Assert.Equal(250f, entity.CurrentHp);
        Assert.Equal(250f, entity.MaxHp);
    }

    [Fact]
    public void CreateMockEntity_CreatesHealthResource()
    {
        // Act
        var entity = _factory.CreateMockEntity("test_entity");

        // Assert
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
        var entity = _factory.CreateMockEntity("test_entity");

        // Assert
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
        var entity = _factory.CreateMockEntity("test_entity");

        // Assert
        Assert.True(entity.IsAlive);
    }
}
