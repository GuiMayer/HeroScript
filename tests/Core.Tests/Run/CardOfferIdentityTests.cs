using Core.Determinism;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardOfferIdentityTests
{
    [Fact]
    public void RepeatedDefinitionCannotProduceAmbiguousRewardOptionsOrConsumeEntropy()
    {
        var context = DeterministicContext.Create(12, "offer-identity");
        var card = new CardContentDefinition { CardId = "repeated", Rarity = CardRarity.Common };
        var pool = new CardPoolResult { PoolId = "ambiguous", Cards = [card, card] };
        var before = CanonicalJson.ComputeHash(new { pool, context });
        var result = CardOfferResolver.Resolve(pool, 2, context);
        Assert.True(result.IsFailure);
        Assert.Contains("duplicate card", result.Error);
        Assert.Equal(before, CanonicalJson.ComputeHash(new { pool, context }));
        Assert.Equal(0UL, context.IdSequence);
    }
}
