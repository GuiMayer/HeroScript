using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Config;
using Core.Content;

namespace Core.Run.Content;

public sealed class CardContentCatalog : ICardContentCatalog, IRevisionedCardContentCatalog
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, CardContentDefinition>> _cardsByConfig = new(StringComparer.OrdinalIgnoreCase);
    private readonly IContentRuntimeResolver? _contentRuntimes;

    public CardContentCatalog(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _contentRuntimes = contentRuntimes;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
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
            return GetCard(cardId, configName ?? "default");

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<CardContentDefinition>.Failure(runtime.Error);
        var definition = runtime.Value.GetDefinition<CardContentDefinition>("cards", cardId);
        return definition.IsFailure
            ? definition
            : Result<CardContentDefinition>.Success(Normalize(definition.Value, cardId));
    }

    public Result<IReadOnlyList<CardContentDefinition>> GetAllCards(
        string contentRevision,
        string? configName = null)
    {
        if (_contentRuntimes == null)
            return GetAllCards(configName ?? "default");

        var runtime = _contentRuntimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<IReadOnlyList<CardContentDefinition>>.Failure(runtime.Error);

        var cards = new List<CardContentDefinition>();
        foreach (var cardId in runtime.Value.GetDefinitions("cards").Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            var definition = runtime.Value.GetDefinition<CardContentDefinition>("cards", cardId);
            if (definition.IsFailure)
                return Result<IReadOnlyList<CardContentDefinition>>.Failure(definition.Error);
            cards.Add(Normalize(definition.Value, cardId));
        }
        return Result<IReadOnlyList<CardContentDefinition>>.Success(cards);
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _cardsByConfig.Clear();
        }
    }

    private Result<IReadOnlyDictionary<string, CardContentDefinition>> GetCards(string configName)
    {
        lock (_lock)
        {
            if (_cardsByConfig.TryGetValue(configName, out var cached))
                return Result<IReadOnlyDictionary<string, CardContentDefinition>>.Success(cached);

            try
            {
                var chain = _configManager.ResolveInheritanceChain(configName);
                var resources = _resourceLoader.LoadResource("cards/card_catalog.json", chain, strictMode: false);
                if (resources.Count == 0)
                    return Result<IReadOnlyDictionary<string, CardContentDefinition>>.Failure("Card catalog not found: cards/card_catalog.json");

                var cards = new Dictionary<string, CardContentDefinition>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, element) in resources)
                {
                    var definition = JsonSerializer.Deserialize<CardContentDefinition>(element.GetRawText(), _jsonOptions);
                    if (definition == null)
                        return Result<IReadOnlyDictionary<string, CardContentDefinition>>.Failure($"Failed to deserialize card content definition: {key}");

                    var normalized = Normalize(definition, key);
                    cards[normalized.CardId] = normalized;
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

    private static CardContentDefinition Normalize(CardContentDefinition definition, string fallbackId)
    {
        var cardId = string.IsNullOrWhiteSpace(definition.CardId) ? fallbackId : definition.CardId;
        var actionId = string.IsNullOrWhiteSpace(definition.ActionId) ? cardId : definition.ActionId;
        return definition with { CardId = cardId, ActionId = actionId };
    }
}
