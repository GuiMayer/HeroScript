using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Combat.Models;

namespace Core.Run.Content;

public interface ICardContentCompiler
{
    Result<CompiledCardDefinition> Compile(
        CardContentDefinition card,
        IReadOnlyDictionary<string, CardComponentBundleDefinition>? bundles = null);

    Result<CompiledCardDefinition> Compile(
        string cardId,
        ContentRuntime runtime);
}

/// <summary>
/// Pure deterministic compiler from authored card content to one closed
/// component container. No live file or cache is consulted during compilation.
/// </summary>
public sealed class CardContentCompiler : ICardContentCompiler
{
    public Result<CompiledCardDefinition> Compile(
        string cardId,
        ContentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var card = runtime.GetDefinition<CardContentDefinition>("cards", cardId);
        if (card.IsFailure)
            return Result<CompiledCardDefinition>.Failure(card.Error);

        var bundles = ImmutableDictionary.CreateBuilder<string, CardComponentBundleDefinition>(
            StringComparer.Ordinal);
        foreach (var bundleId in runtime.GetDefinitions("card-component-bundles").Keys
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            var bundle = runtime.GetDefinition<CardComponentBundleDefinition>(
                "card-component-bundles",
                bundleId);
            if (bundle.IsFailure)
                return Result<CompiledCardDefinition>.Failure(bundle.Error);
            bundles[bundleId] = bundle.Value;
        }

        return Compile(card.Value, bundles.ToImmutable());
    }

    public Result<CompiledCardDefinition> Compile(
        CardContentDefinition card,
        IReadOnlyDictionary<string, CardComponentBundleDefinition>? bundles = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrWhiteSpace(card.CardId))
            return Result<CompiledCardDefinition>.Failure("CardId is required");
        if (card.Tags.Any(string.IsNullOrWhiteSpace) ||
            card.Tags.Distinct(StringComparer.Ordinal).Count() != card.Tags.Count)
            return Result<CompiledCardDefinition>.Failure($"Card {card.CardId} tags must be nonempty and unique");

        bundles ??= ImmutableDictionary<string, CardComponentBundleDefinition>.Empty;
        var slots = CardBundleCompiler.ValidateSlots(card.TransformationSlots);
        if (slots.IsFailure) return Result<CompiledCardDefinition>.Failure(slots.Error);
        if (card.ComponentBundles.Any(reference => reference == null) ||
            card.ComponentBundles.Select(reference => reference.Namespace).Distinct(StringComparer.Ordinal).Count() != card.ComponentBundles.Count)
            return Result<CompiledCardDefinition>.Failure("Bundle namespaces must be unique");
        var expanded = new List<CardComponentDefinition>();
        foreach (var reference in card.ComponentBundles)
        {
            if (string.IsNullOrWhiteSpace(reference.BundleId) || !bundles.TryGetValue(reference.BundleId, out var bundle))
            {
                return Result<CompiledCardDefinition>.Failure(
                    $"Card {card.CardId} references missing component bundle {reference.BundleId}");
            }
            if (bundle.BundleId != reference.BundleId)
                return Result<CompiledCardDefinition>.Failure("Bundle definition identity mismatch");
            var members = CardBundleCompiler.Expand(bundle, reference.Namespace);
            if (members.IsFailure) return Result<CompiledCardDefinition>.Failure(members.Error);
            expanded.AddRange(members.Value);
        }
        expanded.AddRange(card.Components);
        if (expanded.Any(component => component == null))
            return Result<CompiledCardDefinition>.Failure($"Card {card.CardId} contains a null component");

        var duplicate = expanded
            .Where(component => !string.IsNullOrWhiteSpace(component.ComponentId))
            .GroupBy(component => component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            return Result<CompiledCardDefinition>.Failure(
                $"Card {card.CardId} contains duplicate component id {duplicate.Key}");
        }

