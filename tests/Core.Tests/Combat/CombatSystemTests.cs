using Core.Combat;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Common;
using Core.Config;
using Core.Entity.Definitions;
using Core.Effects;
using Core.Events;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Combat;

public class CombatSystemTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<IResourceManager> _mockResourceManager;
    private readonly Mock<IActionManager> _mockActionManager;
    private readonly Mock<IRuntimeFormulaEvaluator> _formulaEvaluator = new();
    private readonly CombatSystem _combatSystem;

    public CombatSystemTests()
    {
        _mockLogger = new Mock<ILogger>();
        _mockEventBus = new Mock<IEventBus>();
        _mockResourceManager = new Mock<IResourceManager>();
        _mockActionManager = new Mock<IActionManager>();
        
        // Setup ResourceManager to return valid pools
        _mockResourceManager.Setup(rm => rm.CreatePool(It.IsAny<string>(), It.IsAny<float>()))
            .Returns((string resourceId, float current) =>
            {
                var definition = new ResourceDefinition
                {
                    ResourceId = resourceId,
                    DisplayName = resourceId == "health" ? "Health" : "Energy",
                    Category = resourceId == "health" ? ResourceCategory.VITAL : ResourceCategory.TACTICAL,
                    DefaultMin = 0,
                    DefaultMax = resourceId == "health" ? 100 : 10,
                    DefaultCurrent = current,
                    CanBeNegative = false
                };

                return new ResourcePool
                {
                    Definition = definition,
                    Current = current,
                    Maximum = resourceId == "health" ? 100 : 10,
                    Minimum = 0
                };
            });
        
        SetupActionDefinitions(_mockActionManager, CreateBasicAttack(), CreateFireball());

        _combatSystem = new CombatSystem(
            _mockLogger.Object,
            _mockResourceManager.Object,
            new FixedTurnOrderCalculator(_mockLogger.Object),
            _mockEventBus.Object,
            actionManager: _mockActionManager.Object,
            actionCostEvaluator: new ActionCostEvaluator(_formulaEvaluator.Object));
    }

    [Fact]
    public void StartCombat_WithValidInput_ShouldSucceed()
    {
        // Arrange
        var heroId = "hero-1";
        var enemies = new List<string> { "enemy-1", "enemy-2" };
        var initialEnergy = 3;

        // Act
        var result = _combatSystem.StartCombat(heroId, enemies, initialEnergy);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(heroId, result.Value.Hero.EntityId);
        Assert.Equal(2, result.Value.Enemies.Count);
        Assert.Equal(initialEnergy, result.Value.GetHeroResource("energy")?.Current ?? 0);
        Assert.Equal(CombatStatus.ACTIVE, result.Value.Status);
    }

    [Fact]
    public void Combat_WithSameInputs_ReproducesIdsTimeAndState()
    {
        var options = new CombatStartOptions(Seed: 123456UL, ContentRevision: "test-content");

        var firstStart = _combatSystem.StartCombat(
            "hero-1", new List<string> { "enemy-1" }, 3, options);
        Assert.True(firstStart.IsSuccess);
        var firstAction = _combatSystem.ExecuteAction(
            firstStart.Value.CombatId,
            Command("hero-1", ActionType.PASS));
        Assert.True(firstAction.IsSuccess);
        _combatSystem.EndCombat(firstStart.Value.CombatId);

        var secondStart = _combatSystem.StartCombat(
            "hero-1", new List<string> { "enemy-1" }, 3, options);
        Assert.True(secondStart.IsSuccess);
        var secondAction = _combatSystem.ExecuteAction(
            secondStart.Value.CombatId,
            Command("hero-1", ActionType.PASS));

        Assert.True(secondAction.IsSuccess);
        Assert.Equal(firstStart.Value.CombatId, secondStart.Value.CombatId);
        Assert.Equal(firstStart.Value.StartedAt, secondStart.Value.StartedAt);
        Assert.Equal(firstAction.Value.Determinism, secondAction.Value.Determinism);
        Assert.Equal(
            firstAction.Value.ActionHistory.Single(),
            secondAction.Value.ActionHistory.Single());
    }

    [Fact]
    public void ExecuteAction_WithStaleExpectedStep_ShouldFailWithoutChangingState()
    {
        var start = _combatSystem.StartCombat(
            "hero-1",
            new List<string> { "enemy-1" },
            3,
            new CombatStartOptions(Seed: 99UL));
        Assert.True(start.IsSuccess);

        var first = _combatSystem.ExecuteAction(start.Value.CombatId, new CombatActionCommand
        {
            ActorId = "hero-1",
            ActionType = ActionType.PASS,
            ExpectedStep = start.Value.Determinism.Step
        });
        Assert.True(first.IsSuccess);

        var stale = _combatSystem.ExecuteAction(start.Value.CombatId, new CombatActionCommand
        {
            ActorId = "hero-1",
            ActionType = ActionType.PASS,
            ExpectedStep = start.Value.Determinism.Step
        });

        Assert.True(stale.IsFailure);
        Assert.Contains("Stale combat command", stale.Error);
        Assert.Single(_combatSystem.GetCombatState(start.Value.CombatId).Value.ActionHistory);
    }

    [Fact]
    public void StartCombat_WithEntityDefinitions_ShouldUseJsonResourcesAndNames()
    {
        // Arrange
        var workspaceRoot = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
        var entityPath = Path.Combine(workspaceRoot, "data", "configs", "default", "Entities");
        var configManager = new Mock<IConfigManager>();
        var resourceLoader = new Mock<IResourceLoader>();
        configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        foreach (var definitionId in new[] { "player_warrior", "enemy_goblin", "enemy_orc_warrior" })
        {
            var json = File.ReadAllText(Path.Combine(entityPath, $"{definitionId}.json"));
            resourceLoader
                .Setup(m => m.LoadResource($"Entities/{definitionId}.json", It.IsAny<IEnumerable<string>>(), false))
                .Returns(ParseEntityResource(definitionId, json));
        }

        var loader = new EntityDefinitionLoader(configManager.Object, resourceLoader.Object, _mockLogger.Object, "test");
        var combatSystem = new CombatSystem(
            _mockLogger.Object,
            _mockResourceManager.Object,
            new FixedTurnOrderCalculator(_mockLogger.Object),
            _mockEventBus.Object,
            actionManager: _mockActionManager.Object,
            entityDefinitionLoader: loader);

        // Act
        var result = combatSystem.StartCombat("player_warrior", new List<string> { "enemy_orc_warrior" }, 4);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Warrior", result.Value.Hero.Name);
        Assert.Equal(150, result.Value.Hero.GetResource("health")?.Maximum);
        Assert.Equal(4, result.Value.Hero.GetResource("energy")?.Current);
        Assert.Equal("Orc Warrior", result.Value.Enemies[0].Name);
        Assert.Equal(100, result.Value.Enemies[0].GetResource("health")?.Current);
        Assert.Equal(100, result.Value.Enemies[0].GetResource("health")?.Maximum);
    }

    private static Dictionary<string, JsonElement> ParseEntityResource(string definitionId, string json)
    {
        using var document = JsonDocument.Parse($$"""
        {
          "{{definitionId}}": {{json}}
        }
        """);

        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartCombat_WithEmptyHeroId_ShouldFail()
    {
        // Arrange
        var heroId = "";
        var enemies = new List<string> { "enemy-1" };

        // Act
        var result = _combatSystem.StartCombat(heroId, enemies);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Hero ID cannot be empty", result.Error);
    }

    [Fact]
    public void StartCombat_WithNoEnemies_ShouldFail()
    {
        // Arrange
        var heroId = "hero-1";
        var enemies = new List<string>();

        // Act
        var result = _combatSystem.StartCombat(heroId, enemies);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("At least one enemy is required", result.Error);
    }

    [Fact]
    public void ExecuteAction_BasicAttack_ShouldDealDamageAndGainEnergy()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 0);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.BASIC_ATTACK, targetId: targetId));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.GetHeroResource("energy")?.Current ?? 0); // Gained 1 energy
        Assert.Equal(40, result.Value.Enemies[0].GetResource("health")?.Current); // 50 - 10 = 40
        Assert.Single(result.Value.ActionHistory);
    }

    [Fact]
    public void ExecuteAction_Power_ShouldConsumeEnergyAndDealDamage()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.POWER, powerId: "FIREBALL", targetId: targetId));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.GetHeroResource("energy")?.Current ?? -1); // 3 - 3 = 0
        Assert.Equal(20, result.Value.Enemies[0].GetResource("health")?.Current); // 50 - 30 = 20
        Assert.Single(result.Value.ActionHistory);
    }

    [Fact]
    public void ExecuteAction_PowerWithoutEnergy_ShouldFail()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 0);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.POWER, powerId: "FIREBALL", targetId: targetId));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Insufficient", result.Error);
    }

    [Fact]
    public void ExecuteAction_PowerWithConfiguredAction_ShouldUseConfiguredDamageAndCosts()
    {
        // Arrange
        var actionManager = new Mock<IActionManager>();
        SetupActionDefinitions(actionManager, CreatePower("ice_bolt", 2, 12));
        var combatSystem = new CombatSystem(
            _mockLogger.Object,
            _mockResourceManager.Object,
            new FixedTurnOrderCalculator(_mockLogger.Object),
            _mockEventBus.Object,
            actionManager: actionManager.Object);
        var startResult = combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act
        var result = combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.POWER, powerId: "ice_bolt", targetId: targetId));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.GetHeroResource("energy")?.Current ?? -1);
        Assert.Equal(38, result.Value.Enemies[0].GetResource("health")?.Current);
        Assert.Equal(-2, result.Value.ActionHistory.Single().EnergyChange);
    }

    [Fact]
    public void ExecuteAction_PowerWithFormulaCost_ShouldSpendCalculatedAmount()
    {
        var actionManager = new Mock<IActionManager>();
        SetupActionDefinitions(actionManager, new ActionDefinition
        {
            ActionId = "dynamic_blast",
            ActionType = ActionType.POWER,
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new() { ResourceId = "energy", Amount = 99, Formula = "energy_current - 1" }
                }
            },
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = 12, Target = EffectTarget.TARGET }
            }
        });
        _formulaEvaluator
            .Setup(m => m.Evaluate("energy_current - 1", It.IsAny<Dictionary<string, float>>(), 0f))
            .Returns(Result<float>.Success(2f));
        var combatSystem = new CombatSystem(
            _mockLogger.Object,
            _mockResourceManager.Object,
            new FixedTurnOrderCalculator(_mockLogger.Object),
            _mockEventBus.Object,
            actionManager: actionManager.Object,
            actionCostEvaluator: new ActionCostEvaluator(_formulaEvaluator.Object));
        var startResult = combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        var result = combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.POWER, powerId: "dynamic_blast", targetId: targetId));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(1, result.Value.GetHeroResource("energy")?.Current ?? -1);
        _formulaEvaluator.Verify(m => m.Evaluate(
            "energy_current - 1",
            It.Is<Dictionary<string, float>>(vars => vars["energy_current"] == 3f),
            0f), Times.AtLeastOnce);
    }

    [Fact]
    public void ExecuteAction_PowerWithAlternativeCosts_ShouldRequireCostOption()
    {
        // Arrange
        var actionManager = new Mock<IActionManager>();
        SetupActionDefinitions(actionManager, new ActionDefinition
        {
            ActionId = "blood_cast",
            Costs = new ActionCosts
            {
                AlternativeCosts = new List<AlternativeCostOption>
                {
                    new()
                    {
                        OptionId = "energy",
                        Description = "Pay energy",
                        Costs = new List<ResourceCost> { new() { ResourceId = "energy", Amount = 1 } }
                    }
                }
            },
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = 12 }
            }
        });
        var combatSystem = new CombatSystem(
            _mockLogger.Object,
            _mockResourceManager.Object,
            new FixedTurnOrderCalculator(_mockLogger.Object),
            _mockEventBus.Object,
            actionManager: actionManager.Object);
        var startResult = combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act
        var result = combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.POWER, powerId: "blood_cast", targetId: targetId));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Cost option must be specified", result.Error);
    }

    [Fact]
    public void ExecuteAction_KillAllEnemies_ShouldSetStatusToVictory()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var targetId = startResult.Value.Enemies[0].EntityId;

        // Act - Kill enemy (50 HP: 1 power = 30 dmg, 2 basic = 20 dmg)
        _combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.POWER, powerId: "FIREBALL", targetId: targetId)); // 50 - 30 = 20 HP
        _combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.BASIC_ATTACK, targetId: targetId)); // 20 - 10 = 10 HP, +1 energy
        var result = _combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.BASIC_ATTACK, targetId: targetId)); // 10 - 10 = 0 HP (dead)

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CombatStatus.VICTORY, result.Value.Status);
        Assert.False(result.Value.Enemies[0].IsAlive);
    }

    [Fact]
    public void ExecuteAction_Pass_ShouldNotChangeState()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 3);
        var combatId = startResult.Value.CombatId;
        var initialEnergy = startResult.Value.GetHeroResource("energy")?.Current ?? 0;
        var initialEnemyHp = startResult.Value.Enemies[0].GetResource("health")?.Current;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.PASS));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(initialEnergy, result.Value.GetHeroResource("energy")?.Current ?? 0);
        Assert.Equal(initialEnemyHp, result.Value.Enemies[0].GetResource("health")?.Current);
        Assert.Single(result.Value.ActionHistory);
    }

    [Fact]
    public void ExecuteAction_EndTurn_ShouldIncrementTurn()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" });
        var combatId = startResult.Value.CombatId;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, Command("hero-1", ActionType.END_TURN));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.CurrentTurn);
        Assert.Single(result.Value.ActionHistory);
    }

    [Fact]
    public void ExecuteAction_EnemyBasicAttack_ShouldDamageHeroAndRecordEnemyActor()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 0);
        var combatId = startResult.Value.CombatId;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, Command("enemy-1", ActionType.BASIC_ATTACK, targetId: "hero-1"));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(90, result.Value.Hero.GetResource("health")?.Current);
        Assert.Equal("enemy-1", result.Value.ActionHistory.Single().ActorId);
        Assert.Equal("hero-1", result.Value.ActionHistory.Single().TargetId);
    }

    [Fact]
    public void ExecuteAction_EnemyPass_ShouldRecordEnemyActor()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" }, 0);
        var combatId = startResult.Value.CombatId;

        // Act
        var result = _combatSystem.ExecuteAction(combatId, Command("enemy-1", ActionType.PASS));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("enemy-1", result.Value.ActionHistory.Single().ActorId);
    }

    [Fact]
    public void GetCombatState_WithValidId_ShouldReturnState()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" });
        var combatId = startResult.Value.CombatId;

        // Act
        var result = _combatSystem.GetCombatState(combatId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(combatId, result.Value.CombatId);
    }

    [Fact]
    public void EndCombat_WithValidId_ShouldReturnResult()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" });
        var combatId = startResult.Value.CombatId;

        // Act
        var result = _combatSystem.EndCombat(combatId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(combatId, result.Value.CombatId);
        Assert.Equal(CombatStatus.ACTIVE, result.Value.Status);
    }

    [Fact]
    public void CombatExists_WithValidId_ShouldReturnTrue()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero-1", new List<string> { "enemy-1" });
        var combatId = startResult.Value.CombatId;

        // Act
        var exists = _combatSystem.CombatExists(combatId);

        // Assert
        Assert.True(exists);
    }

    private static void SetupActionDefinitions(Mock<IActionManager> actionManager, params ActionDefinition[] definitions)
    {
        foreach (var definition in definitions)
        {
            actionManager.Setup(m => m.GetDefinition(definition.ActionId))
                .Returns(Core.Common.Result<ActionDefinition>.Success(definition));
        }

        actionManager.Setup(m => m.GetDefinition(It.Is<string>(id => definitions.All(d => d.ActionId != id))))
            .Returns((string id) => Core.Common.Result<ActionDefinition>.Failure($"Action definition not found: {id}"));
    }

    private static CombatActionCommand Command(
        string actorId,
        ActionType actionType,
        string? powerId = null,
        string? targetId = null,
        string? costOptionId = null)
    {
        return new CombatActionCommand
        {
            ActorId = actorId,
            ActionType = actionType,
            PowerId = powerId,
            TargetId = targetId,
            CostOptionId = costOptionId
        };
    }

    private static ActionDefinition CreateBasicAttack()
    {
        return new ActionDefinition
        {
            ActionId = "basic_attack",
            ActionType = ActionType.BASIC_ATTACK,
            Tags = new List<string> { "physical", "melee", "can_crit" },
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = 10, Target = EffectTarget.TARGET },
                new() { Type = EffectType.MODIFY_RESOURCE, FlatValue = 1, TargetResource = "energy", Target = EffectTarget.SELF }
            }
        };
    }

    private static ActionDefinition CreateFireball()
    {
        return CreatePower("FIREBALL", 3, 30);
    }

    private static ActionDefinition CreatePower(string actionId, float energyCost, float damage)
    {
        return new ActionDefinition
        {
            ActionId = actionId,
            ActionType = ActionType.POWER,
            Costs = new ActionCosts
            {
                Costs = new List<ResourceCost>
                {
                    new() { ResourceId = "energy", Amount = energyCost }
                }
            },
            Effects = new List<EffectDefinition>
            {
                new() { Type = EffectType.DAMAGE, FlatValue = damage, Target = EffectTarget.TARGET }
            }
        };
    }
}
