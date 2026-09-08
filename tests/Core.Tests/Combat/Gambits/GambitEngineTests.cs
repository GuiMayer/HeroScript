using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Config;
using Core.Resources;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Combat.Gambits;

public sealed class GambitEngineTests
{
    private readonly GambitEngine _engine;

    public GambitEngineTests()
    {
        var configManager = new Mock<IConfigManager>();
        var resourceLoader = new Mock<IResourceLoader>();
        configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        resourceLoader
            .Setup(m => m.LoadResource("gambits/gambits.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(ParseResource(TestDefinitionsJson));

        _engine = new GambitEngine(configManager.Object, resourceLoader.Object);
        var load = _engine.LoadDefinitions("test");
        Assert.True(load.IsSuccess, load.IsFailure ? load.Error : null);
    }

    [Fact]
    public void DecideAction_UsesHighestPriorityMatchingGambit()
    {
        var state = CreateState(heroHealth: 20, enemyHealth: 50);
        var entity = state.GetActor("hero")!;

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
        var entity = state.GetActor("hero")!;

        var action = _engine.DecideAction(entity, state);

        Assert.True(action.IsSuccess, action.IsFailure ? action.Error : null);
        Assert.Equal(ActionType.BASIC_ATTACK, action.Value.ActionType);
        Assert.Equal("enemy-1", action.Value.TargetId);
    }

    [Fact]
    public void DecideAction_ActorConditionAndOpponentTarget_WorkForEnemyActors()
    {
        var state = CreateState(heroHealth: 80, enemyHealth: 20);
        var entity = state.GetActor("enemy-1")!;

        var action = _engine.DecideAction(entity, state, new[] { "enemy_actor_attack" });

        Assert.True(action.IsSuccess, action.IsFailure ? action.Error : null);
        Assert.Equal(ActionType.BASIC_ATTACK, action.Value.ActionType);
        Assert.Equal("hero", action.Value.TargetId);
    }

    [Fact]
    public void DecideActionWithMetadata_ReturnsSelectedGambitIntentMetadata()
    {
        var state = CreateState(heroHealth: 80, enemyHealth: 50);
        var entity = state.GetActor("enemy-1")!;

        var decision = _engine.DecideActionWithMetadata(entity, state, new[] { "enemy_actor_attack" });

        Assert.True(decision.IsSuccess, decision.IsFailure ? decision.Error : null);
        Assert.Equal("enemy_actor_attack", decision.Value.GambitId);
        Assert.Equal(20, decision.Value.Priority);
        Assert.Equal(ActionType.BASIC_ATTACK, decision.Value.Action.ActionType);
        Assert.Equal("Enemy prepares a strike", decision.Value.Intent.DisplayName);
        Assert.Equal("Attack", decision.Value.Intent.TelegraphType);
        Assert.Contains("attack", decision.Value.Intent.Tags);
    }

    private static CombatState CreateState(float heroHealth, float enemyHealth)
    {
        var actors = new[]
        {
            CreateEntity("hero", "Hero", isHero: true, heroHealth),
            CreateEntity("enemy-1", "Enemy", isHero: false, enemyHealth)
        };
        return new CombatState { Actors = actors.ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal) };
    }

    private static CombatActorState CreateEntity(string entityId, string name, bool isHero, float health)
    {
        return new CombatActorState
        {
            InstanceId = entityId,
            Name = name,
            SideId = isHero ? "player" : "opposition", ControllerBinding = new ControllerBinding { Kind = isHero ? ControllerKind.Player : ControllerKind.AI },
            ResourceState = new ResourceSet
            {
                OwnerId = entityId,
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

    private static Dictionary<string, JsonElement> ParseResource(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private const string TestDefinitionsJson = """
    {
      "heal_low_hp": {
        "gambitId": "heal_low_hp",
        "displayName": "Heal Low HP",
        "priority": 100,
        "conditions": [
          {
            "type": "ACTOR_RESOURCE_PERCENT",
            "resourceId": "health",
            "lessThanOrEqual": 0.30
          }
        ],
        "action": {
          "actionType": "POWER",
          "powerId": "heal",
          "target": "SELF"
        }
      },
      "attack_first": {
        "gambitId": "attack_first",
        "displayName": "Attack First Alive Enemy",
        "priority": 10,
        "conditions": [
          { "type": "ANY_OPPONENT_ALIVE" }
        ],
        "action": {
          "actionType": "BASIC_ATTACK",
          "target": "FIRST_ALIVE_OPPONENT"
        }
      },
      "enemy_actor_attack": {
        "gambitId": "enemy_actor_attack",
        "displayName": "Enemy Actor Attack",
        "priority": 20,
        "intent": {
          "displayName": "Enemy prepares a strike",
          "description": "The enemy is preparing to attack the hero.",
          "telegraphType": "Attack",
          "tags": ["attack", "physical"]
        },
        "conditions": [
          {
            "type": "ACTOR_RESOURCE_PERCENT",
            "resourceId": "health",
            "greaterThanOrEqual": 0.10
          }
        ],
        "action": {
          "actionType": "BASIC_ATTACK",
          "target": "FIRST_ALIVE_OPPONENT"
        }
      }
    }
    """;
}
