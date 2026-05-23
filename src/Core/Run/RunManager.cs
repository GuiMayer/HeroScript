using Core.Common;
using Core.Config;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Run;

public sealed class RunManager : IRunManager
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly Dictionary<Guid, RunState> _runs = new();
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public RunManager(IConfigManager configManager, IResourceLoader resourceLoader)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public Result<RunState> StartRun(string configName = "default", string runDefinitionId = "default_run", string playerEntityId = "player")
    {
        var definitionResult = LoadDefinition(configName, runDefinitionId);
        if (definitionResult.IsFailure)
            return Result<RunState>.Failure(definitionResult.Error);

        var definition = definitionResult.Value;
        var state = new RunState
        {
            ConfigName = configName,
            PlayerEntityId = playerEntityId,
            Gold = definition.StartingGold,
            PowerPoints = definition.StartingPowerPoints,
            CurrentNodeId = definition.MapNodes.FirstOrDefault()?.NodeId,
            Deck = new DeckState { DrawPile = definition.StartingDeck.ToList() },
            Metadata = new Dictionary<string, object>(definition.Metadata)
        };

        lock (_lock)
        {
            _runs[state.RunId] = state;
        }

        var draw = DrawCards(state.RunId, definition.StartingHandSize);
        return draw.IsFailure ? Result<RunState>.Failure(draw.Error) : GetRun(state.RunId);
    }

    public Result<RunState> GetRun(Guid runId)
    {
        lock (_lock)
        {
            return _runs.TryGetValue(runId, out var state)
                ? Result<RunState>.Success(state)
                : Result<RunState>.Failure($"Run not found: {runId}");
        }
    }

    public Result<RunState> ApplyEconomy(Guid runId, string resource, int amount)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunState>.Failure($"Run not found: {runId}");

            switch (resource.ToLowerInvariant())
            {
                case "gold":
                    state.Gold = System.Math.Max(0, state.Gold + amount);
                    break;
                case "pp":
                case "powerpoints":
                case "power_points":
                    state.PowerPoints = System.Math.Max(0, state.PowerPoints + amount);
                    break;
                default:
                    return Result<RunState>.Failure($"Unsupported run economy resource: {resource}");
            }

            return Result<RunState>.Success(state);
        }
    }

    public Result<IReadOnlyList<string>> DrawCards(Guid runId, int count)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var drawn = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if (state.Deck.DrawPile.Count == 0)
                {
                    MoveAll(state.Deck.DiscardPile, state.Deck.DrawPile);
                }

                if (state.Deck.DrawPile.Count == 0)
                    break;

                var cardId = state.Deck.DrawPile[0];
                state.Deck.DrawPile.RemoveAt(0);
                state.Deck.Hand.Add(cardId);
                drawn.Add(cardId);
            }

            return Result<IReadOnlyList<string>>.Success(drawn);
        }
    }

    public Result<IReadOnlyList<string>> DiscardCards(Guid runId, IReadOnlyList<string> cardIds)
    {
        return MoveCards(runId, cardIds, deck => deck.Hand, deck => deck.DiscardPile, "hand");
    }

    public Result<IReadOnlyList<string>> ExhaustCards(Guid runId, IReadOnlyList<string> cardIds)
    {
        return MoveCards(runId, cardIds, deck => deck.Hand, deck => deck.ExhaustPile, "hand");
    }

    public Result<IReadOnlyList<string>> AddCardsToHand(Guid runId, IReadOnlyList<string> cardIds)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            foreach (var cardId in cardIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                state.Deck.Hand.Add(cardId);
            }

            return Result<IReadOnlyList<string>>.Success(cardIds.ToList());
        }
    }

    public Result ShuffleDiscardIntoDrawPile(Guid runId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result.Failure($"Run not found: {runId}");

            MoveAll(state.Deck.DiscardPile, state.Deck.DrawPile);
            return Result.Success();
        }
    }

    public Result<CardSelectionState> CreateCardSelection(Guid runId, string selectionId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var definitionResult = LoadCardSelectionDefinition(state.ConfigName, selectionId);
            if (definitionResult.IsFailure)
                return Result<CardSelectionState>.Failure(definitionResult.Error);

            var definition = definitionResult.Value;
            var selection = new CardSelectionState
            {
                RunId = runId,
                SelectionId = definition.SelectionId,
                PickCount = definition.PickCount,
                Options = definition.CardPool.ToList()
            };

            state.CardSelections.Add(selection);
            return Result<CardSelectionState>.Success(selection);
        }
    }

    public Result<CardSelectionState> PickCards(Guid runId, Guid selectionInstanceId, IReadOnlyList<string> cardIds)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var selection = state.CardSelections.FirstOrDefault(s => s.SelectionInstanceId == selectionInstanceId);
            if (selection == null)
                return Result<CardSelectionState>.Failure($"Card selection not found: {selectionInstanceId}");

            if (selection.Completed)
                return Result<CardSelectionState>.Failure($"Card selection already completed: {selectionInstanceId}");

            var picks = cardIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (picks.Count == 0 || picks.Count > selection.PickCount)
                return Result<CardSelectionState>.Failure($"Pick between 1 and {selection.PickCount} cards");

            var invalid = picks.Where(id => !selection.Options.Contains(id)).ToList();
            if (invalid.Count > 0)
                return Result<CardSelectionState>.Failure($"Invalid card options: {string.Join(", ", invalid)}");

            foreach (var cardId in picks)
            {
                state.Deck.DiscardPile.Add(cardId);
                selection.PickedCardIds.Add(cardId);
            }

            selection.Completed = true;
            return Result<CardSelectionState>.Success(selection);
        }
    }

    public Result<ShopState> CreateShop(Guid runId, string shopId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<ShopState>.Failure($"Run not found: {runId}");

            var definitionResult = LoadShopDefinition(state.ConfigName, shopId);
            if (definitionResult.IsFailure)
                return Result<ShopState>.Failure(definitionResult.Error);

            var definition = definitionResult.Value;
            var shop = new ShopState
            {
                RunId = runId,
                ShopId = definition.ShopId,
                Items = definition.Items.Select(item => new ShopItemState
                {
                    ItemId = item.ItemId,
                    CardId = item.CardId,
                    GoldCost = item.GoldCost,
                    PowerPointCost = item.PowerPointCost
                }).ToList()
            };

            state.Shops.Add(shop);
            return Result<ShopState>.Success(shop);
        }
    }

    public Result<ShopItemState> BuyShopItem(Guid runId, Guid shopInstanceId, string itemId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<ShopItemState>.Failure($"Run not found: {runId}");

            var shop = state.Shops.FirstOrDefault(s => s.ShopInstanceId == shopInstanceId);
            if (shop == null)
                return Result<ShopItemState>.Failure($"Shop not found: {shopInstanceId}");

            var item = shop.Items.FirstOrDefault(i => i.ItemId == itemId);
            if (item == null)
                return Result<ShopItemState>.Failure($"Shop item not found: {itemId}");

            if (item.Purchased)
                return Result<ShopItemState>.Failure($"Shop item already purchased: {itemId}");

            if (state.Gold < item.GoldCost || state.PowerPoints < item.PowerPointCost)
                return Result<ShopItemState>.Failure($"Insufficient resources for shop item: {itemId}");

            state.Gold -= item.GoldCost;
            state.PowerPoints -= item.PowerPointCost;

            if (!string.IsNullOrWhiteSpace(item.CardId))
                state.Deck.DiscardPile.Add(item.CardId);

            item.Purchased = true;
            return Result<ShopItemState>.Success(item);
        }
    }

    private Result<RunDefinition> LoadDefinition(string configName, string runDefinitionId)
    {
        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName);
            var data = _resourceLoader.LoadResource($"runs/{runDefinitionId}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<RunDefinition>.Failure($"Run definition not found: {runDefinitionId}");

            var element = data.TryGetValue(runDefinitionId, out var exact) ? exact : data.Values.First();
            var definition = JsonSerializer.Deserialize<RunDefinition>(element.GetRawText(), _jsonOptions);
            return definition == null
                ? Result<RunDefinition>.Failure($"Failed to deserialize run definition: {runDefinitionId}")
                : Result<RunDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<RunDefinition>.Failure($"Could not load run definition '{runDefinitionId}': {ex.Message}", ex);
        }
    }

    private Result<CardSelectionDefinition> LoadCardSelectionDefinition(string configName, string selectionId)
    {
        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName);
            var data = _resourceLoader.LoadResource($"card-selections/{selectionId}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<CardSelectionDefinition>.Failure($"Card selection definition not found: {selectionId}");

            var element = data.TryGetValue(selectionId, out var exact) ? exact : data.Values.First();
            var definition = JsonSerializer.Deserialize<CardSelectionDefinition>(element.GetRawText(), _jsonOptions);
            return definition == null
                ? Result<CardSelectionDefinition>.Failure($"Failed to deserialize card selection definition: {selectionId}")
                : Result<CardSelectionDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<CardSelectionDefinition>.Failure($"Could not load card selection definition '{selectionId}': {ex.Message}", ex);
        }
    }

    private Result<ShopDefinition> LoadShopDefinition(string configName, string shopId)
    {
        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName);
            var data = _resourceLoader.LoadResource($"shops/{shopId}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<ShopDefinition>.Failure($"Shop definition not found: {shopId}");

            var element = data.TryGetValue(shopId, out var exact) ? exact : data.Values.First();
            var definition = JsonSerializer.Deserialize<ShopDefinition>(element.GetRawText(), _jsonOptions);
            return definition == null
                ? Result<ShopDefinition>.Failure($"Failed to deserialize shop definition: {shopId}")
                : Result<ShopDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<ShopDefinition>.Failure($"Could not load shop definition '{shopId}': {ex.Message}", ex);
        }
    }

    private Result<IReadOnlyList<string>> MoveCards(
        Guid runId,
        IReadOnlyList<string> cardIds,
        Func<DeckState, List<string>> fromSelector,
        Func<DeckState, List<string>> toSelector,
        string sourceName)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var from = fromSelector(state.Deck);
            var to = toSelector(state.Deck);
            var moved = new List<string>();

            foreach (var cardId in cardIds)
            {
                if (!from.Remove(cardId))
                    return Result<IReadOnlyList<string>>.Failure($"Card '{cardId}' not found in {sourceName}");

                to.Add(cardId);
                moved.Add(cardId);
            }

            return Result<IReadOnlyList<string>>.Success(moved);
        }
    }

    private static void MoveAll(List<string> from, List<string> to)
    {
        to.AddRange(from);
        from.Clear();
    }
}
