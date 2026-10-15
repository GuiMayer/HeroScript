using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class CachedCardCompilationTests
{
    private static ContentRuntime Runtime(float value) => ContentRuntime.Create(new()
    {
        Manifest = new() { Revision = "same-label", ConfigName = "test", Artifacts = [new()
            { Kind = "cards", Path = "cards/test.json", DefinitionCount = 1 }] },
        Artifacts = ImmutableDictionary<string, JsonElement>.Empty.Add("cards/test.json", JsonSerializer.SerializeToElement(new
            Dictionary<string, CardContentDefinition> { ["strike"] = new() { CardId = "strike", Components = [new CardEffectComponentDefinition
            { ComponentId = "damage", Effect = new() { Type = EffectType.DAMAGE, TargetResource = "health", FlatValue = value } }] } }))
    }).Value;
    [Fact]
    public void CompiledCacheSeparatesRuntimeIdentityAndSurvivesParallelReadAndInvalidation()
    {
        var cache = new CachedCardContentCompiler(); var a = Runtime(6); var b = Runtime(12);
        var first = cache.Compile("strike", a).Value;
        Parallel.For(0, 24, _ => Assert.Same(first, cache.Compile("strike", a).Value));
        Assert.NotEqual(first.Fingerprint, cache.Compile("strike", b).Value.Fingerprint);
        Assert.Equal(2, cache.GetStats().Count);
        cache.Invalidate();
        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(cache.Compile("strike", a).Value));
        Assert.True(cache.GetStats().Hits > 0);
    }
    [Fact]
    public void CompositionCacheUsesCompleteInstanceAndDoesNotMutateBase()
    {
        var definition = new CardContentCompiler().Compile("strike", Runtime(6)).Value;
        var instance = new CardInstanceState { CardInstanceId = Guid.NewGuid(), DefinitionId = "strike" };
        var cache = new CachedEffectiveCardResolver();
        var original = cache.Resolve(definition, instance).Value;
        var changed = instance with { Upgrades = [new() { UpgradeId = "boost", TransformationId = 1, ContentRevision = "revision",
            Patches = [new CardEffectNumericPatchDefinition { ComponentId = "damage", Attribute = CardEffectNumericAttribute.FlatValue,
                Operation = CardNumericPatchOperation.Add, Value = 3 }] }] };
        Assert.Same(original, cache.Resolve(definition, instance).Value);
        var upgraded = cache.Resolve(definition, changed).Value;
        Assert.Equal(9, upgraded.All<CardEffectComponentDefinition>()[0].Effect.FlatValue);
        Assert.Equal(6, original.All<CardEffectComponentDefinition>()[0].Effect.FlatValue);
        cache.Invalidate();
        Assert.Equal(CanonicalJson.ComputeHash(upgraded), CanonicalJson.ComputeHash(cache.Resolve(definition, changed).Value));
    }
}
