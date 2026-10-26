using Core.Caching;
using Core.Common;
using Core.Content;
using Core.Determinism;

namespace Core.Run.Content;

/// <summary>Bounded, striped single-flight. Runtime identity is immutable, not merely a revision label.</summary>
public sealed class CachedCardContentCompiler : ICardContentCompiler, ICacheService
{
    private sealed record Key(ContentRuntime Runtime, string CardId);
    private readonly LruCache<Key, CompiledCardDefinition> _cache = new(256);
    private readonly CardContentCompiler _compiler = new();
    private readonly object[] _stripes = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();
    private int _generation;
    public string CacheName => "CompiledCardContainers";
    public CacheLayer Layer => CacheLayer.Derived;
    public void Invalidate(string? key = null) { Interlocked.Increment(ref _generation); _cache.Clear(); }
    public CacheServiceStats GetStats() => new LruCacheService<Key, CompiledCardDefinition>(_cache, CacheName).GetStats();
    public Result<CompiledCardDefinition> Compile(CardContentDefinition card,
        IReadOnlyDictionary<string, CardComponentBundleDefinition>? bundles = null) => _compiler.Compile(card, bundles);
    public Result<CompiledCardDefinition> Compile(string cardId, ContentRuntime runtime)
    {
        var key = new Key(runtime, cardId);
        if (_cache.TryGetValue(key, out var cached)) return Result<CompiledCardDefinition>.Success(cached!);
        lock (_stripes[(key.GetHashCode() & int.MaxValue) % _stripes.Length])
        {
            if (_cache.TryGetValue(key, out cached)) return Result<CompiledCardDefinition>.Success(cached!);
            var generation = Volatile.Read(ref _generation);
            var result = _compiler.Compile(cardId, runtime);
            if (result.IsSuccess && generation == Volatile.Read(ref _generation)) _cache.Set(key, result.Value);
            return result;
        }
    }
}

/// <summary>Composition depends on definition plus complete immutable instance, never contextual actor stats.</summary>
public sealed class CachedEffectiveCardResolver : IEffectiveCardResolver, ICacheService
{
    private readonly LruCache<string, EffectiveCardDefinition> _cache = new(128);
    private readonly EffectiveCardResolver _resolver = new();
    private readonly object[] _stripes = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();
    private int _generation;
    public string CacheName => "ComposedCardContainers";
    public CacheLayer Layer => CacheLayer.Derived;
    public void Invalidate(string? key = null) { Interlocked.Increment(ref _generation); _cache.Clear(); }
    public CacheServiceStats GetStats() => new LruCacheService<string, EffectiveCardDefinition>(_cache, CacheName).GetStats();
    public Result ValidateUpgrade(CompiledCardDefinition definition, CardUpgradeDefinition upgrade) => _resolver.ValidateUpgrade(definition, upgrade);
    public Result<EffectiveCardDefinition> ResolveWithContributions(CompiledCardDefinition definition, CardInstanceState instance,
        IReadOnlyList<CardComponentContribution> contributions)
    {
        if (contributions.Count == 0) return Resolve(definition, instance);
        var ordered = contributions.OrderBy(c => c.Priority).ThenBy(c => c.SourceId, StringComparer.Ordinal).ToArray();
        var key = CanonicalJson.ComputeHash(definition) + ":" + CanonicalJson.ComputeHash(instance) + ":" + CanonicalJson.ComputeHash(ordered);
        if (_cache.TryGetValue(key, out var cached)) return Result<EffectiveCardDefinition>.Success(cached!);
        lock (_stripes[(key.GetHashCode() & int.MaxValue) % _stripes.Length])
        {
            if (_cache.TryGetValue(key, out cached)) return Result<EffectiveCardDefinition>.Success(cached!);
            var generation = Volatile.Read(ref _generation);
            var result = _resolver.ResolveWithContributions(definition, instance, ordered);
            if (result.IsSuccess && generation == Volatile.Read(ref _generation)) _cache.Set(key, result.Value);
            return result;
        }
    }
    public Result<EffectiveCardDefinition> Resolve(CompiledCardDefinition definition, CardInstanceState instance)
    {
        var key = CanonicalJson.ComputeHash(definition) + ":" + CanonicalJson.ComputeHash(instance);
        if (_cache.TryGetValue(key, out var cached)) return Result<EffectiveCardDefinition>.Success(cached!);
        lock (_stripes[(key.GetHashCode() & int.MaxValue) % _stripes.Length])
        {
            if (_cache.TryGetValue(key, out cached)) return Result<EffectiveCardDefinition>.Success(cached!);
            var generation = Volatile.Read(ref _generation);
            var result = _resolver.Resolve(definition, instance);
            if (result.IsSuccess && generation == Volatile.Read(ref _generation)) _cache.Set(key, result.Value);
            return result;
        }
    }
}