        foreach (var component in expanded)
        {
            var validation = ValidateComponent(card.CardId, component);
            if (validation.IsFailure)
                return Result<CompiledCardDefinition>.Failure(validation.Error);
        }
        var bindingErrors = new List<string>();
        void InspectBindings(EffectDefinition effect, string path)
            {
                foreach (var binding in effect.PayloadBindings.Where(binding => binding.CardEffectComponentId != null))
                {
                    var referenced = expanded.OfType<CardEffectComponentDefinition>()
                        .SingleOrDefault(item => item.ComponentId == binding.CardEffectComponentId);
                    var amount = referenced?.Effect.Parameters.SingleOrDefault(item => item.Parameter == EffectNumericParameter.Amount);
                    if (referenced == null || amount?.InputQuantityId != null ||
                        (amount?.FlatValue ?? referenced.Effect.FlatValue) == null &&
                        string.IsNullOrWhiteSpace(amount?.FormulaValue ?? referenced.Effect.FormulaValue))
                        bindingErrors.Add($"{path}: payload binding requires a numeric effective card component: {binding.CardEffectComponentId}");
                }
            }
        var actionErrors = EffectDefinitionValidator.Validate(expanded.OfType<CardEffectComponentDefinition>()
            .Select(component => component.Effect), InspectBindings);
        foreach (var trigger in expanded.OfType<CardTriggerComponentDefinition>())
            actionErrors = actionErrors.AddRange(EffectDefinitionValidator.Validate(trigger.Effects, InspectBindings));
        if (bindingErrors.Count > 0) return Result<CompiledCardDefinition>.Failure(string.Join("; ", bindingErrors));
        if (!actionErrors.IsEmpty)
            return Result<CompiledCardDefinition>.Failure($"Card {card.CardId}: {string.Join("; ", actionErrors)}");
        var references = ValidateResultReferences(expanded.OfType<CardEffectComponentDefinition>().Select(item => item.Effect));
        if (references.IsFailure) return Result<CompiledCardDefinition>.Failure(references.Error);
        foreach (var trigger in expanded.OfType<CardTriggerComponentDefinition>())
        {
            references = ValidateResultReferences(trigger.Effects);
            if (references.IsFailure) return Result<CompiledCardDefinition>.Failure(references.Error);
        }
        if (expanded.OfType<CardTargetingComponentDefinition>().Count() > 1)
            return Result<CompiledCardDefinition>.Failure($"Card {card.CardId} contains multiple targeting components");
        if (expanded.OfType<CardDispositionComponentDefinition>().Count() > 1)
            return Result<CompiledCardDefinition>.Failure($"Card {card.CardId} contains multiple disposition components");
        if (HasInvalidAmounts(card.BasePrices) || HasInvalidAmounts(card.DecomposeRewards))
            return Result<CompiledCardDefinition>.Failure($"Card {card.CardId} contains invalid price or decompose resources");

