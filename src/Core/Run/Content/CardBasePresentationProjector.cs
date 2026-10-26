using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Effects;

namespace Core.Run.Content;

/// <summary>Pure container projection. No actor, legality, fake deck instance, RNG or gameplay execution.</summary>
public static class CardBasePresentationProjector
{
    public static EffectiveCardDefinition DefinitionBase(CompiledCardDefinition definition) => new()
    {
        DefinitionId = definition.CardId, DefinitionFingerprint = definition.Fingerprint,
        Tags = definition.Tags.ToImmutableArray(), Components = definition.Components,
        TransformationSlots = definition.TransformationSlots, Fingerprint = definition.Fingerprint
    };

    public static Result<CardPresentationFacts> Project(CompiledCardDefinition original, EffectiveCardDefinition composed,
        CardPresentationIdentity identity, CardPresentationVersion version)
    {
        var valid = identity.Validate();
        if (valid.IsFailure) return Result<CardPresentationFacts>.Failure(valid.Error);
        if (identity.DefinitionId != original.CardId || composed.DefinitionId != original.CardId ||
            composed.DefinitionFingerprint != original.Fingerprint || string.IsNullOrWhiteSpace(version.ContentRevision) ||
            identity.CardInstanceId != null && identity.CardInstanceId != composed.CardInstanceId)
            return Result<CardPresentationFacts>.Failure("Presentation definition, composition and identity do not match");
        var originalEffects = Effects(original.Components).ToDictionary(effect => effect.EffectPath, StringComparer.Ordinal);
        var effects = ImmutableArray.CreateBuilder<CardEffectPresentation>();
        foreach (var effect in Effects(composed.Components))
        {
            originalEffects.TryGetValue(effect.EffectPath, out var baseline);
            var parameters = Enum.GetValues<EffectNumericParameter>().Where(parameter => EffectNumericParameters.Supports(effect.Definition.Type, parameter))
                .Where(parameter => Include(effect.Definition, parameter)).Select(parameter =>
            {
                var current = Read(effect.Definition, parameter);
                var previous = baseline == null ? null : Read(baseline.Definition, parameter);
                return new CardParameterPresentation(parameter, current with { Original = previous?.ComposedBase });
            }).ToImmutableArray();
            effects.Add(effect with { Parameters = parameters });
        }
        var facts = new CardPresentationFacts
        {
            Identity = identity,
            Version = version with { DefinitionFingerprint = original.Fingerprint, CompositionFingerprint = composed.Fingerprint },
            FaceValueMode = CardFaceValueMode.ComposedBase, Rarity = original.Rarity, Tags = composed.Tags,
            Costs = composed.All<CardCostComponentDefinition>().OrderBy(component => component.Order).ThenBy(component => component.ComponentId, StringComparer.Ordinal)
                .Select(component => new CardCostPresentation(component.ComponentId, component.Costs)).ToImmutableArray(),
            Effects = effects.ToImmutable(), Conditions = composed.All<CardConditionComponentDefinition>().ToImmutableArray(),
            Targeting = composed.All<CardTargetingComponentDefinition>().ToImmutableArray(),
            Dispositions = composed.All<CardDispositionComponentDefinition>().ToImmutableArray(),
            RuleOverrides = composed.All<CardEffectRuleOverrideComponentDefinition>().ToImmutableArray(),
            Influences = composed.All<CardInfluenceComponentDefinition>().ToImmutableArray(),
            PermanentChanges = composed.UpgradeTrace.ToImmutableArray(),
            ComponentContributions = composed.ComponentContributions
        };
        return Result<CardPresentationFacts>.Success(facts with { Fingerprint = CanonicalJson.ComputeHash(facts) });
    }

    private static bool Include(EffectDefinition effect, EffectNumericParameter parameter) =>
        parameter is not (EffectNumericParameter.StatusDuration or EffectNumericParameter.ModifierDuration) ||
        effect.Parameters.Any(value => value.Parameter == parameter) ||
        (parameter == EffectNumericParameter.StatusDuration ? effect.StatusDuration : effect.ModifierDuration) != null;

    private static CardNumericLayers Read(EffectDefinition effect, EffectNumericParameter parameter)
    {
        var authored = effect.Parameters.FirstOrDefault(value => value.Parameter == parameter);
        var expression = authored?.FormulaValue ?? (parameter == EffectNumericParameter.Amount ? effect.FormulaValue : null);
        var input = authored?.InputQuantityId;
        float? value = authored != null ? authored.FlatValue : parameter switch
        {
            EffectNumericParameter.Amount => effect.FlatValue ?? 0,
            EffectNumericParameter.StatusStacks => effect.StatusStacks,
            EffectNumericParameter.StatusDuration => effect.StatusDuration,
            EffectNumericParameter.ModifierStacks => effect.ModifierStacks,
            EffectNumericParameter.ModifierDuration => effect.ModifierDuration,
            EffectNumericParameter.CardCount => effect.CardCount,
            _ => null
        };
        var unresolved = !string.IsNullOrWhiteSpace(input) ? "transported_quantity_required" :
            !string.IsNullOrWhiteSpace(expression) ? "expression_context_required" : value == null ? "definition_default_required" : null;
        return new() { ComposedBase = unresolved == null ? value : null,
            UnitId = authored?.UnitId ?? (parameter is EffectNumericParameter.StatusDuration or EffectNumericParameter.ModifierDuration ? "turns" :
                parameter == EffectNumericParameter.Amount ? "scalar" : "count"),
            Expression = expression ?? input, UnresolvedReason = unresolved };
    }

    private static IEnumerable<CardEffectPresentation> Effects(IEnumerable<CardComponentDefinition> components)
    {
        foreach (var component in components.OrderBy(value => value.Order).ThenBy(value => value.ComponentId, StringComparer.Ordinal))
        {
            if (component is CardEffectComponentDefinition effect)
                foreach (var projected in Visit(effect.Effect, component.ComponentId, component.ComponentId, null, null, 0)) yield return projected;
            if (component is CardTriggerComponentDefinition trigger)
                foreach (var (definition, index) in trigger.Effects.Select((definition, index) => (definition, index)))
                    foreach (var projected in Visit(definition, component.ComponentId, $"{component.ComponentId}/trigger/{index}", null, trigger.Boundary, 0)) yield return projected;
        }
    }

    private static IEnumerable<CardEffectPresentation> Visit(EffectDefinition effect, string componentId, string path,
        string? parent, string? boundary, int depth)
    {
        if (depth > EffectExecutionLimits.MaximumDepth) throw new InvalidOperationException("Presentation effect depth exceeds validated content limits");
        yield return new() { ComponentId = componentId, EffectPath = path, ParentEffectPath = parent, Boundary = boundary, Definition = effect };
        foreach (var (child, index) in (effect.ChainedEffects ?? []).Select((child, index) => (child, index)))
            foreach (var projected in Visit(child, componentId, $"{path}/children/{index}", path, boundary, depth + 1)) yield return projected;
    }
}
