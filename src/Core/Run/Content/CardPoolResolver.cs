using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Config;

namespace Core.Run.Content;

public sealed class CardPoolResolver : ICardPoolResolver
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ICardContentCatalog _catalog;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<string, CardPoolDefinition>> _poolsByConfig = new(StringComparer.OrdinalIgnoreCase);

    public CardPoolResolver(IConfigManager configManager, IResourceLoader resourceLoader, ICardContentCatalog catalog)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public Result<CardPoolDefinition> GetPool(string poolId, string configName = "default")
    {
        if (string.IsNullOrWhiteSpace(poolId))
            return Result<CardPoolDefinition>.Failure("PoolId cannot be empty");

        var poolsResult = GetPools(configName);
        if (poolsResult.IsFailure)
            return Result<CardPoolDefinition>.Failure(poolsResult.Error);

        return poolsResult.Value.TryGetValue(poolId, out var pool)
            ? Result<CardPoolDefinition>.Success(pool)
            : Result<CardPoolDefinition>.Failure($"Card pool definition not found: {poolId}");
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

    public void Invalidate()
    {
        lock (_lock)
        {
            _poolsByConfig.Clear();
        }
    }

    private Result<Dictionary<string, CardPoolDefinition>> GetPools(string configName)
    {
        lock (_lock)
        {
            if (_poolsByConfig.TryGetValue(configName, out var cached))
                return Result<Dictionary<string, CardPoolDefinition>>.Success(cached);

            try
            {
                var chain = _configManager.ResolveInheritanceChain(configName);
                var resources = _resourceLoader.LoadResource("card-pools/basic_rewards.json", chain, strictMode: false);
                if (resources.Count == 0)
                    return Result<Dictionary<string, CardPoolDefinition>>.Failure("Card pools not found: card-pools/basic_rewards.json");

                var pools = new Dictionary<string, CardPoolDefinition>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, element) in resources)
                {
                    var definition = JsonSerializer.Deserialize<CardPoolDefinition>(element.GetRawText(), _jsonOptions);
                    if (definition == null)
                        return Result<Dictionary<string, CardPoolDefinition>>.Failure($"Failed to deserialize card pool definition: {key}");

                    var poolId = string.IsNullOrWhiteSpace(definition.PoolId) ? key : definition.PoolId;
                    pools[poolId] = definition with { PoolId = poolId };
                }

                _poolsByConfig[configName] = pools;
                return Result<Dictionary<string, CardPoolDefinition>>.Success(pools);
            }
            catch (Exception ex)
            {
                return Result<Dictionary<string, CardPoolDefinition>>.Failure($"Failed to load card pools: {ex.Message}");
            }
        }
    }
}
