using Core.Combat.Models;
using Core.Effects;
using Core.Run.Content;

namespace Core.Tests.Run;

internal static class CardPresentationFixtures
{
    internal static IEnumerable<(string Name, EffectDefinition Effect, EffectNumericParameter? Parameter, float? Value)> Cases => [
        ("damage", new() { Type = EffectType.DAMAGE, TargetResource = "health", FlatValue = 8 }, EffectNumericParameter.Amount, 8),
        ("heal", new() { Type = EffectType.HEAL, TargetResource = "health", FlatValue = 6 }, EffectNumericParameter.Amount, 6),
        ("block", new() { Type = EffectType.MODIFY_RESOURCE, TargetResource = "block", FlatValue = 9 }, EffectNumericParameter.Amount, 9),
        ("stacks", new() { Type = EffectType.APPLY_STATUS, StatusId = "poison", StatusStacks = 3 }, EffectNumericParameter.StatusStacks, 3),
        ("conditional", new() { Type = EffectType.DAMAGE, TargetResource = "mana", FlatValue = 8, Condition = "source.resources.focus.current > 0" }, EffectNumericParameter.Amount, 8),
        ("multihit", new() { Type = EffectType.DAMAGE, TargetResource = "health", FlatValue = 12, Repeat = 3 }, EffectNumericParameter.Amount, 12),
        ("condensation", new() { Type = EffectType.CONDENSE_STACKS, CondensationRecipeId = "release" }, null, null),
        ("no-critical", new() { Type = EffectType.DAMAGE, TargetResource = "health", FlatValue = 8, RuleOverrides = [new() { Critical = false }] }, EffectNumericParameter.Amount, 8),
        ("contextual", new() { Type = EffectType.DAMAGE, TargetResource = "mana", FormulaValue = "target.resources.mana.current / 2" }, EffectNumericParameter.Amount, null)
    ];

    internal static CardContentDefinition Card(string name, EffectDefinition effect) => new()
    {
        CardId = name, Components = [new CardCostComponentDefinition { ComponentId = "cost", Costs = new() {
            Costs = [new ResourceCost { ResourceId = "energy", Amount = 1 }], AlternativeCosts = [
                new AlternativeCostOption { OptionId = "mana", Costs = [new ResourceCost { ResourceId = "mana", Amount = 2 }] }] } },
            new CardEffectComponentDefinition { ComponentId = "main", Effect = effect }]
    };
}
