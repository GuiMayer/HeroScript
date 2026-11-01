using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Core.Determinism;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.Determinism;

public sealed class FrozenFrameEncodingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrameCompositionPreservesReferenceBytesAndOrder(bool snapshots)
    {
        using var scope = CanonicalJson.BeginHashScope();
        var combat = EffectTransactionTests.Request().Combat;
        for (var index = 0; index < 20; index++)
        {
            var frames = ImmutableArray.Create(new CombatAnimationFrame
            {
                FrameId = Guid.Parse("9a4a1269-a313-438a-a3c1-59994468e001"),
                Index = index, RunSequence = index + 1, TransitionType = "test",
                Payload = JsonSerializer.SerializeToElement(new { amount = index, names = new[] { "b", "a" } }),
                StateAfter = snapshots ? combat : null
            });
            Assert.Equal(CanonicalJson.Serialize(frames), Encoding.UTF8.GetString(CanonicalJson.FrozenArrayProperty("frames", frames).Bytes.Span));
            Assert.Equal(CanonicalJson.ComputeHash(new { frames, type = "test" }),
                CanonicalJson.ComputeObjectHash(CanonicalJson.FrozenArrayProperty("frames", frames), CanonicalJson.Property("type", "test")));
        }
    }
}
