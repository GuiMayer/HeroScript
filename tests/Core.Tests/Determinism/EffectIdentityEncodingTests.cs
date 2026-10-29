using Core.Determinism;
using Core.Effects;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.Determinism;

public sealed class EffectIdentityEncodingTests
{
    [Fact]
    public void ComposedExecutionIdentityMatchesTheOriginalEmbeddedObjectIncludingNullsAndOrder()
    {
        using var hashes = CanonicalJson.BeginHashScope();
        for (var variant = 0; variant < 20; variant++)
        {
            var request = EffectTransactionTests.Request() with { ContentRevision = "revision-" + variant,
                Tags = System.Collections.Immutable.ImmutableHashSet.Create(StringComparer.Ordinal, "z", "a"),
                SelectedTargetEntityIds = variant % 2 == 0 ? ["hero", "enemy"] : ["enemy", "hero"] };
            var expected = CanonicalJson.ComputeHash(new {
                state = CanonicalJson.ComputeHash(request.Combat), run = request.Run == null ? null : CanonicalJson.ComputeHash(request.Run),
                request.Trigger, request.Components, request.PrefixCommands, request.Provenance, request.ContentRevision,
                request.OwnerEntityId, request.SourceEntityId, request.Variables,
                tags = request.Tags.OrderBy(tag => tag, StringComparer.Ordinal).ToArray(), card = request.Card?.Fingerprint,
                request.StackPayloadLots, request.Quantities, targets = request.SelectedTargetEntityIds });
            Assert.Equal(expected, EffectResultContext.ExecutionId(request));
        }
    }
}
