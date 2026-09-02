using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;

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
            bundles[bundleId] = bundle.Value with
            {
                BundleId = string.IsNullOrWhiteSpace(bundle.Value.BundleId)
                    ? bundleId
                    : bundle.Value.BundleId
            };
        }

        return Compile(
            card.Value with
            {
                CardId = string.IsNullOrWhiteSpace(card.Value.CardId) ? cardId : card.Value.CardId
            },
            bundles.ToImmutable());
    }

    public Result<CompiledCardDefinition> Compile(
        CardContentDefinition card,
        IReadOnlyDictionary<string, CardComponentBundleDefinition>? bundles = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrWhiteSpace(card.CardId))
            return Result<CompiledCardDefinition>.Failure("CardId is required");

        bundles ??= ImmutableDictionary<string, CardComponentBundleDefinition>.Empty;
        var expanded = new List<CardComponentDefinition>();
        foreach (var bundleId in card.ComponentBundleIds)
        {
            if (!bundles.TryGetValue(bundleId, out var bundle))
            {
                return Result<CompiledCardDefinition>.Failure(
                    $"Card {card.CardId} references missing component bundle {bundleId}");
            }
            expanded.AddRange(bundle.Components);
        }
        expanded.AddRange(card.Components);

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
        if (expanded.OfType<CardTargetingComponentDefinition>().Count() > 1)
            return Result<CompiledCardDefinition>.Failure($"Card {card.CardId} contains multiple targeting components");
        if (expanded.OfType<CardDispositionComponentDefinition>().Count() > 1)
            return Result<CompiledCardDefinition>.Failure($"Card {card.CardId} contains multiple disposition components");

        var ordered = expanded
            .OrderBy(component => component.Order)
            .ThenBy(component => component.ComponentId, StringComparer.Ordinal)
            .ToImmutableArray();
        var payload = new CompiledCardPayload(
            card.CardId,
            card.Rarity,
            card.BaseGoldPrice,
            card.DecomposePowerPoints,
            card.Tags.OrderBy(tag => tag, StringComparer.Ordinal).ToImmutableArray(),
            ordered);
        return Result<CompiledCardDefinition>.Success(new CompiledCardDefinition
        {
            CardId = payload.CardId,
            Rarity = payload.Rarity,
            BaseGoldPrice = payload.BaseGoldPrice,
            DecomposePowerPoints = payload.DecomposePowerPoints,
            Tags = payload.Tags,
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
            CardCostComponentDefinition cost when cost.Costs.Costs.Any(item =>
                string.IsNullOrWhiteSpace(item.ResourceId) || item.Amount < 0) =>
                Result.Failure($"Card {cardId} component {component.ComponentId} contains an invalid cost"),
            CardEffectComponentDefinition effect => ValidateEffect(cardId, component.ComponentId, effect.Effect),
            CardConditionComponentDefinition condition when string.IsNullOrWhiteSpace(condition.Expression) =>
                Result.Failure($"Card {cardId} condition {component.ComponentId} requires expression"),
            CardTargetingComponentDefinition targeting when targeting.MinimumTargets < 0 ||
                                                           targeting.MaximumTargets < targeting.MinimumTargets =>
                Result.Failure($"Card {cardId} targeting {component.ComponentId} has an invalid target range"),
            CardTargetingComponentDefinition targeting when
                targeting.Target is EffectTarget.LOWEST_HP_ENEMY or EffectTarget.HIGHEST_HP_ENEMY &&
                string.IsNullOrWhiteSpace(targeting.SelectionResourceId) =>
                Result.Failure($"Card {cardId} targeting {component.ComponentId} requires selectionResourceId"),
            CardTriggerComponentDefinition trigger when string.IsNullOrWhiteSpace(trigger.Boundary) =>
                Result.Failure($"Card {cardId} trigger {component.ComponentId} requires boundary"),
            CardInfluenceComponentDefinition influence when string.IsNullOrWhiteSpace(influence.Channel) ||
                                                               string.IsNullOrWhiteSpace(influence.Bucket) =>
                Result.Failure($"Card {cardId} influence {component.ComponentId} requires channel and bucket"),
            _ => Result.Success()
        };
    }

    private static Result ValidateEffect(
        string cardId,
        string componentId,
        EffectDefinition effect)
    {
        if (effect.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE &&
            string.IsNullOrWhiteSpace(effect.TargetResource))
        {
            return Result.Failure(
                $"Card {cardId} effect component {componentId} requires targetResource");
        }
        return Result.Success();
    }

    private sealed record CompiledCardPayload(
        string CardId,
        CardRarity Rarity,
        int BaseGoldPrice,
        int DecomposePowerPoints,
        ImmutableArray<string> Tags,
        ImmutableArray<CardComponentDefinition> Components);
}
