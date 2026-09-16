using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardPlayEvaluatorTests
{
    private readonly VariableFormulaEvaluator _formulas = new();

    [Fact]
    public void Evaluate_UsesOneAuthorityForConditionsCostsAndTargets()
    {
        var combat = Combat(
            Entity("hero", true, ("mana", 2)),
            Entity("enemy", false, ("mana", 9)));
        var evaluator = CreateEvaluator();
        var card = Card(
            new CardCostComponentDefinition
            {
                ComponentId = "cost.mana",
                Costs = new ActionCosts
                {
                    Costs = [new ResourceCost { ResourceId = "mana", Amount = 2 }]
                }
            },
            new CardConditionComponentDefinition
            {
                ComponentId = "condition.enabled",
                Expression = "enabled",
                FailureReason = "Card is disabled"
            },
            Target(EffectTarget.TARGET));

        var result = evaluator.Evaluate(card, combat, new CardPlayRequest
        {
            ActorId = "hero",
            SelectedTargetIds = ["enemy"],
            Variables = new Dictionary<string, float> { ["enabled"] = 1 }
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value.IsLegal);
        Assert.Equal("enemy", Assert.Single(result.Value.LegalTargetIds));
        Assert.Equal("enemy", Assert.Single(result.Value.ResolvedTargetIds));
        var cost = Assert.Single(result.Value.Costs);
        Assert.Equal("mana", cost.ResourceId);
        Assert.Equal(2, cost.Amount);
        Assert.True(cost.Affordable);
        Assert.Equal(CardConsumeDestination.Discard, result.Value.Destination);
        Assert.Equal("ability.cooldown", result.Value.CardZoneResolutionFlowId);

        var graphResult = evaluator.Evaluate(card, combat, new CardPlayRequest
        {
            ActorId = "hero",
            SelectedTargetIds = ["enemy"],
            Variables = new Dictionary<string, float> { ["enabled"] = 1 },
            UseCardZoneResolution = true
        });
        Assert.True(graphResult.IsSuccess, graphResult.IsFailure ? graphResult.Error : null);
        Assert.Equal(CardConsumeDestination.None, graphResult.Value.Destination);
        Assert.Equal("ability.cooldown", graphResult.Value.CardZoneResolutionFlowId);
    }

    [Fact]
    public void Evaluate_ReportsAllRuleFailuresWithoutMutatingCombat()
    {
        var hero = Entity("hero", true, ("mana", 1));
        var combat = Combat(hero, Entity("enemy", false, ("mana", 4)));
        var card = Card(
            new CardCostComponentDefinition
            {
                ComponentId = "cost.mana",
                Costs = new ActionCosts
                {
                    Costs = [new ResourceCost { ResourceId = "mana", Amount = 3 }]
                }
            },
            new CardConditionComponentDefinition
            {
                ComponentId = "condition.enabled",
                Expression = "enabled",
                FailureReason = "Card is disabled"
            },
            Target(EffectTarget.TARGET));

        var result = CreateEvaluator().Evaluate(card, combat, new CardPlayRequest
        {
            ActorId = "hero",
            SelectedTargetIds = ["hero"],
            Variables = new Dictionary<string, float> { ["enabled"] = 0 }
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.False(result.Value.IsLegal);
        Assert.Contains("Card is disabled", result.Value.FailureReasons);
        Assert.Contains("Costs for cost.mana cannot be paid", result.Value.FailureReasons);
        Assert.Contains("Selection contains an illegal target", result.Value.FailureReasons);
        Assert.Equal(1, combat.GetActor("hero")!.GetResource("mana")!.Current);
    }

    [Fact]
    public void Evaluate_AutomaticLowestTargetUsesConfiguredResource()
    {
        var hero = Entity("hero", true, ("mana", 5));
        var lowMana = Entity("enemy-a", false, ("mana", 1), ("health", 99));
        var lowHealth = Entity("enemy-b", false, ("mana", 8), ("health", 1));
        var card = Card(new CardTargetingComponentDefinition
        {
            ComponentId = "target.lowest_mana",
            Target = EffectTarget.LOWEST_RESOURCE_ENEMY,
            SelectionResourceId = "mana",
            MinimumTargets = 1,
            MaximumTargets = 1
        });

        var result = CreateEvaluator().Evaluate(
            card,
            Combat(hero, lowMana, lowHealth),
            new CardPlayRequest { ActorId = "hero" });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value.IsLegal);
        Assert.Equal("enemy-a", Assert.Single(result.Value.ResolvedTargetIds));
    }

    [Fact]
    public void Evaluate_ResourceVariablesAreCanonicalAndCannotBeOverriddenByCaller()
    {
        var hero = Entity("hero", true, ("mana", 5));
        var card = Card(new CardConditionComponentDefinition
        {
            ComponentId = "condition.has_mana",
            Expression = "source.resources.mana.current",
            FailureReason = "Actor has no mana"
        });

        var result = CreateEvaluator().Evaluate(
            card,
            Combat(hero),
            new CardPlayRequest
            {
                ActorId = "hero",
                Variables = new Dictionary<string, float>
                {
                    ["source.resources.mana.current"] = 0
                }
            });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.True(result.Value.IsLegal);
    }

    [Fact]
    public void Evaluate_AutomaticResourceTargetIgnoresEntitiesWithoutConfiguredResource()
    {
        var hero = Entity("hero", true, ("mana", 5));
        var hasMana = Entity("enemy-b", false, ("mana", 8));
        var lacksMana = Entity("enemy-a", false, ("health", 1));
        var card = Card(new CardTargetingComponentDefinition
        {
            ComponentId = "target.lowest_mana",
            Target = EffectTarget.LOWEST_RESOURCE_ENEMY,
            SelectionResourceId = "mana",
            MinimumTargets = 1,
            MaximumTargets = 1
        });

        var result = CreateEvaluator().Evaluate(
            card,
            Combat(hero, lacksMana, hasMana),
            new CardPlayRequest { ActorId = "hero" });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("enemy-b", Assert.Single(result.Value.ResolvedTargetIds));
    }

    [Fact]
    public void Evaluate_RejectsAggregateCostsAgainstTheSameResource()
    {
        var combat = Combat(
            Entity("hero", true, ("mana", 3)),
            Entity("enemy", false, ("mana", 9)));
        var card = Card(
            new CardCostComponentDefinition
            {
                ComponentId = "cost.first",
                Costs = new ActionCosts
                {
                    Costs = [new ResourceCost { ResourceId = "mana", Amount = 2 }]
                }
            },
            new CardCostComponentDefinition
            {
                ComponentId = "cost.second",
                Costs = new ActionCosts
                {
                    Costs = [new ResourceCost { ResourceId = "mana", Amount = 2 }]
                }
            });

        var result = CreateEvaluator().Evaluate(
            card,
            combat,
            new CardPlayRequest { ActorId = "hero" });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.False(result.Value.IsLegal);
        Assert.Contains(
            result.Value.FailureReasons,
            reason => reason.Contains("Selected card costs", StringComparison.Ordinal));
        Assert.Equal(3, combat.GetActor("hero")!.GetResource("mana")!.Current);
    }

    private CardPlayEvaluator CreateEvaluator() =>
        new(new ActionCostEvaluator(_formulas), _formulas);

    private static EffectiveCardDefinition Card(params CardComponentDefinition[] components) => new()
    {
        CardInstanceId = Guid.Parse("10000000-0000-8000-8000-000000000001"),
        DefinitionId = "test-card",
        Components = components.Concat(
        [
            new CardDispositionComponentDefinition
            {
                ComponentId = "disposition.default",
                Destination = CardConsumeDestination.Discard,
                CardZoneResolutionFlowId = "ability.cooldown"
            }
        ]).ToArray()
    };

    private static CardTargetingComponentDefinition Target(EffectTarget target) => new()
    {
        ComponentId = "targeting.primary",
        Target = target,
        MinimumTargets = 1,
        MaximumTargets = 1
    };

    private static CombatState Combat(CombatActorState hero, params CombatActorState[] enemies) => new()
    {
        CombatId = Guid.Parse("20000000-0000-8000-8000-000000000001"),
        Actors = enemies.Append(hero).ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal)
    };

    private static CombatActorState Entity(
        string id,
        bool isHero,
        params (string Id, float Current)[] resources) => new()
    {
        InstanceId = id,
        SideId = isHero ? "player" : "opposition", ControllerBinding = new ControllerBinding { Kind = isHero ? ControllerKind.Player : ControllerKind.AI },
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

    private sealed class VariableFormulaEvaluator : IRuntimeFormulaEvaluator
    {
        public Result<float> Evaluate(
            string expressionOrFormulaId,
            Dictionary<string, float>? variables = null,
            float initialValue = 0) =>
            variables != null && variables.TryGetValue(expressionOrFormulaId, out var value)
                ? Result<float>.Success(value)
                : Result<float>.Failure($"Variable not found: {expressionOrFormulaId}");
    }
}
