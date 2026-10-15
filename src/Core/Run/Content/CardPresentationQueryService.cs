using System.Collections.Immutable;
using Core.CardZones;
using Core.Common;
using Core.Content;
using Core.Caching;
using Core.Determinism;

namespace Core.Run.Content;

public sealed record CardPresentationBatch(Guid RunId, int RunSequence, string ContentRevision,
    ImmutableArray<CardPresentationFacts> Cards);

public interface ICardPresentationQueryService
{
    Result<CardPresentationBatch> Read(Guid runId);
    Result<CardPresentationBatch> Read(RunState snapshot, bool playableOnly = false) =>
        Result<CardPresentationBatch>.Failure("Captured card presentation queries are unavailable");
}

/// <summary>Projects only visible instances and current offers from one captured, revision-pinned run.</summary>
public sealed class CardPresentationQueryService(IRunQueryService runs, IContentRuntimeResolver runtimes,
    ICardContentCompiler compiler, IEffectiveCardResolver effectiveCards) : ICardPresentationQueryService, ICacheService
{
    public const int MaximumCards = 512;
    private readonly LruCache<string, CardPresentationBatch> _cache = new(64);
    public string CacheName => "RunCardPresentations";
    public CacheLayer Layer => CacheLayer.Derived;
    public void Invalidate(string? key = null) { if (key == null) _cache.Clear(); else _cache.Remove(key); }
    public CacheServiceStats GetStats() => new LruCacheService<string, CardPresentationBatch>(_cache, CacheName).GetStats();

    public Result<CardPresentationBatch> Read(Guid runId)
    {
        var loaded = runs.GetRun(runId);
        if (loaded.IsFailure) return Result<CardPresentationBatch>.Failure(loaded.Error);
        return Read(loaded.Value);
    }

    public Result<CardPresentationBatch> Read(RunState run, bool playableOnly = false)
    {
        var key = CanonicalJson.ComputeHash(run) + (playableOnly ? ":playable" : ":visible");
        if (_cache.TryGetValue(key, out var cached)) return Result<CardPresentationBatch>.Success(cached!);
        var resolved = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (resolved.IsFailure) return Result<CardPresentationBatch>.Failure(resolved.Error);
        var runtime = resolved.Value;
        var subjects = new List<(CardPresentationIdentity Identity, CardInstanceState? Instance)>();
        foreach (var id in CardZoneReadModel.Project(run).Zones.Where(zone => !playableOnly || zone.AllowsCardPlay).SelectMany(zone => zone.Cards)
                     .Select(card => card.CardInstanceId).Distinct().Order())
        {
            var instance = run.Deck.GetCard(id)!;
            subjects.Add((new() { Kind = CardPresentationSubjectKind.Instance,
                DefinitionId = instance.DefinitionId, CardInstanceId = id }, instance));
        }
        foreach (var selection in run.CardSelections.Where(value => value.NodeId == run.CurrentNodeId && !value.Completed))
            foreach (var (option, index) in selection.Options.Select((option, index) => (option, index)).Where(value => !value.option.Decomposed))
                subjects.Add((new() { Kind = CardPresentationSubjectKind.Offer, DefinitionId = option.CardId,
                    OfferId = $"selection:{selection.SelectionInstanceId}:{selection.OfferFingerprint}:{index}" }, null));
        foreach (var shop in run.Shops.Where(value => value.NodeId == run.CurrentNodeId))
            foreach (var item in shop.Items.Where(value => !value.Purchased && !string.IsNullOrWhiteSpace(value.CardId)))
                subjects.Add((new() { Kind = CardPresentationSubjectKind.Offer, DefinitionId = item.CardId!,
                    OfferId = $"shop:{shop.ShopInstanceId}:{shop.OfferFingerprint}:{item.ItemId}" }, null));
        if (subjects.Count > MaximumCards)
            return Result<CardPresentationBatch>.Failure("Card presentation batch exceeds the supported limit; no partial snapshot was returned");
        var cards = ImmutableArray.CreateBuilder<CardPresentationFacts>();
        var definitions = new Dictionary<string, CompiledCardDefinition>(StringComparer.Ordinal);
        foreach (var subject in subjects)
        {
            if (!definitions.TryGetValue(subject.Identity.DefinitionId, out var definition))
            {
                var compiled = compiler.Compile(subject.Identity.DefinitionId, runtime);
                if (compiled.IsFailure) return Result<CardPresentationBatch>.Failure(compiled.Error);
                definitions.Add(subject.Identity.DefinitionId, definition = compiled.Value);
            }
            var composed = subject.Instance == null
                ? Result<EffectiveCardDefinition>.Success(CardBasePresentationProjector.DefinitionBase(definition))
                : effectiveCards.Resolve(definition, subject.Instance);
            if (composed.IsFailure) return Result<CardPresentationBatch>.Failure(composed.Error);
            var facts = CardBasePresentationProjector.Project(definition, composed.Value, subject.Identity,
                new() { RunId = run.RunId, RunSequence = run.Sequence, ContentRevision = run.Determinism.ContentRevision });
            if (facts.IsFailure) return Result<CardPresentationBatch>.Failure(facts.Error);
            cards.Add(facts.Value);
        }
        var batch = new CardPresentationBatch(run.RunId, run.Sequence, run.Determinism.ContentRevision, cards.ToImmutable());
        _cache.Set(key, batch);
        return Result<CardPresentationBatch>.Success(batch);
    }
}