        var ordered = expanded
            .OrderBy(component => component.Order)
            .ThenBy(component => component.ComponentId, StringComparer.Ordinal)
            .ToImmutableArray();
        var payload = new CompiledCardPayload(
            card.CardId,
            card.Rarity,
            card.BasePrices.OrderBy(item => item.ResourceId, StringComparer.Ordinal).ToImmutableArray(),
            card.DecomposeRewards.OrderBy(item => item.ResourceId, StringComparer.Ordinal).ToImmutableArray(),
            card.Tags.OrderBy(tag => tag, StringComparer.Ordinal).ToImmutableArray(),
            card.TransformationSlots.OrderBy(slot => slot.SlotId, StringComparer.Ordinal).ToImmutableArray(),
            ordered);
        return Result<CompiledCardDefinition>.Success(new CompiledCardDefinition
        {
            CardId = payload.CardId,
            Rarity = payload.Rarity,
            BasePrices = payload.BasePrices,
            DecomposeRewards = payload.DecomposeRewards,
            Tags = payload.Tags,
            TransformationSlots = payload.TransformationSlots,
            Components = payload.Components,
            Fingerprint = CanonicalJson.ComputeHash(payload)
        });
    }

    private static Result ValidateComponent(string cardId, CardComponentDefinition component)
    {
        if (string.IsNullOrWhiteSpace(component.ComponentId))
            return Result.Failure($"Card {cardId} contains a component without componentId");

        return component switch
        {
            CardCostComponentDefinition cost => ValidateCosts(cardId, cost),
            CardEffectComponentDefinition effect => ValidateEffect(cardId, component.ComponentId, effect.Effect),
            CardConditionComponentDefinition condition when string.IsNullOrWhiteSpace(condition.Expression) =>
                Result.Failure($"Card {cardId} condition {component.ComponentId} requires expression"),
            CardTargetingComponentDefinition targeting when targeting.MinimumTargets < 0 ||
                                                           targeting.MaximumTargets < targeting.MinimumTargets ||
                                                           !Enum.IsDefined(targeting.Target) =>
                Result.Failure($"Card {cardId} targeting {component.ComponentId} has an invalid target range"),
            CardTargetingComponentDefinition targeting when
                targeting.Target is EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY &&
                string.IsNullOrWhiteSpace(targeting.SelectionResourceId) =>
                Result.Failure($"Card {cardId} targeting {component.ComponentId} requires selectionResourceId"),
            CardDispositionComponentDefinition disposition when
                string.IsNullOrWhiteSpace(disposition.CardZoneResolutionFlowId) =>
                Result.Failure($"Card {cardId} disposition {component.ComponentId} requires cardZoneResolutionFlowId"),
            CardTriggerComponentDefinition trigger when string.IsNullOrWhiteSpace(trigger.Boundary) =>
                Result.Failure($"Card {cardId} trigger {component.ComponentId} requires boundary"),
            CardInfluenceComponentDefinition influence when string.IsNullOrWhiteSpace(influence.Channel) ||
                                                               string.IsNullOrWhiteSpace(influence.Bucket) ||
                                                               influence.Value is { } value && !float.IsFinite(value) =>
                Result.Failure($"Card {cardId} influence {component.ComponentId} requires channel and bucket"),
            _ => Result.Success()
        };
    }

    private static Result ValidateResultReferences(IEnumerable<EffectDefinition> roots)
    {
        // EffectDefinitionValidator already bounded and validated this tree.
        var all = new List<EffectDefinition>();
        void Visit(EffectDefinition effect)
        {
            all.Add(effect);
            foreach (var child in effect.ChainedEffects ?? []) Visit(child);
        }
        foreach (var effect in roots) Visit(effect);
        var aliases = all.Where(effect => effect.OutputId != null).Select(effect => effect.OutputId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var formula in all.SelectMany(effect => new[] { effect.FormulaValue, effect.Condition }
            .Concat(effect.Parameters.Select(item => item.FormulaValue))
            .Concat(effect.PayloadBindings.Select(item => item.FormulaValue))).Where(formula => formula != null))
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(formula!,
            @"(?<![A-Za-z0-9_.])results\.([A-Za-z_][A-Za-z0-9_]*)\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (!aliases.Contains(match.Groups[1].Value)) return Result.Failure($"Card effect references missing output alias: {match.Groups[1].Value}");
        return Result.Success();
    }

    private static Result ValidateEffect(
        string cardId,
        string componentId,
        EffectDefinition effect)
    {
        if (effect == null) return Result.Failure($"Card {cardId} effect component {componentId} cannot have a null effect");
        if (effect.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE &&
            string.IsNullOrWhiteSpace(effect.TargetResource))
        {
            return Result.Failure(
                $"Card {cardId} effect component {componentId} requires targetResource");
        }
        if (effect.Target is EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY &&
            string.IsNullOrWhiteSpace(effect.SelectionResourceId))
        {
            return Result.Failure(
                $"Card {cardId} effect component {componentId} requires selectionResourceId");
        }
        return Result.Success();
    }

    private static Result ValidateCosts(string cardId, CardCostComponentDefinition component)
    {
        var costs = component.Costs;
        if (costs == null || costs.AlternativeCosts.Any(option => option == null || string.IsNullOrWhiteSpace(option.OptionId)) ||
            costs.AlternativeCosts.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count() != costs.AlternativeCosts.Count ||
            costs.Costs.Concat(costs.AlternativeCosts.SelectMany(option => option.Costs)).Any(item =>
                item == null || string.IsNullOrWhiteSpace(item.ResourceId) || !float.IsFinite(item.Amount) || item.Amount < 0))
            return Result.Failure($"Card {cardId} component {component.ComponentId} contains an invalid cost or alternative option");
        return Result.Success();
    }

    private static bool HasInvalidAmounts(IReadOnlyList<Core.Resources.ResourceAmount> amounts) =>
        amounts.Any(amount =>
            string.IsNullOrWhiteSpace(amount.ResourceId) ||
            float.IsNaN(amount.Amount) ||
            float.IsInfinity(amount.Amount) ||
            amount.Amount < 0) ||
        amounts.GroupBy(amount => amount.ResourceId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1);

    private sealed record CompiledCardPayload(
        string CardId,
        CardRarity Rarity,
        ImmutableArray<Core.Resources.ResourceAmount> BasePrices,
        ImmutableArray<Core.Resources.ResourceAmount> DecomposeRewards,
        ImmutableArray<string> Tags,
        ImmutableArray<CardTransformationSlotDefinition> TransformationSlots,
        ImmutableArray<CardComponentDefinition> Components);
}
