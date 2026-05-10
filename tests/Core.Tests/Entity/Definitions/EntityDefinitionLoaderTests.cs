using Core.Entity.Definitions;
using Core.Logging;
using Xunit;

namespace Core.Tests.Entity.Definitions;

public class EntityDefinitionLoaderTests
{
    private readonly string _testDataPath;
    
    public EntityDefinitionLoaderTests()
    {
        // Usar o caminho absoluto baseado no workspace root
        // Assumindo que os testes rodam de tests/Core.Tests/bin/Debug/net10.0
        var workspaceRoot = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
        _testDataPath = Path.Combine(workspaceRoot, "data", "configs", "default", "Entities");
    }
    
    [Fact]
    public void LoadDefinition_ShouldLoadValidDefinition()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        
        // Act
        var result = loader.LoadDefinition("player_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("player_warrior", result.Value!.DefinitionId);
        Assert.Equal("Warrior", result.Value.DisplayName);
        Assert.Equal(Core.Entity.EntityType.PLAYER, result.Value.Type);
    }
    
    [Fact]
    public void LoadDefinition_ShouldLoadResources()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        
        // Act
        var result = loader.LoadDefinition("player_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Resources);
        Assert.True(result.Value.Resources.Resources.ContainsKey("health"));
        Assert.Equal(150, result.Value.Resources.Resources["health"].Current);
        Assert.Equal(150, result.Value.Resources.Resources["health"].Max);
    }
    
    [Fact]
    public void LoadDefinition_ShouldLoadStats()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        
        // Act
        var result = loader.LoadDefinition("player_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Stats);
        Assert.Equal(18, result.Value.Stats.Strength);
        Assert.Equal(12, result.Value.Stats.Dexterity);
        Assert.Equal(16, result.Value.Stats.Constitution);
    }
    
    [Fact]
    public void LoadDefinition_ShouldLoadAI()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        
        // Act
        var result = loader.LoadDefinition("enemy_goblin");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.AI);
        Assert.Equal("aggressive", result.Value.AI.BehaviorTree);
        Assert.Equal(0.3f, result.Value.AI.LowHealthThreshold);
        Assert.Equal(0.2f, result.Value.AI.FleeHealthThreshold);
    }
    
    [Fact]
    public void LoadDefinition_ShouldSupportDeltaInheritance()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        
        // Act
        var result = loader.LoadDefinition("enemy_orc_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        
        // Deve herdar tipo de enemy_goblin
        Assert.Equal(Core.Entity.EntityType.ENEMY, result.Value!.Type);
        
        // Deve sobrescrever nome
        Assert.Equal("Orc Warrior", result.Value.DisplayName);
        
        // Deve sobrescrever stats específicos mas manter outros
        Assert.Equal(16, result.Value.Stats!.Strength); // Sobrescrito
        Assert.Equal(14, result.Value.Stats.Dexterity); // Herdado de goblin
        
        // Deve sobrescrever AI behavior
        Assert.Equal("balanced", result.Value.AI!.BehaviorTree);
    }
    
    [Fact]
    public void LoadDefinition_ShouldFail_WhenFileNotFound()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        
        // Act
        var result = loader.LoadDefinition("nonexistent");
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }
    
    [Fact]
    public void LoadDefinition_ShouldCacheDefinitions()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        
        // Act
        var result1 = loader.LoadDefinition("player_warrior");
        var result2 = loader.LoadDefinition("player_warrior");
        
        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Same(result1.Value, result2.Value); // Deve retornar mesma instância do cache
    }
    
    [Fact]
    public void GetDefinition_ShouldReturnCachedDefinition()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        loader.LoadDefinition("player_warrior");
        
        // Act
        var definition = loader.GetDefinition("player_warrior");
        
        // Assert
        Assert.NotNull(definition);
        Assert.Equal("player_warrior", definition!.DefinitionId);
    }
    
    [Fact]
    public void GetDefinition_ShouldReturnNull_WhenNotCached()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        
        // Act
        var definition = loader.GetDefinition("player_warrior");
        
        // Assert
        Assert.Null(definition);
    }
    
    [Fact]
    public void ClearCache_ShouldClearAllDefinitions()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        loader.LoadDefinition("player_warrior");
        
        // Act
        loader.ClearCache();
        var definition = loader.GetDefinition("player_warrior");
        
        // Assert
        Assert.Null(definition);
    }
    
    [Fact]
    public void ReloadDefinition_ShouldReloadFromFile()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        loader.LoadDefinition("player_warrior");
        
        // Act
        var result = loader.ReloadDefinition("player_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }
    
    [Fact]
    public void LoadAllDefinitions_ShouldLoadAllJsonFiles()
    {
        // Arrange
        var loader = new EntityDefinitionLoader(_testDataPath, new ConsoleLogger("Test"));
        
        // Act
        var result = loader.LoadAllDefinitions();
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!);
        Assert.True(result.Value.ContainsKey("player_warrior"));
        Assert.True(result.Value.ContainsKey("enemy_goblin"));
    }
}
