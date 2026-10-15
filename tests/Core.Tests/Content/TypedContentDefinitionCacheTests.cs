using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Xunit;

namespace Core.Tests.Content;

public sealed class TypedContentDefinitionCacheTests
{
    private static ContentRuntime Runtime(TypedContentDefinitionCache cache, string formula = "base", string revision = "same-label")
    {
        var result = ContentRuntime.Create(new()
        {
            Manifest = new() { Revision = revision, ConfigName = "test", Artifacts =
                [new() { Kind = "calculation-pipelines", Path = "calculation-pipelines/test.json" },
                 new() { Kind = "effect-resolution-policies", Path = "effect-resolution-policies/test.json" }] },
            Artifacts = ImmutableDictionary<string, JsonElement>.Empty
                .Add("calculation-pipelines/test.json", JsonSerializer.SerializeToElement(new Dictionary<string, CalculationPipelineDefinition>
                { ["damage"] = new() { PipelineId = "damage", Channel = "damage", Buckets =
                    [new() { BucketId = "base", Operation = CalculationBucketOperation.Formula, Formula = formula }] } }))
                .Add("effect-resolution-policies/test.json", JsonSerializer.SerializeToElement(new Dictionary<string, EffectResolutionPolicyDefinition>
                    { ["global"] = new() { PolicyId = "global" } }))
        }, ContentKindRegistry.Default, cache);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    [Fact]
    public void ConcurrentReadsShareOnlyTheSameImmutableRuntimeTypeKindAndId()
    {
        var cache = new TypedContentDefinitionCache();
        var runtime = Runtime(cache);
        var values = new CalculationPipelineDefinition[32];
        Parallel.For(0, values.Length, i => values[i] = runtime.GetDefinition<CalculationPipelineDefinition>(
            i % 2 == 0 ? "calculation-pipelines" : "CALCULATION-PIPELINES", "damage").Value);
        Assert.All(values, value => Assert.Same(values[0], value));
        Assert.Equal(1, cache.GetStats().Count);
        var different = Runtime(cache, "base + 2");
        Assert.NotEqual(CanonicalJson.ComputeHash(values[0]), CanonicalJson.ComputeHash(
            different.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", "damage").Value));
        Assert.Equal(2, cache.GetStats().Count);
        var count = cache.GetStats().Count;
        Assert.True(runtime.GetDefinition<EffectResolutionPolicyDefinition>("calculation-pipelines", "damage").IsFailure);
        Assert.Equal(count, cache.GetStats().Count); // Invalid type/failure is never cached.
    }

    [Fact]
    public void MutableConsumersRemainIndependentAndCannotPoisonTheRuntime()
    {
        var cache = new TypedContentDefinitionCache(); var runtime = Runtime(cache);
        var a = runtime.GetDefinition<MutablePolicy>("effect-resolution-policies", "global").Value;
        a.PolicyId = "changed";
        var b = runtime.GetDefinition<MutablePolicy>("effect-resolution-policies", "global").Value;
        Assert.NotSame(a, b); Assert.Equal("global", b.PolicyId);
        Assert.Equal(0, cache.GetStats().Count);
        var immutable = runtime.GetDefinition<EffectResolutionPolicyDefinition>("effect-resolution-policies", "global").Value;
        Assert.Equal("global", immutable.PolicyId);
        Assert.Equal(1, cache.GetStats().Count);
    }

    [Fact]
    public void DisabledEvictedAndInvalidatedReadsHaveTheSameCanonicalValue()
    {
        var cache = new TypedContentDefinitionCache(1); var runtime = Runtime(cache);
        var first = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", "damage").Value;
        var expected = CanonicalJson.ComputeHash(first);
        runtime.GetDefinition<EffectResolutionPolicyDefinition>("effect-resolution-policies", "global");
        var evicted = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", "damage").Value;
        Assert.NotSame(first, evicted); Assert.Equal(expected, CanonicalJson.ComputeHash(evicted));
        cache.Invalidate();
        var invalidated = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", "damage").Value;
        Assert.NotSame(evicted, invalidated); Assert.Equal(expected, CanonicalJson.ComputeHash(invalidated));
        var disabled = Runtime(new(0));
        var a = disabled.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", "damage").Value;
        var b = disabled.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", "damage").Value;
        Assert.NotSame(a, b); Assert.Equal(expected, CanonicalJson.ComputeHash(a));
    }

    [Fact]
    public void AuditedDefinitionHashesMatchReferenceAndCopiesDoNotShareAStaleHash()
    {
        var pipeline = Runtime(new()).GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", "damage").Value;
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(CanonicalJson.Serialize(pipeline))));
        Assert.Equal(expected, CanonicalJson.ComputeHash(pipeline));
        Assert.Equal(expected, CanonicalJson.ComputeHash(pipeline with { }));
        Assert.NotEqual(expected, CanonicalJson.ComputeHash(pipeline with { Channel = "other" }));
    }

    [Fact]
    public async Task InvalidationDoesNotPublishAnOlderInFlightGeneration()
    {
        var cache = new TypedContentDefinitionCache(); var runtime = Runtime(cache);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var pending = Task.Run(() => cache.GetOrCreate(runtime, "calculation-pipelines", "damage", () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            return Core.Common.Result<CalculationPipelineDefinition>.Success(new() { PipelineId = "damage" });
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        cache.Invalidate(); release.Set();
        Assert.True((await pending).IsSuccess);
        Assert.Equal(0, cache.GetStats().Count);
        Assert.True(runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", "damage").IsSuccess);
        Assert.Equal(1, cache.GetStats().Count);
    }

    public sealed class MutablePolicy
    {
        public string PolicyId { get; set; } = "";
        public EffectResolutionRuleDefinition[] Rules { get; set; } = [];
        public ActorAttributeProjectionDefinition[] Attributes { get; set; } = [];
    }
}
