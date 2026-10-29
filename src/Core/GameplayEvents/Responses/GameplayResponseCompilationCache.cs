using System.Runtime.CompilerServices;
using Core.Common;

namespace Core.GameplayEvents.Responses;

/// <summary>Only immutable compilation products. Catalogue lifetime pins setting/revision; no RNG, eligibility or quotas.</summary>
internal static class GameplayResponseCompilationCache
{
    internal const int Capacity = 4096;
    private static readonly ConditionalWeakTable<GameplayEventCatalog, Cache> Catalogs = new();
    private sealed record Key(GameplayResponseDefinition Definition, bool Boundary, bool Empty, int Effects, bool Extensions, string Frequencies);
    private sealed class Cache
    {
        internal readonly Dictionary<Key, Result<CompiledGameplayResponse>> Entries = new();
    }

    internal static Result<CompiledGameplayResponse> Get(GameplayResponseDefinition definition, GameplayEventCatalog catalog,
        GameplayResponseCapabilities capabilities, Func<Result<CompiledGameplayResponse>> compile)
    {
        var cache = Catalogs.GetOrCreateValue(catalog);
        var key = new Key(definition, capabilities.ContainerBoundary, capabilities.EmptyEffects, capabilities.MaximumEffects,
            capabilities.ExtensionTopics, string.Join(",", capabilities.Frequencies.Order()));
        lock (cache)
        {
            if (cache.Entries.TryGetValue(key, out var found)) return found;
            var result = compile();
            if (cache.Entries.Count == Capacity) cache.Entries.Clear();
            cache.Entries.Add(key, result);
            return result;
        }
    }
}
