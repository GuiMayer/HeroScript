using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class AbilityExecutorTests
{
    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Execute_AppliesCostsAndEffectsWithoutMutatingInput()
    {
        var ability = Ability(flatValue: 7);
        var combat = Combat();
        var result = Executor(ability).Execute(Request(combat));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.Combat.GetActor("hero")!.GetResource("energy")!.Current);
        Assert.Equal(13, result.Value.Combat.GetActor("enemy")!.GetResource("health")!.Current);
        Assert.Equal(3, combat.GetActor("hero")!.GetResource("energy")!.Current);
        Assert.Equal(20, combat.GetActor("enemy")!.GetResource("health")!.Current);
        Assert.Equal(2, result.Value.Applications.Count);
        var action = Assert.Single(result.Value.Combat.ActionHistory);
        Assert.Equal(ActionType.POWER, action.ActionType);
        Assert.Equal("test_ability", action.PowerId);
        Assert.Equal("enemy", action.TargetId);
        Assert.Equal(result.Value.Applications, action.Applications);
    }

    [Fact]
    public void Execute_SameSnapshotProducesSameFingerprintAndState()
    {
        var ability = Ability(flatValue: 4, chance: .65f, repeat: 3);
        var combat = Combat();
        var executor = Executor(ability);
        var request = Request(combat);

        var first = executor.Execute(request);
        var second = executor.Execute(request);

        Assert.True(first.IsSuccess && second.IsSuccess);
        Assert.Equal(first.Value.ResolutionFingerprint, second.Value.ResolutionFingerprint);
        Assert.Equal(
            CanonicalJson.ComputeHash(first.Value.Combat),
            CanonicalJson.ComputeHash(second.Value.Combat));
        Assert.Equal(combat.Determinism, request.Combat.Determinism);
    }

    private static AbilityExecutor Executor(ActionDefinition definition)
    {
        var actions = new Mock<IActionManager>();
        actions.As<IRevisionedActionCatalog>()
            .Setup(catalog => catalog.GetDefinition("test_ability", Revision, "default"))
            .Returns(Result<ActionDefinition>.Success(definition));
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        var effects = new ImmutableEffectProcessor();
        return new AbilityExecutor(
            actions.Object,
            new CardPlayEvaluator(new ActionCostEvaluator(formulas.Object), formulas.Object),
            new EffectTriggerExecutor(formulas.Object, effects));
    }

    private static ActionDefinition Ability(float flatValue, float chance = 1, int repeat = 1) => new()
    {
        ActionId = "test_ability",
        ActionType = ActionType.POWER,
        RequiresTarget = true,
        Costs = new ActionCosts
        {
            Costs = [new ResourceCost { ResourceId = "energy", Amount = 1 }]
        },
        Effects =
        [
            new EffectDefinition
            {
                EffectId = "test_ability.damage",
                Type = EffectType.DAMAGE,
                Target = EffectTarget.TARGET,
                TargetResource = "health",
                FlatValue = flatValue,
                Chance = chance,
                Repeat = repeat,
                Tags = ["damage"]
            }
        ]
    };

    private static AbilityExecutionRequest Request(CombatState combat) => new()
    {
        Run = new RunState
        {
            RunId = Guid.Parse("10000000-0000-8000-8000-000000000001"),
            ConfigName = "default",
            Determinism = DeterministicContext.Create(31, Revision)
        },
        Combat = combat,
        ActionId = "test_ability",
        ActorId = "hero",
        SelectedTargetIds = ["enemy"]
    };

    private static CombatState Combat() => new()
    {
        CombatId = Guid.Parse("20000000-0000-8000-8000-000000000001"),
        Actors = new[] { Entity("hero", true, ("energy", 3), ("health", 30)),
                Entity("enemy", false, ("health", 20)) }
            .ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
        Determinism = DeterministicContext.Create(47, Revision)
    };

    private static CombatActorState Entity(
        string id,
        bool hero,
        params (string Id, float Current)[] resources) => new()
    {
        InstanceId = id,
        SideId = hero ? "player" : "opposition", ControllerBinding = new ControllerBinding { Kind = hero ? ControllerKind.Player : ControllerKind.AI },
        ResourceState = new ResourceSet
        {
            OwnerId = id,
            Resources = resources.ToDictionary(
                item => item.Id,
                item => new ResourcePool
                {
                    ResourceId = item.Id,
                    Current = item.Current,
                    Minimum = 0,
                    Maximum = 100,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = item.Id,
                        DisplayName = item.Id
                    }
                },
                StringComparer.Ordinal)
        }
    };
}
