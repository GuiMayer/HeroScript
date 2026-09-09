using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Resources;
using Core.Run.Content;

namespace Core.Run;

/// <summary>
/// Resolves definitions against the immutable content revision pinned to a run.
/// </summary>
internal sealed class RunContentDefinitionResolver(IContentRuntimeResolver contentRuntimes)
{
    public Result<T> Resolve<T>(
        RunState run,
        string kind,
        string definitionId)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        var runtime = contentRuntimes.Resolve(
            run.Determinism.ContentRevision,
            run.ConfigName);
        return runtime.IsFailure
            ? Result<T>.Failure(runtime.Error)
            : runtime.Value.GetDefinition<T>(kind, definitionId);
    }
}

internal sealed record CardSelectionOfferGeneration(
    IReadOnlyList<CardSelectionOptionState> Options,
    DeterministicContext Context,
    string Fingerprint);

internal sealed record ShopOfferGeneration(
    IReadOnlyList<ShopItemState> Items,
    DeterministicContext Context,
    string Fingerprint);

/// <summary>
/// Pure offer policy over a pinned content view and deterministic context.
/// It does not mutate a run, publish events or persist commits.
/// </summary>
internal sealed class RunOfferGenerator(
    ICardPoolResolver? cardPools,
    ICardContentCatalog? cards,
    bool useRevisionedContent)
{
    public Result<CardSelectionOfferGeneration> GenerateCardSelection(
        RunState state,
        CardSelectionDefinition definition,
        IReadOnlySet<string> lockedCardIds)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(lockedCardIds);

        var lockedOptions = lockedCardIds
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => CreateCardSelectionOption(state, cardId))
            .Where(option => option != null)
            .Cast<CardSelectionOptionState>()
            .ToList();
        var desiredCount = System.Math.Max(1, definition.OfferCount) - lockedOptions.Count;
        if (!string.IsNullOrWhiteSpace(definition.CardPoolId))
        {
            if (cardPools == null)
            {
                return Result<CardSelectionOfferGeneration>.Failure(
                    $"Card pool resolver is unavailable for {definition.CardPoolId}");
            }

            var poolResult = useRevisionedContent && cardPools is IRevisionedCardPoolResolver revisionedPools
                ? revisionedPools.ResolvePool(
                    definition.CardPoolId,
                    state.Determinism.ContentRevision,
                    state.ConfigName)
                : cardPools.ResolvePool(definition.CardPoolId, state.ConfigName);
            if (poolResult.IsFailure)
                return Result<CardSelectionOfferGeneration>.Failure(poolResult.Error);
            var offer = CardOfferResolver.Resolve(
                poolResult.Value,
                System.Math.Max(0, desiredCount),
                state.Determinism,
                lockedCardIds);
            if (offer.IsFailure)
                return Result<CardSelectionOfferGeneration>.Failure(offer.Error);
            lockedOptions.AddRange(offer.Value.Cards.Select(ToOption));
            return Result<CardSelectionOfferGeneration>.Success(new(
                lockedOptions,
                offer.Value.Context,
                offer.Value.Fingerprint));
        }

        var candidates = definition.CardPool
            .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
            .Where(cardId => !lockedCardIds.Contains(cardId))
            .Select(cardId => CreateCardSelectionOption(state, cardId) ?? new CardSelectionOptionState { CardId = cardId })
            .OrderBy(option => option.CardId, StringComparer.Ordinal)
            .ToList();
        lockedOptions.AddRange(candidates.Take(System.Math.Max(0, desiredCount)));
        return Result<CardSelectionOfferGeneration>.Success(new(
            lockedOptions,
            state.Determinism,
            CanonicalJson.ComputeHash(lockedOptions.Select(option => option.CardId).ToArray())));
    }

    public Result<ShopOfferGeneration> GenerateShop(RunState state, ShopDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definition);

        if (!string.IsNullOrWhiteSpace(definition.CardPoolId))
        {
            if (cardPools == null)
            {
                return Result<ShopOfferGeneration>.Failure(
                    $"Card pool resolver is unavailable for {definition.CardPoolId}");
            }

            var poolResult = useRevisionedContent && cardPools is IRevisionedCardPoolResolver revisionedPools
                ? revisionedPools.ResolvePool(
                    definition.CardPoolId,
                    state.Determinism.ContentRevision,
                    state.ConfigName)
                : cardPools.ResolvePool(definition.CardPoolId, state.ConfigName);
            if (poolResult.IsFailure)
                return Result<ShopOfferGeneration>.Failure(poolResult.Error);
            var offer = CardOfferResolver.Resolve(
                poolResult.Value,
                System.Math.Max(1, definition.OfferCount),
                state.Determinism);
            if (offer.IsFailure)
                return Result<ShopOfferGeneration>.Failure(offer.Error);
            return Result<ShopOfferGeneration>.Success(new(
                offer.Value.Cards
                    .Select((card, index) => ToShopItem(card, definition.Pricing, index))
                    .ToList(),
                offer.Value.Context,
                offer.Value.Fingerprint));
        }

        var items = definition.Items
            .Select((item, index) => ToShopItem(state, item, definition.Pricing, index))
            .ToList();
        return Result<ShopOfferGeneration>.Success(new(
            items,
            state.Determinism,
            CanonicalJson.ComputeHash(items.Select(item => item.ItemId).ToArray())));
    }

    private CardSelectionOptionState? CreateCardSelectionOption(RunState state, string cardId)
    {
        if (cards == null)
            return null;

        var cardResult = useRevisionedContent && cards is IRevisionedCardContentCatalog revisionedCards
            ? revisionedCards.GetCard(cardId, state.Determinism.ContentRevision, state.ConfigName)
            : cards.GetCard(cardId, state.ConfigName);
        return cardResult.IsSuccess ? ToOption(cardResult.Value) : null;
    }

    private ShopItemState ToShopItem(
        RunState state,
        ShopItemDefinition item,
        ShopPricingRules pricing,
        int index)
    {
        if (!string.IsNullOrWhiteSpace(item.CardId) && cards != null)
        {
            var cardResult = useRevisionedContent && cards is IRevisionedCardContentCatalog revisionedCards
                ? revisionedCards.GetCard(item.CardId, state.Determinism.ContentRevision, state.ConfigName)
                : cards.GetCard(item.CardId, state.ConfigName);
            if (cardResult.IsSuccess)
                return ToShopItem(cardResult.Value, pricing, index, item.ItemId, item.Costs);
        }

        return new ShopItemState
        {
            ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? $"item_{index + 1}" : item.ItemId,
            CardId = item.CardId,
            BaseCosts = item.Costs,
            Costs = item.Costs
        };
    }

    private static CardSelectionOptionState ToOption(CardContentDefinition card) => new()
    {
        CardId = card.CardId,
        Rarity = card.Rarity,
        Tags = card.Tags.ToList(),
        DecomposeRewards = card.DecomposeRewards
    };

    private static ShopItemState ToShopItem(
        CardContentDefinition card,
        ShopPricingRules pricing,
        int index,
        string? itemId = null,
        IReadOnlyList<ResourceAmount>? explicitCosts = null)
    {
        var calculated = CalculateShopPrices(card, pricing, explicitCosts);
        return new ShopItemState
        {
            ItemId = string.IsNullOrWhiteSpace(itemId) ? $"buy_{card.CardId}_{index + 1}" : itemId,
            CardId = card.CardId,
            Rarity = card.Rarity,
            Tags = card.Tags.ToList(),
            BaseCosts = calculated.BaseCosts,
            Costs = calculated.Costs,
            PricingBreakdowns = calculated.Breakdowns
        };
    }

    private static CalculatedShopPrices CalculateShopPrices(
        CardContentDefinition card,
        ShopPricingRules pricing,
        IReadOnlyList<ResourceAmount>? explicitCosts)
    {
        var baseCosts = (explicitCosts is { Count: > 0 } ? explicitCosts : card.BasePrices)
            .OrderBy(cost => cost.ResourceId, StringComparer.Ordinal)
            .ToImmutableArray();
        var rarityMultiplier = pricing.RarityMultipliers.TryGetValue(card.Rarity, out var rarityValue)
            ? rarityValue
            : 1.0;
        var tagMultiplier = card.Tags
            .Select(tag => pricing.TagMultipliers.TryGetValue(tag, out var value) ? value : 1.0)
            .Aggregate(1.0, (current, value) => current * value);
        var costs = ImmutableArray.CreateBuilder<ResourceAmount>(baseCosts.Length);
        var breakdowns = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseCost in baseCosts)
        {
            var raw = baseCost.Amount * pricing.BaseMultiplier * rarityMultiplier * tagMultiplier;
            var final = pricing.Rounding switch
            {
                ResourcePriceRounding.None => raw,
                ResourcePriceRounding.Floor => System.Math.Floor(raw),
                ResourcePriceRounding.Ceiling => System.Math.Ceiling(raw),
                ResourcePriceRounding.Nearest => System.Math.Round(raw, MidpointRounding.AwayFromZero),
                _ => throw new InvalidOperationException($"Unsupported price rounding: {pricing.Rounding}")
            };
            costs.Add(new ResourceAmount { ResourceId = baseCost.ResourceId, Amount = (float)final });
            breakdowns[baseCost.ResourceId] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["base"] = baseCost.Amount,
                ["baseMultiplier"] = pricing.BaseMultiplier,
                ["rarityMultiplier"] = rarityMultiplier,
                ["tagMultiplier"] = tagMultiplier,
                ["raw"] = raw,
                ["final"] = final
            };
        }

        return new CalculatedShopPrices(baseCosts, costs.MoveToImmutable(), breakdowns);
    }

    private sealed record CalculatedShopPrices(
        ImmutableArray<ResourceAmount> BaseCosts,
        ImmutableArray<ResourceAmount> Costs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Breakdowns);
}
