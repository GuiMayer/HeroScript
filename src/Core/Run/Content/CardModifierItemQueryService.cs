using System.Collections.Immutable;
using Core.CardZones;
using Core.Common;
using Core.Content;
using Core.Caching;
using Core.Determinism;

namespace Core.Run.Content;

public sealed record CardModifierItemCompatibility(Guid CardInstanceId, bool Compatible, string? Reason);
public sealed record CardModifierItemView(CardModifierItemInstance Instance, CardModifierItemDefinition Definition,
    ImmutableArray<CardModifierItemCompatibility> Cards);
public sealed record CardModifierItemSnapshot(Guid RunId, int RunSequence, string ContentRevision,
    ImmutableArray<CardModifierItemDefinition> Catalog, ImmutableArray<CardModifierItemView> Items);
public interface ICardModifierItemQueryService
{
    Result<CardModifierItemSnapshot> Read(Guid runId);
}

/// <summary>Ownership and compatibility facts. Reuses the ledger transition and effective composer; no rule evaluation in clients.</summary>
public sealed class CardModifierItemQueryService(IRunQueryService runs, IContentRuntimeResolver runtimes,
    ICardContentCompiler compiler, IEffectiveCardResolver composer) : ICardModifierItemQueryService, ICacheService
{
    private readonly LruCache<string, CardModifierItemSnapshot> _cache = new(32);
    public string CacheName => "RunCardModifierItems";
    public CacheLayer Layer => CacheLayer.Derived;
    public void Invalidate(string? key = null) { if (key == null) _cache.Clear(); else _cache.Remove(key); }
    public CacheServiceStats GetStats() => new LruCacheService<string, CardModifierItemSnapshot>(_cache, CacheName).GetStats();
    public Result<CardModifierItemSnapshot> Read(Guid runId)
    {
        var loaded = runs.GetRun(runId);
        if (loaded.IsFailure) return Result<CardModifierItemSnapshot>.Failure(loaded.Error);
        var run = loaded.Value;
        var key = CanonicalJson.ComputeHash(run);
        if (_cache.TryGetValue(key, out var cached)) return Result<CardModifierItemSnapshot>.Success(cached!);
        var active = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (active.IsFailure) return Result<CardModifierItemSnapshot>.Failure(active.Error);
        var catalog = ImmutableArray.CreateBuilder<CardModifierItemDefinition>();
        foreach (var id in active.Value.GetDefinitions("card-modifier-items").Keys.Order(StringComparer.Ordinal))
        {
            var definition = active.Value.GetDefinition<CardModifierItemDefinition>("card-modifier-items", id);
            if (definition.IsFailure) return Result<CardModifierItemSnapshot>.Failure(definition.Error);
            catalog.Add(definition.Value);
        }
        var inventory = run.CardModifierItems ?? new();
        var valid = inventory.Validate();
        if (valid.IsFailure) return Result<CardModifierItemSnapshot>.Failure(valid.Error);
        var visible = CardZoneReadModel.Project(run).Zones.SelectMany(zone => zone.Cards)
            .Select(card => card.CardInstanceId).Distinct().Order().ToArray();
        var items = ImmutableArray.CreateBuilder<CardModifierItemView>();
        foreach (var instance in inventory.Items)
        {
            var acquired = runtimes.Resolve(instance.AcquisitionRevision, run.ConfigName);
            if (acquired.IsFailure) return Result<CardModifierItemSnapshot>.Failure(acquired.Error);
            var definition = acquired.Value.GetDefinition<CardModifierItemDefinition>("card-modifier-items", instance.ItemDefinitionId);
            if (definition.IsFailure) return Result<CardModifierItemSnapshot>.Failure(definition.Error);
            var cards = ImmutableArray.CreateBuilder<CardModifierItemCompatibility>();
            foreach (var id in visible)
            {
                var planned = new CardModifierItemApplicationPlanner(runtimes, compiler, composer).Plan(run, instance.ItemInstanceId, id);
                cards.Add(new(id, planned.IsSuccess, planned.IsFailure ? planned.Error : null));
            }
            items.Add(new(instance, definition.Value, cards.ToImmutable()));
        }
        var snapshot = new CardModifierItemSnapshot(run.RunId, run.Sequence, run.Determinism.ContentRevision, catalog.ToImmutable(), items.ToImmutable());
        _cache.Set(key, snapshot);
        return Result<CardModifierItemSnapshot>.Success(snapshot);
    }
}
