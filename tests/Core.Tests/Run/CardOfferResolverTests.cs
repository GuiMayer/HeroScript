using Core.Determinism;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardOfferResolverTests
{
    [Fact]
    public void Resolve_SamePinnedInputs_ProducesSameOfferAndFingerprint()
    {
        var pool = Pool(
            Card("rare", CardRarity.Rare),
            Card("common_b", CardRarity.Common),
            Card("common_a", CardRarity.Common),
            Card("uncommon", CardRarity.Uncommon));
        var context = DeterministicContext.Create(451UL, new string('a', 64));

        var first = CardOfferResolver.Resolve(pool, 3, context);
        var second = CardOfferResolver.Resolve(pool with { Cards = pool.Cards.Reverse().ToArray() }, 3, context);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(
            first.Value.Cards.Select(card => card.CardId),
            second.Value.Cards.Select(card => card.CardId));
        Assert.Equal(first.Value.Fingerprint, second.Value.Fingerprint);
        Assert.Equal(first.Value.Context, second.Value.Context);
        Assert.NotEqual(context.RandomState, first.Value.Context.RandomState);
    }

    [Fact]
    public void Resolve_ZeroWeightAndExplicitExclusion_CannotEnterOffer()
    {
        var pool = Pool(
            Card("common", CardRarity.Common),
            Card("rare", CardRarity.Rare),
            Card("uncommon", CardRarity.Uncommon)) with
        {
            RarityWeights = new Dictionary<CardRarity, int>
            {
                [CardRarity.Common] = 0,
                [CardRarity.Uncommon] = 10,
                [CardRarity.Rare] = 100
            }
        };

        var result = CardOfferResolver.Resolve(
            pool,
            3,
            DeterministicContext.Create(9UL, new string('b', 64)),
            new HashSet<string>(["rare"], StringComparer.Ordinal));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["uncommon"], result.Value.Cards.Select(card => card.CardId));
    }

    [Fact]
    public void Resolve_DoesNotMutatePoolOrStartingContext()
    {
        var pool = Pool(Card("a", CardRarity.Common), Card("b", CardRarity.Common));
        var originalCards = pool.Cards.ToArray();
        var context = DeterministicContext.Create(12UL, new string('c', 64));

        var result = CardOfferResolver.Resolve(pool, 1, context);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(originalCards, pool.Cards);
        Assert.Equal(0UL, context.Step);
        Assert.NotEqual(context.RandomState, result.Value.Context.RandomState);
    }

    private static CardPoolResult Pool(params CardContentDefinition[] cards) => new()
    {
        PoolId = "test",
        Cards = cards,
        RarityWeights = new Dictionary<CardRarity, int>
        {
            [CardRarity.Common] = 70,
            [CardRarity.Uncommon] = 25,
            [CardRarity.Rare] = 5
        }
    };

    private static CardContentDefinition Card(string id, CardRarity rarity) => new()
    {
        CardId = id,
        Rarity = rarity
    };
}
