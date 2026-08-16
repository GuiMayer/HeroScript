namespace Core.Effects;

public static class EffectModifierTransformer
{
    public static EffectDefinition Apply(
        EffectDefinition definition,
        IEnumerable<EffectModifier> modifiers)
    {
        return modifiers
            .Where(modifier => IsApplicable(modifier, definition))
            .Aggregate(definition, ApplyOne);
    }

    private static bool IsApplicable(EffectModifier modifier, EffectDefinition definition)
    {
        if (modifier.RequiredTags is { Count: > 0 }
            && !modifier.RequiredTags.All(definition.Tags.Contains))
            return false;
        if (modifier.ExcludedTags is { Count: > 0 }
            && modifier.ExcludedTags.Any(definition.Tags.Contains))
            return false;
        return true;
    }

    private static EffectDefinition ApplyOne(EffectDefinition definition, EffectModifier modifier) =>
        modifier.Type switch
        {
            EffectModifierType.MULTIPLY_VALUE when modifier.ValueMultiplier.HasValue =>
                definition.FlatValue.HasValue
                    ? definition with { FlatValue = definition.FlatValue * modifier.ValueMultiplier }
                    : definition,
            EffectModifierType.ADD_VALUE when modifier.ValueAddition.HasValue =>
                definition.FlatValue.HasValue
                    ? definition with { FlatValue = definition.FlatValue + modifier.ValueAddition }
                    : definition,
            EffectModifierType.CHANGE_TYPE when modifier.OverrideType.HasValue =>
                definition with { Type = modifier.OverrideType.Value },
            EffectModifierType.CHANGE_TARGET when modifier.OverrideTarget.HasValue =>
                definition with { Target = modifier.OverrideTarget.Value },
            EffectModifierType.ADD_TAGS when modifier.AddTags is { Count: > 0 } =>
                definition with
                {
                    Tags = definition.Tags
                        .Concat(modifier.AddTags)
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
                },
            EffectModifierType.REMOVE_TAGS when modifier.RemoveTags is { Count: > 0 } =>
                definition with
                {
                    Tags = definition.Tags
                        .Where(tag => !modifier.RemoveTags.Contains(tag))
                        .ToList()
                },
            EffectModifierType.MULTIPLY_CHANCE when modifier.ChanceMultiplier.HasValue =>
                definition with { Chance = System.Math.Min(1f, definition.Chance * modifier.ChanceMultiplier.Value) },
            EffectModifierType.ADD_REPEAT when modifier.RepeatAddition.HasValue =>
                definition with { Repeat = definition.Repeat + modifier.RepeatAddition.Value },
            _ => definition
        };
}
