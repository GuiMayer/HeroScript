using Core.Combat.Models;
using Core.Config;
using Core.Logging;
using Core.Resources;
using Moq;

namespace Core.Tests.Combat.TurnOrder;

/// <summary>
/// Helper class para criar objetos de teste
/// </summary>
public static class TurnOrderTestHelper
{
    public static ILogger CreateTestLogger()
    {
        return new ConsoleLogger("TurnOrderTests", enableDebug: false);
    }
    
    public static IResourceManager CreateTestResourceManager()
    {
        var mockResourceManager = new Mock<IResourceManager>();
        
        // Configure CreatePool to return valid ResourcePool objects
        // Note: initialCurrent is used as the Maximum, and Current will be set separately
        mockResourceManager
            .Setup(rm => rm.CreatePool(It.IsAny<string>(), It.IsAny<float?>()))
            .Returns((string resourceId, float? initialCurrent) =>
            {
                var maximum = initialCurrent ?? 100f;
                return new ResourcePool
                {
                    ResourceId = resourceId,
                    Current = maximum,  // Start at maximum, can be modified with 'with' expression
                    Maximum = maximum,
                    Minimum = 0f,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = resourceId,
                        DisplayName = resourceId,
                        ShortName = resourceId,
                        Category = ResourceCategory.VITAL,
                        DefaultMin = 0f,
                        DefaultMax = maximum,
                        DefaultCurrent = maximum
                    }
                };
            });
        
        return mockResourceManager.Object;
    }
    
    public static CombatState CreateTestCombatState(string heroId, string[] enemyIds)
    {
        var resourceManager = CreateTestResourceManager();
        
        var heroHealthPool = resourceManager.CreatePool("health", 100);
        var heroResources = new Dictionary<string, ResourcePool>
        {
            ["health"] = heroHealthPool
        };
        
        var hero = new CombatEntity
        {
            EntityId = heroId,
            Name = "Hero",
            IsHero = true,
            ResourceState = new ResourceSet
            {
                OwnerId = heroId,
                Resources = heroResources
            }
        };
        
        var enemies = enemyIds.Select(id =>
        {
            var enemyHealthPool = resourceManager.CreatePool("health", 50);
            var enemyResources = new Dictionary<string, ResourcePool>
            {
                ["health"] = enemyHealthPool
            };
            
            return new CombatEntity
            {
                EntityId = id,
                Name = $"Enemy-{id}",
                IsHero = false,
                ResourceState = new ResourceSet
                {
                    OwnerId = id,
                    Resources = enemyResources
                }
            };
        }).ToList();
        
        return new CombatState
        {
            Hero = hero,
            Enemies = enemies,
            CurrentTurn = 1,
            Status = CombatStatus.ACTIVE
        };
    }
    
    public static CombatState CreateTestCombatStateWithSpeed(
        (string id, float speed) heroData,
        (string id, float speed)[] enemyData)
    {
        var resourceManager = CreateTestResourceManager();
        
        var heroHealthPool = resourceManager.CreatePool("health", 100);
        var heroSpeedPool = resourceManager.CreatePool("speed", heroData.speed);
        var heroResources = new Dictionary<string, ResourcePool>
        {
            ["health"] = heroHealthPool,
            ["speed"] = heroSpeedPool
        };
        
        var hero = new CombatEntity
        {
            EntityId = heroData.id,
            Name = "Hero",
            IsHero = true,
            ResourceState = new ResourceSet
            {
                OwnerId = heroData.id,
                Resources = heroResources
            }
        };
        
        var enemies = enemyData.Select(data =>
        {
            var enemyHealthPool = resourceManager.CreatePool("health", 50);
            var enemySpeedPool = resourceManager.CreatePool("speed", data.speed);
            var enemyResources = new Dictionary<string, ResourcePool>
            {
                ["health"] = enemyHealthPool,
                ["speed"] = enemySpeedPool
            };
            
            return new CombatEntity
            {
                EntityId = data.id,
                Name = $"Enemy-{data.id}",
                IsHero = false,
                ResourceState = new ResourceSet
                {
                    OwnerId = data.id,
                    Resources = enemyResources
                }
            };
        }).ToList();
        
        return new CombatState
        {
            Hero = hero,
            Enemies = enemies,
            CurrentTurn = 1,
            Status = CombatStatus.ACTIVE
        };
    }
    
    public static CombatState CreateTestCombatStateWithHealth(
        (string id, float current, float max) heroData,
        (string id, float current, float max)[] enemyData)
    {
        var resourceManager = CreateTestResourceManager();
        
        var heroHealthPool = resourceManager.CreatePool("health", heroData.max);
        heroHealthPool = heroHealthPool with { Current = heroData.current };
        var heroResources = new Dictionary<string, ResourcePool>
        {
            ["health"] = heroHealthPool
        };
        
        var hero = new CombatEntity
        {
            EntityId = heroData.id,
            Name = "Hero",
            IsHero = true,
            ResourceState = new ResourceSet
            {
                OwnerId = heroData.id,
                Resources = heroResources
            }
        };
        
        var enemies = enemyData.Select(data =>
        {
            var enemyHealthPool = resourceManager.CreatePool("health", data.max);
            enemyHealthPool = enemyHealthPool with { Current = data.current };
            var enemyResources = new Dictionary<string, ResourcePool>
            {
                ["health"] = enemyHealthPool
            };
            
            return new CombatEntity
            {
                EntityId = data.id,
                Name = $"Enemy-{data.id}",
                IsHero = false,
                ResourceState = new ResourceSet
                {
                    OwnerId = data.id,
                    Resources = enemyResources
                }
            };
        }).ToList();
        
        return new CombatState
        {
            Hero = hero,
            Enemies = enemies,
            CurrentTurn = 1,
            Status = CombatStatus.ACTIVE
        };
    }
    
    public static CombatState CreateTestCombatStateWithHealthAndSpeed(
        (string id, float currentHealth, float maxHealth, float speed) heroData,
        (string id, float currentHealth, float maxHealth, float speed)[] enemyData)
    {
        var resourceManager = CreateTestResourceManager();
        
        var heroHealthPool = resourceManager.CreatePool("health", heroData.maxHealth);
        heroHealthPool = heroHealthPool with { Current = heroData.currentHealth };
        var heroSpeedPool = resourceManager.CreatePool("speed", heroData.speed);
        var heroResources = new Dictionary<string, ResourcePool>
        {
            ["health"] = heroHealthPool,
            ["speed"] = heroSpeedPool
        };
        
        var hero = new CombatEntity
        {
            EntityId = heroData.id,
            Name = "Hero",
            IsHero = true,
            ResourceState = new ResourceSet
            {
                OwnerId = heroData.id,
                Resources = heroResources
            }
        };
        
        var enemies = enemyData.Select(data =>
        {
            var enemyHealthPool = resourceManager.CreatePool("health", data.maxHealth);
            enemyHealthPool = enemyHealthPool with { Current = data.currentHealth };
            var enemySpeedPool = resourceManager.CreatePool("speed", data.speed);
            var enemyResources = new Dictionary<string, ResourcePool>
            {
                ["health"] = enemyHealthPool,
                ["speed"] = enemySpeedPool
            };
            
            return new CombatEntity
            {
                EntityId = data.id,
                Name = $"Enemy-{data.id}",
                IsHero = false,
                ResourceState = new ResourceSet
                {
                    OwnerId = data.id,
                    Resources = enemyResources
                }
            };
        }).ToList();
        
        return new CombatState
        {
            Hero = hero,
            Enemies = enemies,
            CurrentTurn = 1,
            Status = CombatStatus.ACTIVE
        };
    }
}
