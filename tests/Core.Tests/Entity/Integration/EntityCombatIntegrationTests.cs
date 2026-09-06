using Core.Combat;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Common;
using Core.Config;
using Core.Entity;
using Core.Entity.Components;
using Core.Entity.Definitions;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Entity.Integration;

/// <summary>
/// Testes de integração entre o sistema Entity e a fábrica de combate.
/// Valida o fluxo completo de criação de entidades e início de combate.
/// </summary>
public class EntityCombatIntegrationTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly IResourceManager _resourceManager;
    private readonly ICombatFactory _combatFactory;
    
    public EntityCombatIntegrationTests()
    {
        _mockLogger = new Mock<ILogger>();
        var mockConfigManager = new Mock<IConfigManager>();
        var mockResourceLoader = new Mock<IResourceLoader>();
        _resourceManager = new ResourceManager(
            mockConfigManager.Object,
            mockResourceLoader.Object,
            _mockLogger.Object
        );
        
        _combatFactory = new CombatFactory(
            _resourceManager,
            new FixedTurnOrderCalculator(_mockLogger.Object));
    }
    
    [Fact]
    public void StartCombatWithEntities_ShouldCreateCombatFromEntityDefinitions()
    {
        // Arrange - Criar definição de herói
        var heroDefinition = new EntityDefinition
        {
            DefinitionId = "warrior",
            Type = Core.Entity.EntityType.PLAYER,
            DisplayName = "Brave Warrior",
            Resources = new ResourcesDefinition
            {
                Resources = new Dictionary<string, ResourcePoolDefinition>
                {
                    ["health"] = new ResourcePoolDefinition { Current = 100, Max = 100 },
                    ["energy"] = new ResourcePoolDefinition { Current = 3, Max = 10 }
                }
            }
        };
        
        // Criar definição de inimigo
        var enemyDefinition = new EntityDefinition
        {
            DefinitionId = "goblin",
            Type = Core.Entity.EntityType.ENEMY,
            DisplayName = "Goblin Warrior",
            Resources = new ResourcesDefinition
            {
                Resources = new Dictionary<string, ResourcePoolDefinition>
                {
                    ["health"] = new ResourcePoolDefinition { Current = 50, Max = 50 }
                }
            }
        };
        
        // Criar entidades a partir das definições
        var hero = CreateEntityFromDefinition("hero-1", heroDefinition);
        var enemy = CreateEntityFromDefinition("enemy-1", enemyDefinition);
        
        // Act - Iniciar combate usando entidades
        var result = _combatFactory.Create(
            hero,
            new List<Core.Entity.Entity> { enemy },
            new CombatStartOptions(Seed: 42));
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        
        var combatState = result.Value;
        Assert.Equal(CombatStatus.ACTIVE, combatState.Status);
        Assert.Equal("hero-1", combatState.Hero.EntityId);
        Assert.Equal("Brave Warrior", combatState.Hero.Name);
        Assert.Single(combatState.Enemies);
        Assert.Equal("enemy-1", combatState.Enemies[0].EntityId);
        Assert.Equal("Goblin Warrior", combatState.Enemies[0].Name);
        
        // Verificar recursos
        var heroHealth = combatState.Hero.GetResource("health");
        Assert.NotNull(heroHealth);
        Assert.Equal(100, heroHealth.Current);
        
        var heroEnergy = combatState.Hero.GetResource("energy");
        Assert.NotNull(heroEnergy);
        Assert.Equal(3, heroEnergy.Current);
        
        var enemyHealth = combatState.Enemies[0].GetResource("health");
        Assert.NotNull(enemyHealth);
        Assert.Equal(50, enemyHealth.Current);
    }
    
    [Fact]
    public void StartCombatWithEntities_WithMultipleEnemies_ShouldCreateCombatCorrectly()
    {
        // Arrange
        var heroDefinition = new EntityDefinition
        {
            DefinitionId = "mage",
            Type = Core.Entity.EntityType.PLAYER,
            DisplayName = "Fire Mage",
            Resources = new ResourcesDefinition
            {
                Resources = new Dictionary<string, ResourcePoolDefinition>
                {
                    ["health"] = new ResourcePoolDefinition { Current = 80, Max = 80 },
                    ["energy"] = new ResourcePoolDefinition { Current = 5, Max = 10 }
                }
            }
        };
        
        var enemyDefinition = new EntityDefinition
        {
            DefinitionId = "orc",
            Type = Core.Entity.EntityType.ENEMY,
            DisplayName = "Orc Brute",
            Resources = new ResourcesDefinition
            {
                Resources = new Dictionary<string, ResourcePoolDefinition>
                {
                    ["health"] = new ResourcePoolDefinition { Current = 60, Max = 60 }
                }
            }
        };
        
        var hero = CreateEntityFromDefinition("hero-1", heroDefinition);
        var enemy1 = CreateEntityFromDefinition("enemy-1", enemyDefinition);
        var enemy2 = CreateEntityFromDefinition("enemy-2", enemyDefinition);
        var enemy3 = CreateEntityFromDefinition("enemy-3", enemyDefinition);
        
        // Act
        var result = _combatFactory.Create(
            hero, 
            new List<Core.Entity.Entity> { enemy1, enemy2, enemy3 },
            new CombatStartOptions(Seed: 42)
        );
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Enemies.Count);
        Assert.All(result.Value.Enemies, enemy => 
        {
            Assert.Equal("Orc Brute", enemy.Name);
            Assert.Equal(60, enemy.GetResource("health")?.Current);
        });
    }
    
    [Fact]
    public void StartCombatWithEntities_WithNullHero_ShouldReturnFailure()
    {
        // Arrange
        var enemy = CreateEntityFromDefinition("enemy-1", new EntityDefinition
        {
            DefinitionId = "goblin",
            Type = Core.Entity.EntityType.ENEMY,
            DisplayName = "Goblin",
            Resources = new ResourcesDefinition
            {
                Resources = new Dictionary<string, ResourcePoolDefinition>
                {
                    ["health"] = new ResourcePoolDefinition { Current = 50, Max = 50 }
                }
            }
        });
        
        // Act
        var result = _combatFactory.Create(
            null!,
            new List<Core.Entity.Entity> { enemy },
            new CombatStartOptions(Seed: 42));
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("cannot be null", result.Error);
    }
    
    [Fact]
    public void StartCombatWithEntities_WithEmptyEnemyList_ShouldReturnFailure()
    {
        // Arrange
        var hero = CreateEntityFromDefinition("hero-1", new EntityDefinition
        {
            DefinitionId = "warrior",
            Type = Core.Entity.EntityType.PLAYER,
            DisplayName = "Warrior",
            Resources = new ResourcesDefinition
            {
                Resources = new Dictionary<string, ResourcePoolDefinition>
                {
                    ["health"] = new ResourcePoolDefinition { Current = 100, Max = 100 },
                    ["energy"] = new ResourcePoolDefinition { Current = 3, Max = 10 }
                }
            }
        });
        
        // Act
        var result = _combatFactory.Create(
            hero,
            new List<Core.Entity.Entity>(),
            new CombatStartOptions(Seed: 42));
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("At least one enemy is required", result.Error);
    }
    
    /// <summary>
    /// Helper para criar entidade a partir de definição
    /// </summary>
    private Core.Entity.Entity CreateEntityFromDefinition(string entityId, EntityDefinition definition)
    {
        var resources = new Dictionary<string, ResourcePool>();
        
        if (definition.Resources?.Resources != null)
        {
            foreach (var (resourceId, poolDef) in definition.Resources.Resources)
            {
                // Criar definição mínima para o recurso
                var resourceDef = new ResourceDefinition
                {
                    ResourceId = resourceId,
                    DisplayName = resourceId,
                    DefaultMax = poolDef.Max,
                    DefaultCurrent = poolDef.Current,
                    CanExceedMax = false,
                    CanBeNegative = false
                };
                
                // Criar pool diretamente
                var pool = new ResourcePool
                {
                    ResourceId = resourceId,
                    Current = poolDef.Current,
                    Maximum = poolDef.Max,
                    Minimum = 0,
                    Definition = resourceDef
                };
                resources[resourceId] = pool;
            }
        }
        
        var resourceState = new ResourceSet
        {
            OwnerId = entityId,
            Resources = resources
        };
        
        var resourceComponent = new ResourceComponent(resourceState);
        
        var components = new Dictionary<Type, IComponent>
        {
            [typeof(ResourceComponent)] = resourceComponent
        };
        
        return new Core.Entity.Entity
        {
            EntityId = entityId,
            Type = definition.Type,
            DefinitionId = definition.DefinitionId,
            DisplayName = definition.DisplayName,
            Components = components
        };
    }
}
