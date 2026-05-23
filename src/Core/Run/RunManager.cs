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
