using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.Run.Content;

public sealed record CardOfferResult
{
    private ImmutableArray<CardContentDefinition> _cards = [];

    public IReadOnlyList<CardContentDefinition> Cards
    {
        get => _cards;
        init => _cards = value?.ToImmutableArray() ?? [];
    }
    public DeterministicContext Context { get; init; } =
        DeterministicContext.Create(0, "unbound");
    public string Fingerprint { get; init; } = string.Empty;
}

/// <summary>
/// Pure weighted selection over a pinned card pool. Candidate order is
/// canonicalized before consuming deterministic entropy, so filesystem,
/// dictionary and JSON declaration order cannot influence an offer.
/// </summary>
public static class CardOfferResolver
{
    public static Result<CardOfferResult> Resolve(
        CardPoolResult pool,
        int count,
        DeterministicContext context,
        IReadOnlySet<string>? excludedCardIds = null)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(context);
        if (count < 0)
            return Result<CardOfferResult>.Failure("Card offer count cannot be negative");
        if (pool.RarityWeights.Any(pair => pair.Value < 0))
            return Result<CardOfferResult>.Failure("Card rarity weights cannot be negative");

        var duplicate = pool.Cards
            .GroupBy(card => card.CardId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            return Result<CardOfferResult>.Failure(
                $"Card pool {pool.PoolId} contains duplicate card {duplicate.Key}");

        var excluded = excludedCardIds ?? new HashSet<string>(StringComparer.Ordinal);
        var remaining = pool.Cards
            .Where(card => !excluded.Contains(card.CardId))
            .Select(card => new WeightedCard(card, ResolveWeight(pool, card)))
            .Where(candidate => candidate.Weight > 0)
            .OrderBy(candidate => candidate.Card.CardId, StringComparer.Ordinal)
            .ToList();
        var selected = ImmutableArray.CreateBuilder<CardContentDefinition>(
            System.Math.Min(count, remaining.Count));
        var current = context;

        while (selected.Count < count && remaining.Count > 0)
        {
            var totalWeight = remaining.Sum(candidate => (long)candidate.Weight);
            if (totalWeight <= 0 || totalWeight > int.MaxValue)
            {
                return Result<CardOfferResult>.Failure(
                    $"Card pool {pool.PoolId} has an invalid total offer weight: {totalWeight}");
            }

            var roll = current.DrawInt32((int)totalWeight);
            current = roll.Context;
            long cursor = roll.Value;
            var selectedIndex = 0;
            for (; selectedIndex < remaining.Count; selectedIndex++)
            {
                cursor -= remaining[selectedIndex].Weight;
                if (cursor < 0)
                    break;
            }

            selected.Add(remaining[selectedIndex].Card);
            remaining.RemoveAt(selectedIndex);
        }

        var cards = selected.MoveToImmutable();
        return Result<CardOfferResult>.Success(new CardOfferResult
        {
            Cards = cards,
            Context = current,
            Fingerprint = CanonicalJson.ComputeHash(new
            {
                pool.PoolId,
                requestedCount = count,
                excluded = excluded.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                cards = cards.Select(card => card.CardId).ToArray(),
                startStep = context.Step,
                finalStep = current.Step,
                startRandomState = context.RandomState,
                finalRandomState = current.RandomState
            })
        });
    }

    private static int ResolveWeight(CardPoolResult pool, CardContentDefinition card) =>
        pool.RarityWeights.Count == 0
            ? 1
            : pool.RarityWeights.GetValueOrDefault(card.Rarity);

    private sealed record WeightedCard(CardContentDefinition Card, int Weight);
}
