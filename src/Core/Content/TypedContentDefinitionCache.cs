using System.Runtime.CompilerServices;
using Core.Caching;
using Core.Common;
using Core.Determinism;

namespace Core.Content;

/// <summary>Derived typed views of a captured runtime, never a live-content authority.</summary>
public sealed class TypedContentDefinitionCache : ICacheService
{
    private sealed class Key(ContentRuntime runtime, Type type, string kind, string id) : IEquatable<Key>
    {
        private ContentRuntime Runtime { get; } = runtime;
        private Type Type { get; } = type;
        private string Kind { get; } = kind;
        private string Id { get; } = id;
        public bool Equals(Key? other) => other != null && ReferenceEquals(Runtime, other.Runtime) &&
            Type == other.Type && StringComparer.OrdinalIgnoreCase.Equals(Kind, other.Kind) &&
            StringComparer.Ordinal.Equals(Id, other.Id);
        public override bool Equals(object? obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Runtime), Type,
            StringComparer.OrdinalIgnoreCase.GetHashCode(Kind), StringComparer.Ordinal.GetHashCode(Id));
    }
    private readonly LruCache<Key, object>? _cache;
    private readonly object[] _stripes = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();
    private readonly object _invalidationGate = new();
    private int _generation;
    public TypedContentDefinitionCache(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        if (capacity > 0) _cache = new(capacity);
    }
    public string CacheName => "TypedRevisionedDefinitions";
    public CacheLayer Layer => CacheLayer.Derived;
    public void Invalidate(string? key = null)
    { lock (_invalidationGate) { _generation++; _cache?.Clear(); } }
    public CacheServiceStats GetStats() => _cache == null ? new() { CacheName = CacheName, Capacity = 0 } :
        new LruCacheService<Key, object>(_cache, CacheName).GetStats();

    internal Result<T> GetOrCreate<T>(ContentRuntime runtime, string kind, string id, Func<Result<T>> factory)
    {
        // Mutable DTOs and unknown types always retain independent deserialization.
        if (_cache == null || !typeof(ICanonicalHashMemoizable).IsAssignableFrom(typeof(T))) return factory();
        var key = new Key(runtime, typeof(T), kind, id);
        if (_cache.TryGetValue(key, out var known)) return Result<T>.Success((T)known!);
        lock (_stripes[(key.GetHashCode() & int.MaxValue) % _stripes.Length])
        {
            if (_cache.TryGetValue(key, out known)) return Result<T>.Success((T)known!);
            var generation = Volatile.Read(ref _generation);
            var result = factory();
            lock (_invalidationGate)
                if (result.IsSuccess && result.Value is ICanonicalHashMemoizable && generation == _generation)
                    _cache.Set(key, result.Value);
            return result;
        }
    }
}
