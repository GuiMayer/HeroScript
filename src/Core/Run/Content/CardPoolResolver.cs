using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Caching;

namespace Core.Run.Content;

public sealed class CardPoolResolver : ICardPoolResolver, IRevisionedCardPoolResolver, ICacheService
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ICardContentCatalog _catalog;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private readonly Dictionary<string, CardPoolDefinition> _poolsByConfigAndId = new(StringComparer.OrdinalIgnoreCase);
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private long _hits;
    private long _misses;
    private DateTime? _lastInvalidation;

    public string CacheName => "Definitions:card-pools";
    public CacheLayer Layer => CacheLayer.Definition;

    public CardPoolResolver(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ICardContentCatalog catalog,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _contentRuntimes = contentRuntimes;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public Result<CardPoolDefinition> GetPool(string poolId, string configName = "default")
    {
        if (string.IsNullOrWhiteSpace(poolId))
            return Result<CardPoolDefinition>.Failure("PoolId cannot be empty");

        return LoadPool(poolId, configName);
    }

    public Result<CardPoolResult> ResolvePool(string poolId, string configName = "default")
    {
        var poolResult = GetPool(poolId, configName);
        if (poolResult.IsFailure)
            return Result<CardPoolResult>.Failure(poolResult.Error);

        var cardsResult = _catalog.GetAllCards(configName);
        if (cardsResult.IsFailure)
            return Result<CardPoolResult>.Failure(cardsResult.Error);

        var pool = poolResult.Value;
        var includeTags = new HashSet<string>(pool.IncludeTags, StringComparer.OrdinalIgnoreCase);
        var excludeTags = new HashSet<string>(pool.ExcludeTags, StringComparer.OrdinalIgnoreCase);
        var explicitIds = new HashSet<string>(pool.ExplicitCardIds, StringComparer.OrdinalIgnoreCase);

        var cards = cardsResult.Value
            .Where(card => explicitIds.Count == 0 || explicitIds.Contains(card.CardId))
            .Where(card => includeTags.Count == 0 || includeTags.All(tag => card.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .Where(card => excludeTags.Count == 0 || !excludeTags.Any(tag => card.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .Where(card => pool.RarityWeights.Count == 0 || pool.RarityWeights.ContainsKey(card.Rarity))
            .OrderBy(card => card.CardId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<CardPoolResult>.Success(new CardPoolResult
        {
            PoolId = pool.PoolId,
            Cards = cards
        });
    }

    public Result<CardPoolDefinition> GetPool(
        string poolId,
        string contentRevision,
        string? configName = null)
    {
        if (_contentRuntimes == null)
            return GetPool(poolId, configName ?? "default");

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<CardPoolDefinition>.Failure(runtime.Error);
        var definition = runtime.Value.GetDefinition<CardPoolDefinition>("card-pools", poolId);
        return definition.IsFailure
            ? definition
            : Result<CardPoolDefinition>.Success(definition.Value with
            {
                PoolId = string.IsNullOrWhiteSpace(definition.Value.PoolId) ? poolId : definition.Value.PoolId
            });
    }

    public Result<CardPoolResult> ResolvePool(
        string poolId,
        string contentRevision,
        string? configName = null)
    {
        var poolResult = GetPool(poolId, contentRevision, configName);
        if (poolResult.IsFailure)
            return Result<CardPoolResult>.Failure(poolResult.Error);

        var cardsResult = _catalog is IRevisionedCardContentCatalog revisionedCatalog
            ? revisionedCatalog.GetAllCards(contentRevision, configName)
            : _catalog.GetAllCards(configName ?? "default");
        return cardsResult.IsFailure
            ? Result<CardPoolResult>.Failure(cardsResult.Error)
            : ResolvePool(poolResult.Value, cardsResult.Value);
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _poolsByConfigAndId.Clear();
            _lastInvalidation = DateTime.UtcNow; // nondeterministic-boundary: operational telemetry
        }
    }

    void ICacheService.Invalidate(string? key) => Invalidate();

    public CacheServiceStats GetStats()
    {
        lock (_lock)
        {
            var requests = _hits + _misses;
            return new CacheServiceStats
            {
                CacheName = CacheName,
                Capacity = int.MaxValue,
                Count = _poolsByConfigAndId.Count,
                Hits = _hits,
                Misses = _misses,
                HitRate = requests == 0 ? 0 : (double)_hits / requests,
                LastInvalidation = _lastInvalidation
            };
        }
    }

    private Result<CardPoolDefinition> LoadPool(string poolId, string configName)
    {
        lock (_lock)
        {
            var cacheKey = CacheKey(configName, poolId);
            if (_poolsByConfigAndId.TryGetValue(cacheKey, out var cached))
            {
                _hits++;
                return Result<CardPoolDefinition>.Success(cached);
            }
            _misses++;

            try
            {
                var chain = _configManager.ResolveInheritanceChain(configName);
                var relativePath = $"card-pools/{poolId}.json";
                var resources = _resourceLoader.LoadResource(relativePath, chain, strictMode: false);
                if (resources.Count == 0)
                    return Result<CardPoolDefinition>.Failure($"Card pool definition not found: {poolId}");

                var element = resources.TryGetValue(poolId, out var exact)
                    ? exact
                    : resources.Values.First();
                var definition = JsonSerializer.Deserialize<CardPoolDefinition>(element.GetRawText(), _jsonOptions);
                if (definition == null)
                    return Result<CardPoolDefinition>.Failure($"Failed to deserialize card pool definition: {poolId}");

                var resolvedPoolId = string.IsNullOrWhiteSpace(definition.PoolId) ? poolId : definition.PoolId;
                var pool = definition with { PoolId = resolvedPoolId };
                _poolsByConfigAndId[cacheKey] = pool;
                return Result<CardPoolDefinition>.Success(pool);
            }
            catch (Exception ex)
            {
                return Result<CardPoolDefinition>.Failure($"Failed to load card pool '{poolId}': {ex.Message}");
            }
        }
    }

    private static Result<CardPoolResult> ResolvePool(
        CardPoolDefinition pool,
        IReadOnlyList<CardContentDefinition> allCards)
    {
        var includeTags = new HashSet<string>(pool.IncludeTags, StringComparer.OrdinalIgnoreCase);
        var excludeTags = new HashSet<string>(pool.ExcludeTags, StringComparer.OrdinalIgnoreCase);
        var explicitIds = new HashSet<string>(pool.ExplicitCardIds, StringComparer.OrdinalIgnoreCase);
        var cards = allCards
            .Where(card => explicitIds.Count == 0 || explicitIds.Contains(card.CardId))
            .Where(card => includeTags.Count == 0 || includeTags.All(tag => card.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .Where(card => excludeTags.Count == 0 || !excludeTags.Any(tag => card.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .Where(card => pool.RarityWeights.Count == 0 || pool.RarityWeights.ContainsKey(card.Rarity))
            .OrderBy(card => card.CardId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Result<CardPoolResult>.Success(new CardPoolResult { PoolId = pool.PoolId, Cards = cards });
    }

    private static string CacheKey(string configName, string poolId)
    {
        return $"{configName.Trim().ToLowerInvariant()}::{poolId.Trim().ToLowerInvariant()}";
    }
}
