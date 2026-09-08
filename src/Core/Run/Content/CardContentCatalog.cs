using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Caching;

namespace Core.Run.Content;

public sealed class CardContentCatalog : ICardContentCatalog, IRevisionedCardContentCatalog, ICacheService
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, CardContentDefinition>> _cardsByConfig = new(StringComparer.OrdinalIgnoreCase);
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private long _hits;
    private long _misses;
    private DateTime? _lastInvalidation;

    public string CacheName => "Definitions:cards";
    public CacheLayer Layer => CacheLayer.Definition;

    public CardContentCatalog(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _contentRuntimes = contentRuntimes;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public Result<CardContentDefinition> GetCard(string cardId, string configName = "default")
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return Result<CardContentDefinition>.Failure("CardId cannot be empty");

        var cardsResult = GetCards(configName);
        if (cardsResult.IsFailure)
            return Result<CardContentDefinition>.Failure(cardsResult.Error);

        return cardsResult.Value.TryGetValue(cardId, out var card)
            ? Result<CardContentDefinition>.Success(card)
            : Result<CardContentDefinition>.Failure($"Card content definition not found: {cardId}");
    }

    public Result<IReadOnlyList<CardContentDefinition>> GetAllCards(string configName = "default")
    {
        var cardsResult = GetCards(configName);
        return cardsResult.IsSuccess
            ? Result<IReadOnlyList<CardContentDefinition>>.Success(cardsResult.Value.Values.ToList())
            : Result<IReadOnlyList<CardContentDefinition>>.Failure(cardsResult.Error);
    }

    public Result<CardContentDefinition> GetCard(
        string cardId,
        string contentRevision,
        string? configName = null)
    {
        if (_contentRuntimes == null)
            return Result<CardContentDefinition>.Failure("Revisioned card runtime is not configured");

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<CardContentDefinition>.Failure(runtime.Error);
        var definition = runtime.Value.GetDefinition<CardContentDefinition>("cards", cardId);
        return definition;
    }

    public Result<IReadOnlyList<CardContentDefinition>> GetAllCards(
        string contentRevision,
        string? configName = null)
    {
        if (_contentRuntimes == null)
            return Result<IReadOnlyList<CardContentDefinition>>.Failure("Revisioned card runtime is not configured");

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<IReadOnlyList<CardContentDefinition>>.Failure(runtime.Error);

        var cards = new List<CardContentDefinition>();
        foreach (var cardId in runtime.Value.GetDefinitions("cards").Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            var definition = runtime.Value.GetDefinition<CardContentDefinition>("cards", cardId);
            if (definition.IsFailure)
                return Result<IReadOnlyList<CardContentDefinition>>.Failure(definition.Error);
            cards.Add(definition.Value);
        }
        return Result<IReadOnlyList<CardContentDefinition>>.Success(cards);
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _cardsByConfig.Clear();
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
                Count = _cardsByConfig.Count,
                Hits = _hits,
                Misses = _misses,
                HitRate = requests == 0 ? 0 : (double)_hits / requests,
                LastInvalidation = _lastInvalidation
            };
        }
    }

    private Result<IReadOnlyDictionary<string, CardContentDefinition>> GetCards(string configName)
    {
        lock (_lock)
        {
            if (_cardsByConfig.TryGetValue(configName, out var cached))
            {
                _hits++;
                return Result<IReadOnlyDictionary<string, CardContentDefinition>>.Success(cached);
            }
            _misses++;

            try
            {
                var chain = _configManager.ResolveInheritanceChain(configName);
                var resources = _resourceLoader.LoadResource("cards/card_catalog.json", chain, strictMode: true);
                if (resources.Count == 0)
                    return Result<IReadOnlyDictionary<string, CardContentDefinition>>.Failure("Card catalog not found: cards/card_catalog.json");

                var cards = new Dictionary<string, CardContentDefinition>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, element) in resources)
                {
                    var definition = JsonSerializer.Deserialize<CardContentDefinition>(element.GetRawText(), _jsonOptions);
                    if (definition == null)
                        return Result<IReadOnlyDictionary<string, CardContentDefinition>>.Failure($"Failed to deserialize card content definition: {key}");

                    if (!string.Equals(definition.CardId, key, StringComparison.Ordinal))
                    {
                        return Result<IReadOnlyDictionary<string, CardContentDefinition>>.Failure(
                            $"Card definition identity mismatch: expected {key}, got {definition.CardId}");
                    }
                    cards[key] = definition;
                }

                _cardsByConfig[configName] = cards;
                return Result<IReadOnlyDictionary<string, CardContentDefinition>>.Success(cards);
            }
            catch (Exception ex)
            {
                return Result<IReadOnlyDictionary<string, CardContentDefinition>>.Failure($"Failed to load card catalog: {ex.Message}");
            }
        }
    }

}
