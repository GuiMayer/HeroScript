using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Config;
using Core.Entity;
using Core.Entity.Controllers;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Gambits;

public sealed class GambitEngineTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GambitEngine _engine;

    public GambitEngineTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"heroscript-gambit-tests-{Guid.NewGuid():N}");
        var gambitDirectory = Path.Combine(_tempRoot, "Gambits");
        Directory.CreateDirectory(gambitDirectory);
        File.WriteAllText(Path.Combine(gambitDirectory, "gambits.json"), TestDefinitionsJson);

        var configManager = new Mock<IConfigManager>();
        configManager.Setup(m => m.GetConfigPath("test")).Returns(_tempRoot);

        _engine = new GambitEngine(configManager.Object);
        var load = _engine.LoadDefinitions("test");
        Assert.True(load.IsSuccess, load.IsFailure ? load.Error : null);
    }

    [Fact]
    public void DecideAction_UsesHighestPriorityMatchingGambit()
    {
        var state = CreateState(heroHealth: 20, enemyHealth: 50);
        var entity = new Core.Entity.Entity { EntityId = "hero" };

        var action = _engine.DecideAction(entity, state);

        Assert.True(action.IsSuccess, action.IsFailure ? action.Error : null);
        Assert.Equal(ActionType.POWER, action.Value.ActionType);
        Assert.Equal("heal", action.Value.PowerId);
        Assert.Equal("hero", action.Value.TargetId);
    }

    [Fact]
    public void DecideAction_FallsBackToAttackWhenHealConditionDoesNotMatch()
    {
        var state = CreateState(heroHealth: 80, enemyHealth: 50);
        var entity = new Core.Entity.Entity { EntityId = "hero" };

        var action = _engine.DecideAction(entity, state);

        Assert.True(action.IsSuccess, action.IsFailure ? action.Error : null);
        Assert.Equal(ActionType.BASIC_ATTACK, action.Value.ActionType);
        Assert.Equal("enemy-1", action.Value.TargetId);
    }

    [Fact]
    public async Task GambitController_DelegatesDecisionToEngine()
    {
        var state = CreateState(heroHealth: 80, enemyHealth: 50);
        var entity = new Core.Entity.Entity { EntityId = "hero" };
        var controller = new GambitController(_engine, gambitIds: new[] { "attack_first" });

        var action = await controller.DecideAction(entity, state);

        Assert.True(action.IsSuccess, action.IsFailure ? action.Error : null);
        Assert.Equal(ActionType.BASIC_ATTACK, action.Value.ActionType);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static CombatState CreateState(float heroHealth, float enemyHealth)
    {
        return new CombatState
        {
            Hero = CreateEntity("hero", "Hero", isHero: true, heroHealth),
            Enemies = new[] { CreateEntity("enemy-1", "Enemy", isHero: false, enemyHealth) }
        };
    }

    private static CombatEntity CreateEntity(string entityId, string name, bool isHero, float health)
    {
        return new CombatEntity
        {
            EntityId = entityId,
            Name = name,
            IsHero = isHero,
            ResourceState = new EntityResourceState
            {
                EntityId = entityId,
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = new ResourcePool
                    {
                        ResourceId = "health",
                        Current = health,
                        Maximum = 100,
                        Minimum = 0,
                        Definition = new ResourceDefinition
                        {
                            ResourceId = "health",
                            Category = ResourceCategory.VITAL,
                            DefaultMax = 100,
                            DefaultMin = 0
                        }
                    }
                }
            }
        };
    }

    private const string TestDefinitionsJson = """
    {
      "heal_low_hp": {
        "gambitId": "heal_low_hp",
        "displayName": "Heal Low HP",
        "priority": 100,
        "conditions": [
          {
            "type": "HERO_RESOURCE_PERCENT",
            "resourceId": "health",
            "lessThanOrEqual": 0.30
          }
        ],
        "action": {
          "actionType": "POWER",
          "powerId": "heal",
          "target": "HERO"
        }
      },
      "attack_first": {
        "gambitId": "attack_first",
        "displayName": "Attack First Alive Enemy",
        "priority": 10,
        "conditions": [
          { "type": "ANY_ENEMY_ALIVE" }
        ],
        "action": {
          "actionType": "BASIC_ATTACK",
          "target": "FIRST_ALIVE_ENEMY"
        }
      }
    }
    """;
}
