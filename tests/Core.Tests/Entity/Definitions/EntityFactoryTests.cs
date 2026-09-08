using Core.Entity.Definitions;
using Core.Config;
using Core.Entity.Components;
using Core.Entity.Controllers;
using Core.Logging;
using Core.Resources;
using Core.Common;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Entity.Definitions;

public class EntityFactoryTests
{
    private readonly string _testDataPath;
    private readonly Mock<IResourceManager> _mockResourceManager;
    
    public EntityFactoryTests()
    {
        // Usar o caminho absoluto baseado no workspace root
        var workspaceRoot = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
        _testDataPath = Path.Combine(workspaceRoot, "data", "configs", "default", "Resources", "entities");
        
        _mockResourceManager = new Mock<IResourceManager>();
        SetupMockResourceManager();
    }
    
    private void SetupMockResourceManager()
    {
        // Mock health resource
        var healthDef = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultCurrent = 100,
            DefaultMax = 100
        };
        
        _mockResourceManager
            .Setup(m => m.GetDefinition("health"))
            .Returns(Result<ResourceDefinition>.Success(healthDef));
        
        // Mock energy resource
        var energyDef = new ResourceDefinition
        {
            ResourceId = "energy",
            DisplayName = "Energy",
            Category = ResourceCategory.TACTICAL,
            DefaultCurrent = 10,
            DefaultMax = 10
        };
        
        _mockResourceManager
            .Setup(m => m.GetDefinition("energy"))
            .Returns(Result<ResourceDefinition>.Success(energyDef));

        var blockDef = new ResourceDefinition
        {
            ResourceId = "block",
            DisplayName = "Block",
            Category = ResourceCategory.TEMPORARY,
            DefaultCurrent = 0,
            DefaultMax = 999
        };
        _mockResourceManager
            .Setup(m => m.GetDefinition("block"))
            .Returns(Result<ResourceDefinition>.Success(blockDef));
    }
    
    [Fact]
    public void CreateEntity_ShouldCreateEntityFromDefinition()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("player_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("player_warrior", result.Value!.DefinitionId);
        Assert.Equal("Warrior", result.Value.DisplayName);
        Assert.Equal(Core.Entity.EntityType.PLAYER, result.Value.Type);
    }
    
    [Fact]
    public void CreateEntity_ShouldAddResourceComponent()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("player_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value!;
        Assert.True(entity.HasComponent<ResourceComponent>());
        
        var resourceComp = entity.GetComponent<ResourceComponent>()!;
        Assert.NotNull(resourceComp.GetResource("health"));
        Assert.Equal(150, resourceComp.GetResource("health")!.Current);
    }
    
    [Fact]
    public void CreateEntity_ShouldAddStatsComponent()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("player_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value!;
        Assert.True(entity.HasComponent<StatsComponent>());
        
        var statsComp = entity.GetComponent<StatsComponent>()!;
        Assert.Equal(18, statsComp.Strength);
        Assert.Equal(12, statsComp.Dexterity);
        Assert.Equal(16, statsComp.Constitution);
    }
    
    [Fact]
    public void CreateEntity_ShouldAddInventoryComponent()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("player_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value!;
        Assert.True(entity.HasComponent<InventoryComponent>());
        
        var inventoryComp = entity.GetComponent<InventoryComponent>()!;
        Assert.Equal(20, inventoryComp.MaxCapacity);
    }
    
    [Fact]
    public void CreateEntity_ShouldCreatePlayerController_ForPlayerType()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("player_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value!;
        Assert.NotNull(entity.Controller);
        Assert.IsType<PlayerController>(entity.Controller);
        Assert.Equal(EntityControllerType.PLAYER_INPUT, entity.Controller.Type);
    }
    
    [Fact]
    public void CreateEntity_ShouldCreateAIController_ForEnemyType()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("enemy_goblin");
        
        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value!;
        Assert.NotNull(entity.Controller);
        Assert.IsType<AIController>(entity.Controller);
        Assert.Equal(EntityControllerType.AI_BEHAVIOR_TREE, entity.Controller.Type);
    }
    
    [Fact]
    public void CreateEntity_ShouldConfigureAIController_FromDefinition()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("enemy_goblin");
        
        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value!;
        var aiController = entity.Controller as AIController;
        Assert.NotNull(aiController);
    }
    
    [Fact]
    public void CreateEntity_ShouldGenerateUniqueId_WhenNotProvided()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result1 = factory.CreateEntity("player_warrior");
        var result2 = factory.CreateEntity("player_warrior");
        
        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.NotEqual(result1.Value!.EntityId, result2.Value!.EntityId);
    }
    
    [Fact]
    public void CreateEntity_ShouldUseProvidedId()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("player_warrior", "custom-id-123");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("custom-id-123", result.Value!.EntityId);
    }
    
    [Fact]
    public void CreateEntity_ShouldFail_WhenDefinitionNotFound()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("nonexistent");
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to load definition", result.Error);
    }

    [Fact]
    public void CreateEntity_ShouldFail_WhenReferencedResourceDefinitionIsMissing()
    {
        var loader = CreateLoader();
        var resources = new Mock<IResourceManager>();
        resources.Setup(manager => manager.GetDefinition(It.IsAny<string>()))
            .Returns((string id) => Result<ResourceDefinition>.Failure($"Resource not found: {id}"));
        var factory = new EntityFactory(loader, resources.Object, new ConsoleLogger("Test"));

        var result = factory.CreateEntity("player_warrior", "player");

        Assert.True(result.IsFailure);
        Assert.Contains("Resource definition not found", result.Error);
    }
    
    [Fact]
    public void CreateEntity_ShouldUseMaterializedPackageDefinition()
    {
        // Arrange
        var loader = CreateLoader();
        var factory = new EntityFactory(loader, _mockResourceManager.Object, new ConsoleLogger("Test"));
        
        // Act
        var result = factory.CreateEntity("enemy_orc_warrior");
        
        // Assert
        Assert.True(result.IsSuccess);
        var entity = result.Value!;
        
        // A definição publicada já contém o estado completo.
        var statsComp = entity.GetComponent<StatsComponent>()!;
        Assert.Equal(16, statsComp.Strength);
        Assert.Equal(14, statsComp.Dexterity);
        
        // Deve ter AI configurado
        Assert.NotNull(entity.Controller);
        Assert.IsType<AIController>(entity.Controller);
    }

    private EntityDefinitionLoader CreateLoader()
    {
        var configManager = new Mock<IConfigManager>();
        var resourceLoader = new Mock<IResourceLoader>();
        configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });

        foreach (var definitionId in new[] { "player_warrior", "enemy_goblin", "enemy_orc_warrior" })
        {
            var json = File.ReadAllText(Path.Combine(_testDataPath, $"{definitionId}.json"));
            resourceLoader
                .Setup(m => m.LoadResource($"entities/{definitionId}.json", It.IsAny<IEnumerable<string>>(), true))
                .Returns(ParseResource(definitionId, json));
        }

        resourceLoader
            .Setup(m => m.LoadResource("entities/nonexistent.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(new Dictionary<string, JsonElement>());

        return new EntityDefinitionLoader(configManager.Object, resourceLoader.Object, new ConsoleLogger("Test"), "test");
    }

    private static Dictionary<string, JsonElement> ParseResource(string definitionId, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            [definitionId] = document.RootElement.GetProperty(definitionId).Clone()
        };
    }
}
