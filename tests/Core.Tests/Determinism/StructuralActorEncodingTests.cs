using System.Text;
using System.Text.Json;
using Core.Determinism;
using Core.Resources;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.Determinism;

public sealed class StructuralActorEncodingTests
{
    [Fact]
    public void ChangingOneActorPreservesCanonicalBytesAndSharedPolymorphicComponents()
    {
        var combat = EffectTransactionTests.Request().Combat;
        var original = CanonicalJson.Serialize(combat);
        using var hashes = CanonicalJson.BeginHashScope();
        for (var index = 0; index < 100; index++)
        {
            var hero = combat.GetActor("hero")!;
            var state = hero.ResourceState;
            var pool = state.Resources.First();
            var next = hero.WithResourceState(state with { Resources = state.Resources.ToDictionary(pair => pair.Key,
                pair => pair.Key == pool.Key ? pair.Value with { Current = index } : pair.Value) });
            combat = combat with { Actors = combat.Actors.ToDictionary(pair => pair.Key, pair => pair.Key == "hero" ? next : pair.Value) };
            var expected = CanonicalJson.Serialize(combat);
            Assert.Equal(expected, Encoding.UTF8.GetString(CanonicalJson.Property("combat", combat).Bytes.Span));
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(expected))), CanonicalJson.ComputeHash(combat));
        }
        Assert.NotEqual(original, CanonicalJson.Serialize(combat));
    }
}
