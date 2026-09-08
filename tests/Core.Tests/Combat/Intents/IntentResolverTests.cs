using Core.Combat;
using Core.Combat.Gambits;
using Core.Combat.Intents;
using Core.Combat.Models;
using Core.Effects;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Intents;

public sealed class IntentResolverTests
{
    private readonly Mock<IGambitEngine> _gambitEngine = new();
    private readonly Mock<IActionManager> _actionManager = new();

    [Fact]
    public void ResolveIntent_UsesGambitDecisionAndActionDefinition()
    {
        var state = CreateState();
        _gambitEngine
            .Setup(engine => engine.DecideActionWithMetadata(
                It.IsAny<CombatActorState>(),
                state,
                It.IsAny<IEnumerable<string>>()))
            .Returns(Core.Common.Result<GambitDecision>.Success(new GambitDecision
            {
                GambitId = "enemy_attack",
                Priority = 50,
                Action = new EntityAction
                {
                    ActionType = ActionType.POWER,
                    PowerId = "slash",
                    TargetId = "hero"
                },
                Intent = new GambitIntentDefinition
                {
                    DisplayName = "Enemy raises blade",
                    TelegraphType = "Attack",
                    Tags = new List<string> { "intent" }
                }
            }));
        _actionManager
            .Setup(manager => manager.GetDefinition("slash"))
            .Returns(Core.Common.Result<ActionDefinition>.Success(new ActionDefinition
            {
                ActionId = "slash",
                DisplayName = "Slash",
                Description = "Deal damage.",
                ActionType = ActionType.POWER,
                Tags = new List<string> { "physical" },
                Effects = new List<EffectDefinition>
                {
                    new() { Type = EffectType.DAMAGE, FlatValue = 8, Repeat = 2 }
                }
            }));

        var resolver = new IntentResolver(_gambitEngine.Object, _actionManager.Object);

        var result = resolver.ResolveIntent(state, "enemy-1");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("enemy-1", result.Value.ActorId);
        Assert.Equal(ActionType.POWER, result.Value.ActionType);
        Assert.Equal("slash", result.Value.PowerId);
        Assert.Equal("hero", result.Value.TargetId);
        Assert.Equal("Enemy raises blade", result.Value.DisplayName);
        Assert.Equal("Attack", result.Value.TelegraphType);
        Assert.Equal(16, result.Value.EstimatedDamage);
        Assert.Equal("enemy_attack", result.Value.SourceGambitId);
        Assert.Contains("intent", result.Value.Tags);
        Assert.Contains("physical", result.Value.Tags);
    }

    [Fact]
    public void ResolveEnemyIntents_ReturnsAliveEnemyIntentsOnly()
    {
        var state = CreateState(includeDeadEnemy: true);
        _gambitEngine
            .Setup(engine => engine.DecideActionWithMetadata(It.IsAny<CombatActorState>(), state, It.IsAny<IEnumerable<string>>()))
            .Returns(Core.Common.Result<GambitDecision>.Success(new GambitDecision
            {
                Action = new EntityAction { ActionType = ActionType.PASS }
            }));

        var resolver = new IntentResolver(_gambitEngine.Object, _actionManager.Object);

        var result = resolver.ResolveEnemyIntents(state);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(result.Value);
        Assert.Equal("enemy-1", result.Value[0].ActorId);
    }

    [Fact]
    public void ResolveIntent_LeavesEstimatedDamageEmptyForFormulaDamage()
    {
        var state = CreateState();
        _gambitEngine
            .Setup(engine => engine.DecideActionWithMetadata(It.IsAny<CombatActorState>(), state, It.IsAny<IEnumerable<string>>()))
            .Returns(Core.Common.Result<GambitDecision>.Success(new GambitDecision
            {
                Action = new EntityAction
                {
                    ActionType = ActionType.POWER,
                    PowerId = "formula_attack",
                    TargetId = "hero"
                }
            }));
        _actionManager
            .Setup(manager => manager.GetDefinition("formula_attack"))
            .Returns(Core.Common.Result<ActionDefinition>.Success(new ActionDefinition
            {
                ActionId = "formula_attack",
                Effects = new List<EffectDefinition>
                {
                    new() { Type = EffectType.DAMAGE, FormulaValue = "actor_power * 2" }
                }
            }));

        var resolver = new IntentResolver(_gambitEngine.Object, _actionManager.Object);

        var result = resolver.ResolveIntent(state, "enemy-1");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Null(result.Value.EstimatedDamage);
    }

    private static CombatState CreateState(bool includeDeadEnemy = false)
    {
        var enemies = new List<CombatActorState> { CreateEntity("enemy-1", "Enemy", isHero: false, health: 40) };
        if (includeDeadEnemy)
            enemies.Add(CreateEntity("enemy-2", "Dead Enemy", isHero: false, health: 0));

        var hero = CreateEntity("hero", "Hero", isHero: true, health: 100);
        return new CombatState
        {
            Actors = enemies.Append(hero).ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal)
        };
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
                    ["health"] = new()
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
                            DefaultMin = 0,
                            ThresholdPolicies =
                            [
                                new ResourceThresholdPolicy
                                {
                                    PolicyId = "defeat_when_depleted",
                                    Comparison = ResourceThresholdComparison.LessThanOrEqual,
                                    ThresholdSource = ResourceThresholdSource.Minimum,
                                    Consequence = ResourceThresholdConsequence.DefeatOwner
                                }
                            ]
                        }
                    }
                }
            }
        };
    }
}
