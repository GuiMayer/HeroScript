using Core.Abstractions.Persistence;
using Core.Common;
using Core.Combat.Modifiers;
using Core.Config;
using Core.Events;
using Core.Events.Domain;
using Core.Determinism;
using Core.Run.Content;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Run;

public sealed class RunManager : IRunManager
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly ICardPoolResolver? _cardPoolResolver;
    private readonly ICardContentCatalog? _cardContentCatalog;
    private readonly IScriptModifierManager? _scriptModifierManager;
    private readonly IEventBus? _eventBus;
    private readonly IRunStateRepository? _repository;
    private readonly Dictionary<Guid, RunState> _runs = new();
    private readonly object _lock = new();
    private readonly object _persistenceLock = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public RunManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ICardPoolResolver? cardPoolResolver = null,
        ICardContentCatalog? cardContentCatalog = null,
        IScriptModifierManager? scriptModifierManager = null,
        IEventBus? eventBus = null,
        IRunStateRepository? repository = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _cardPoolResolver = cardPoolResolver;
        _cardContentCatalog = cardContentCatalog;
        _scriptModifierManager = scriptModifierManager;
        _eventBus = eventBus;
        _repository = repository;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public Result<RunState> StartRun(string configName = "default", string runDefinitionId = "default_run", string playerEntityId = "player")
    {
        return StartRun(new RunStartOptions(configName, runDefinitionId, playerEntityId));
    }

    public Result<RunState> StartRun(RunStartOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConfigName))
            return Result<RunState>.Failure("Config name is required");
        if (string.IsNullOrWhiteSpace(options.RunDefinitionId))
            return Result<RunState>.Failure("Run definition id is required");
        if (string.IsNullOrWhiteSpace(options.PlayerEntityId))
            return Result<RunState>.Failure("Player entity id is required");

        var definitionResult = LoadDefinition(options.ConfigName, options.RunDefinitionId);
        if (definitionResult.IsFailure)
            return Result<RunState>.Failure(definitionResult.Error);

        var definition = definitionResult.Value;
        var seed = options.Seed ?? CreateSeed();
        var contentRevision = string.IsNullOrWhiteSpace(options.ContentRevision)
            ? CanonicalJson.ComputeHash(definition, _jsonOptions)
            : options.ContentRevision;
        var context = DeterministicContext.Create(seed, contentRevision!);
        var runId = context.AllocateId(
            $"run:{options.ConfigName}:{options.RunDefinitionId}:{options.PlayerEntityId}");
        context = runId.Context;

        var state = new RunState
        {
            RunId = runId.Value,
            ConfigName = options.ConfigName,
            PlayerEntityId = options.PlayerEntityId,
            Gold = definition.StartingGold,
            PowerPoints = definition.StartingPowerPoints,
            CurrentNodeId = definition.MapNodes.FirstOrDefault()?.NodeId,
            Deck = new DeckState { DrawPile = [.. definition.StartingDeck] },
            Metadata = ToImmutableMetadata(definition.Metadata),
            Determinism = context
        };

        var initialDraw = DeckTransitions.Draw(state.Deck, definition.StartingHandSize, state.Determinism);
        if (initialDraw.IsFailure)
            return Result<RunState>.Failure(initialDraw.Error);

        state = state with
        {
            Deck = initialDraw.Value.State,
            Determinism = initialDraw.Value.Context.AdvanceStep()
        };

        lock (_lock)
        {
            if (_runs.ContainsKey(state.RunId))
                return Result<RunState>.Failure($"Run already exists for the deterministic inputs: {state.RunId}");

            _runs[state.RunId] = state;
            state = PersistAsync(state);
        }

        _eventBus?.Publish(new RunStartedEvent(state.RunId, options.ConfigName, options.PlayerEntityId, definition.StartingGold, definition.StartingPowerPoints));
        if (!initialDraw.Value.Cards.IsEmpty)
            _eventBus?.Publish(new CardDrawnEvent(state.RunId, initialDraw.Value.Cards));

        return Result<RunState>.Success(state);
    }

    public Result<RunState> GetRun(Guid runId)
    {
        lock (_lock)
        {
            if (_runs.TryGetValue(runId, out var state))
                return Result<RunState>.Success(state);
        }

        // Cache miss — try loading from repository
        if (_repository != null)
        {
            var loaded = _repository.LoadLatestAsync(runId).GetAwaiter().GetResult();
            if (loaded != null)
            {
                lock (_lock) { _runs[runId] = loaded; }
                return Result<RunState>.Success(loaded);
            }
        }

        return Result<RunState>.Failure($"Run not found: {runId}");
    }

    public Result<RunState> ApplyEconomy(Guid runId, string resource, int amount)
    {
        int oldValue, newValue;
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<RunState>.Failure($"Run not found: {runId}");

            switch (resource.ToLowerInvariant())
            {
                case "gold":
                    oldValue = state.Gold;
                    newValue = SaturatingEconomyChange(state.Gold, amount);
                    state = state with { Gold = newValue };
                    break;
                case "pp":
                case "powerpoints":
                case "power_points":
                    oldValue = state.PowerPoints;
                    newValue = SaturatingEconomyChange(state.PowerPoints, amount);
                    state = state with { PowerPoints = newValue };
                    break;
                default:
                    return Result<RunState>.Failure($"Unsupported run economy resource: {resource}");
            }

            state = state with { Determinism = state.Determinism.AdvanceStep() };
            _runs[runId] = state;
            state = PersistAsync(state);
            _eventBus?.Publish(new EconomyChangedEvent(runId, resource, oldValue, newValue));
            return Result<RunState>.Success(state);
        }
    }

    public Result<IReadOnlyList<string>> DrawCards(Guid runId, int count)
    {
        ImmutableArray<string> drawn;
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.Draw(state.Deck, count, state.Determinism);
            if (transition.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(transition.Error);

            drawn = transition.Value.Cards;
            if (!drawn.IsEmpty)
            {
                state = state with
                {
                    Deck = transition.Value.State,
                    Determinism = transition.Value.Context.AdvanceStep()
                };
                _runs[runId] = state;
                PersistAsync(state);
            }
        }

        if (!drawn.IsEmpty)
            _eventBus?.Publish(new CardDrawnEvent(runId, drawn));

        return Result<IReadOnlyList<string>>.Success(drawn);
    }

    public Result<IReadOnlyList<string>> DiscardCards(Guid runId, IReadOnlyList<string> cardIds)
    {
        return MoveCards(runId, cardIds, CardConsumeDestination.Discard);
    }

    public Result<IReadOnlyList<string>> ExhaustCards(Guid runId, IReadOnlyList<string> cardIds)
    {
        return MoveCards(runId, cardIds, CardConsumeDestination.Exhaust);
    }

    public Result<IReadOnlyList<string>> AddCardsToHand(Guid runId, IReadOnlyList<string> cardIds)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.AddToHand(state.Deck, cardIds, state.Determinism);
            if (transition.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(transition.Error);

            if (!transition.Value.Cards.IsEmpty)
            {
                state = state with
                {
                    Deck = transition.Value.State,
                    Determinism = transition.Value.Context.AdvanceStep()
                };
                _runs[runId] = state;
                PersistAsync(state);
            }

            return Result<IReadOnlyList<string>>.Success(transition.Value.Cards);
        }
    }

    public Result<bool> HasCardInHand(Guid runId, string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return Result<bool>.Failure("Card id is required");

        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<bool>.Failure($"Run not found: {runId}");

            return Result<bool>.Success(state.Deck.Hand.Contains(cardId));
        }
    }

    public Result<IReadOnlyList<string>> ConsumeCardsFromHand(Guid runId, IReadOnlyList<string> cardIds, CardConsumeDestination destination)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.MoveFromHand(state.Deck, cardIds, destination, state.Determinism);
            if (transition.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(transition.Error);

            if (!ReferenceEquals(transition.Value.State, state.Deck))
            {
                state = state with
                {
                    Deck = transition.Value.State,
                    Determinism = transition.Value.Context.AdvanceStep()
                };
                _runs[runId] = state;
                PersistAsync(state);
            }

            return Result<IReadOnlyList<string>>.Success(transition.Value.Cards);
        }
    }

    public Result ShuffleDiscardIntoDrawPile(Guid runId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.ShuffleDiscardIntoDrawPile(state.Deck, state.Determinism);
            if (!transition.Cards.IsEmpty)
            {
                state = state with
                {
                    Deck = transition.State,
                    Determinism = transition.Context.AdvanceStep()
                };
                _runs[runId] = state;
                PersistAsync(state);
            }
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
            var instanceId = state.Determinism.AllocateId("card-selection");
            var selection = new CardSelectionState
            {
                SelectionInstanceId = instanceId.Value,
                RunId = runId,
                SelectionId = definition.SelectionId,
                PickCount = definition.PickCount,
                OfferCount = definition.OfferCount,
                CardPoolId = definition.CardPoolId,
                Reroll = definition.Reroll,
                Decompose = definition.Decompose,
                FreeRerollsRemaining = definition.Reroll.FreeRerolls,
                RerollCostGold = CalculateRerollCost(definition.Reroll, 0),
                Options = GenerateCardSelectionOptions(state, definition, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            };

            state = state with
            {
                CardSelections = state.CardSelections.Add(selection),
                Determinism = instanceId.Context.AdvanceStep()
            };
            _runs[runId] = state;
            PersistAsync(state);
            return Result<CardSelectionState>.Success(selection);
        }
    }

    public Result<CardSelectionState> PickCards(Guid runId, Guid selectionInstanceId, IReadOnlyList<string> cardIds)
    {
        lock (_lock)
        {
            return ExecuteRunTransaction(runId, transaction =>
            {
                var state = transaction.State;
                var selection = state.CardSelections.FirstOrDefault(s => s.SelectionInstanceId == selectionInstanceId);
                if (selection == null)
                    return Result<CardSelectionState>.Failure($"Card selection not found: {selectionInstanceId}");

                if (selection.Completed)
                    return Result<CardSelectionState>.Failure($"Card selection already completed: {selectionInstanceId}");

                var picks = cardIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
                if (picks.Count == 0 || picks.Count > selection.PickCount)
                    return Result<CardSelectionState>.Failure($"Pick between 1 and {selection.PickCount} cards");

                var availableOptions = selection.Options
                    .Where(option => !option.Decomposed)
                    .Select(option => option.CardId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var invalid = picks.Where(id => !availableOptions.Contains(id)).ToList();
                if (invalid.Count > 0)
                    return Result<CardSelectionState>.Failure($"Invalid card options: {string.Join(", ", invalid)}");

                var deck = DeckTransitions.AddToDiscard(state.Deck, picks, state.Determinism);
                if (deck.IsFailure)
                    return Result<CardSelectionState>.Failure(deck.Error);

                foreach (var cardId in picks)
                    selection.PickedCardIds.Add(cardId);

                selection.Completed = true;
                transaction.State = state with { Deck = deck.Value.State };
                return Result<CardSelectionState>.Success(selection);
            });
        }
    }

    public Result<CardSelectionState> RerollCardSelection(Guid runId, Guid selectionInstanceId, IReadOnlyList<string>? lockedCardIds = null)
    {
        lock (_lock)
        {
            return ExecuteRunTransaction(runId, transaction =>
            {
                var state = transaction.State;
                var selection = state.CardSelections.FirstOrDefault(s => s.SelectionInstanceId == selectionInstanceId);
                if (selection == null)
                    return Result<CardSelectionState>.Failure($"Card selection not found: {selectionInstanceId}");

                if (selection.Completed)
                    return Result<CardSelectionState>.Failure($"Card selection already completed: {selectionInstanceId}");

                var definitionResult = LoadCardSelectionDefinition(state.ConfigName, selection.SelectionId);
                if (definitionResult.IsFailure)
                    return Result<CardSelectionState>.Failure(definitionResult.Error);

                var cost = selection.FreeRerollsRemaining > 0 ? 0 : selection.RerollCostGold;
                if (state.Gold < cost)
                    return Result<CardSelectionState>.Failure($"Insufficient gold for reroll: {selection.SelectionId}");

                transaction.State = state = state with { Gold = state.Gold - cost };
                selection.RerollsUsed++;
                selection.FreeRerollsRemaining = System.Math.Max(0, selection.FreeRerollsRemaining - 1);
                selection.RerollCostGold = CalculateRerollCost(selection.Reroll, selection.RerollsUsed);

                var locked = new HashSet<string>(lockedCardIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var option in selection.Options.Where(option => locked.Contains(option.CardId)))
                    option.Decomposed = false;

                var generated = GenerateCardSelectionOptions(state, definitionResult.Value, locked);
                selection.Options.Clear();
                selection.Options.AddRange(generated);
                return Result<CardSelectionState>.Success(selection);
            });
        }
    }

    public Result<CardSelectionState> DecomposeCardSelectionOption(Guid runId, Guid selectionInstanceId, string cardId)
    {
        lock (_lock)
        {
            return ExecuteRunTransaction(runId, transaction =>
            {
                var state = transaction.State;
                var selection = state.CardSelections.FirstOrDefault(s => s.SelectionInstanceId == selectionInstanceId);
                if (selection == null)
                    return Result<CardSelectionState>.Failure($"Card selection not found: {selectionInstanceId}");

                if (selection.Completed)
                    return Result<CardSelectionState>.Failure($"Card selection already completed: {selectionInstanceId}");

                if (!selection.Decompose.Enabled)
                    return Result<CardSelectionState>.Failure($"Decompose is disabled for card selection: {selection.SelectionId}");

                var option = selection.Options.FirstOrDefault(o => o.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase));
                if (option == null)
                    return Result<CardSelectionState>.Failure($"Card option not found: {cardId}");

                if (option.Decomposed)
                    return Result<CardSelectionState>.Failure($"Card option already decomposed: {cardId}");

                option.Decomposed = true;
                selection.DecomposedCardIds.Add(option.CardId);
                transaction.State = state with { PowerPoints = checked(state.PowerPoints + option.DecomposePowerPoints) };
                return Result<CardSelectionState>.Success(selection);
            });
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
            var instanceId = state.Determinism.AllocateId("shop");
            var shop = new ShopState
            {
                ShopInstanceId = instanceId.Value,
                RunId = runId,
                ShopId = definition.ShopId,
                CardPoolId = definition.CardPoolId,
                OfferCount = definition.OfferCount,
                Pricing = definition.Pricing,
                Reroll = definition.Reroll,
                RerollCostGold = CalculateShopRerollCost(definition.Reroll, 0),
                Items = GenerateShopItems(state, definition)
            };

            state = state with
            {
                Shops = state.Shops.Add(shop),
                Determinism = instanceId.Context.AdvanceStep()
            };
            _runs[runId] = state;
            PersistAsync(state);
            return Result<ShopState>.Success(shop);
        }
    }

    public Result<ShopItemState> BuyShopItem(Guid runId, Guid shopInstanceId, string itemId)
    {
        lock (_lock)
        {
            return ExecuteRunTransaction(runId, transaction =>
            {
                var state = transaction.State;
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

                state = state with
                {
                    Gold = state.Gold - item.GoldCost,
                    PowerPoints = state.PowerPoints - item.PowerPointCost
                };

                if (!string.IsNullOrWhiteSpace(item.CardId))
                {
                    var deck = DeckTransitions.AddToDiscard(state.Deck, new[] { item.CardId }, state.Determinism);
                    if (deck.IsFailure)
                        return Result<ShopItemState>.Failure(deck.Error);
                    state = state with { Deck = deck.Value.State };
                }

                item.Purchased = true;
                transaction.State = state;
                return Result<ShopItemState>.Success(item);
            });
        }
    }

    public Result<ShopState> RerollShop(Guid runId, Guid shopInstanceId)
    {
        lock (_lock)
        {
            return ExecuteRunTransaction(runId, transaction =>
            {
                var state = transaction.State;
                var shop = state.Shops.FirstOrDefault(s => s.ShopInstanceId == shopInstanceId);
                if (shop == null)
                    return Result<ShopState>.Failure($"Shop not found: {shopInstanceId}");

                var definitionResult = LoadShopDefinition(state.ConfigName, shop.ShopId);
                if (definitionResult.IsFailure)
                    return Result<ShopState>.Failure(definitionResult.Error);

                if (state.Gold < shop.RerollCostGold)
                    return Result<ShopState>.Failure($"Insufficient gold for shop reroll: {shop.ShopId}");

                transaction.State = state = state with { Gold = state.Gold - shop.RerollCostGold };
                shop.RerollsUsed++;
                shop.RerollCostGold = CalculateShopRerollCost(shop.Reroll, shop.RerollsUsed);
                shop.Items.Clear();
                shop.Items.AddRange(GenerateShopItems(state, definitionResult.Value, shop.RerollsUsed));
                return Result<ShopState>.Success(shop);
            });
        }
    }

    public Result<PreparationState> CreatePreparation(Guid runId, string preparationId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<PreparationState>.Failure($"Run not found: {runId}");

            var definitionResult = LoadPreparationDefinition(state.ConfigName, preparationId);
            if (definitionResult.IsFailure)
                return Result<PreparationState>.Failure(definitionResult.Error);

            var definition = definitionResult.Value;
            var instanceId = state.Determinism.AllocateId("preparation");
            var preparation = new PreparationState
            {
                PreparationInstanceId = instanceId.Value,
                RunId = runId,
                PreparationId = definition.PreparationId,
                Options = definition.Options.Select(option => new PreparationOptionState
                {
                    OptionId = option.OptionId,
                    GoldCost = option.GoldCost,
                    PowerPointCost = option.PowerPointCost,
                    AddCardsToDiscard = option.AddCardsToDiscard.ToList(),
                    ApplyModifiers = option.ApplyModifiers.Select(modifier => new PreparationModifierGrantState
                    {
                        OwnerId = modifier.OwnerId,
                        ModifierId = modifier.ModifierId,
                        Stacks = modifier.Stacks,
                        Duration = modifier.Duration,
                        SourceId = modifier.SourceId
                    }).ToList()
                }).ToList()
            };

            state = state with
            {
                Preparations = state.Preparations.Add(preparation),
                Determinism = instanceId.Context.AdvanceStep()
            };
            _runs[runId] = state;
            PersistAsync(state);
            return Result<PreparationState>.Success(preparation);
        }
    }

    public Result<PreparationOptionState> ApplyPreparationOption(Guid runId, Guid preparationInstanceId, string optionId)
    {
        lock (_lock)
        {
            return ExecuteRunTransaction(runId, transaction =>
            {
                var state = transaction.State;
                var preparation = state.Preparations.FirstOrDefault(p => p.PreparationInstanceId == preparationInstanceId);
                if (preparation == null)
                    return Result<PreparationOptionState>.Failure($"Preparation not found: {preparationInstanceId}");

                var option = preparation.Options.FirstOrDefault(o => o.OptionId == optionId);
                if (option == null)
                    return Result<PreparationOptionState>.Failure($"Preparation option not found: {optionId}");

                if (option.Applied)
                    return Result<PreparationOptionState>.Failure($"Preparation option already applied: {optionId}");

                if (state.Gold < option.GoldCost || state.PowerPoints < option.PowerPointCost)
                    return Result<PreparationOptionState>.Failure($"Insufficient resources for preparation option: {optionId}");

                if (option.ApplyModifiers.Count > 0 && _scriptModifierManager == null)
                    return Result<PreparationOptionState>.Failure("Script modifier manager is not available for preparation modifier grants");

                var deck = DeckTransitions.AddToDiscard(state.Deck, option.AddCardsToDiscard, state.Determinism);
                if (deck.IsFailure)
                    return Result<PreparationOptionState>.Failure(deck.Error);
                state = state with
                {
                    Gold = state.Gold - option.GoldCost,
                    PowerPoints = state.PowerPoints - option.PowerPointCost,
                    Deck = deck.Value.State
                };
                transaction.State = state;

                var appliedModifiers = new List<(string OwnerId, Guid InstanceId)>();
                foreach (var modifier in option.ApplyModifiers)
                {
                    var ownerId = ResolvePreparationModifierOwner(state, modifier.OwnerId);
                    var sourceId = string.IsNullOrWhiteSpace(modifier.SourceId) ? option.OptionId : modifier.SourceId;
                    var apply = _scriptModifierManager!.ApplyModifier(ownerId, modifier.ModifierId, modifier.Stacks, modifier.Duration, sourceId);
                    if (apply.IsFailure)
                    {
                        var rollback = RollbackAppliedModifiers(appliedModifiers);
                        return rollback.IsFailure
                            ? Result<PreparationOptionState>.Failure($"{apply.Error}; modifier rollback failed: {rollback.Error}")
                            : Result<PreparationOptionState>.Failure(apply.Error);
                    }

                    appliedModifiers.Add((ownerId, apply.Value.InstanceId));
                    option.AppliedModifierInstanceIds.Add(apply.Value.InstanceId);
                }

                option.Applied = true;
                preparation.AppliedOptionIds.Add(option.OptionId);

                return Result<PreparationOptionState>.Success(option);
            });
        }
    }

    private Result RollbackAppliedModifiers(IReadOnlyList<(string OwnerId, Guid InstanceId)> appliedModifiers)
    {
        if (_scriptModifierManager == null)
            return appliedModifiers.Count == 0
                ? Result.Success()
                : Result.Failure("Script modifier manager is not available for modifier rollback");

        foreach (var applied in appliedModifiers.AsEnumerable().Reverse())
        {
            var remove = _scriptModifierManager.RemoveModifier(applied.OwnerId, applied.InstanceId);
            if (remove.IsFailure)
                return Result.Failure(remove.Error);
        }

        return Result.Success();
    }

    private Result<T> ExecuteRunTransaction<T>(Guid runId, Func<RunTransaction, Result<T>> operation)
    {
        if (!_runs.TryGetValue(runId, out var state))
            return Result<T>.Failure($"Run not found: {runId}");

        var transaction = new RunTransaction(CloneRunState(state));
        try
        {
            var result = operation(transaction);
            if (result.IsFailure)
                return result;

            var committed = transaction.State with
            {
                Determinism = transaction.State.Determinism.AdvanceStep()
            };
            _runs[runId] = committed;
            PersistAsync(committed);
            return result;
        }
        catch (Exception ex)
        {
            return Result<T>.Failure($"Run transaction failed: {ex.Message}");
        }
    }

    private sealed class RunTransaction
    {
        public RunTransaction(RunState state) => State = state;
        public RunState State { get; set; }
    }

    private static RunState CloneRunState(RunState source)
    {
        return new RunState
        {
            RunId = source.RunId,
            Sequence = source.Sequence,
            ConfigName = source.ConfigName,
            PlayerEntityId = source.PlayerEntityId,
            Gold = source.Gold,
            PowerPoints = source.PowerPoints,
            CurrentNodeId = source.CurrentNodeId,
            Deck = CloneDeckState(source.Deck),
            CardSelections = [.. source.CardSelections.Select(CloneCardSelection)],
            Shops = [.. source.Shops.Select(CloneShop)],
            Preparations = [.. source.Preparations.Select(ClonePreparation)],
            Metadata = source.Metadata,
            Determinism = source.Determinism
        };
    }

    private static DeckState CloneDeckState(DeckState source)
    {
        return new DeckState
        {
            DrawPile = source.DrawPile,
            Hand = source.Hand,
            DiscardPile = source.DiscardPile,
            ExhaustPile = source.ExhaustPile
        };
    }

    private static CardSelectionState CloneCardSelection(CardSelectionState source)
    {
        return new CardSelectionState
        {
            SelectionInstanceId = source.SelectionInstanceId,
            RunId = source.RunId,
            SelectionId = source.SelectionId,
            PickCount = source.PickCount,
            OfferCount = source.OfferCount,
            CardPoolId = source.CardPoolId,
            Options = source.Options.Select(CloneCardSelectionOption).ToList(),
            RerollsUsed = source.RerollsUsed,
            FreeRerollsRemaining = source.FreeRerollsRemaining,
            RerollCostGold = source.RerollCostGold,
            Completed = source.Completed,
            PickedCardIds = source.PickedCardIds.ToList(),
            DecomposedCardIds = source.DecomposedCardIds.ToList(),
            Reroll = source.Reroll,
            Decompose = source.Decompose
        };
    }

    private static CardSelectionOptionState CloneCardSelectionOption(CardSelectionOptionState source)
    {
        return new CardSelectionOptionState
        {
            CardId = source.CardId,
            Rarity = source.Rarity,
            Tags = source.Tags.ToList(),
            DecomposePowerPoints = source.DecomposePowerPoints,
            Decomposed = source.Decomposed
        };
    }

    private static ShopState CloneShop(ShopState source)
    {
        return new ShopState
        {
            ShopInstanceId = source.ShopInstanceId,
            RunId = source.RunId,
            ShopId = source.ShopId,
            CardPoolId = source.CardPoolId,
            OfferCount = source.OfferCount,
            RerollsUsed = source.RerollsUsed,
            RerollCostGold = source.RerollCostGold,
            Pricing = source.Pricing,
            Reroll = source.Reroll,
            Items = source.Items.Select(CloneShopItem).ToList()
        };
    }

    private static ShopItemState CloneShopItem(ShopItemState source)
    {
        return new ShopItemState
        {
            ItemId = source.ItemId,
            CardId = source.CardId,
            Rarity = source.Rarity,
            Tags = source.Tags.ToList(),
            BaseGoldPrice = source.BaseGoldPrice,
            GoldCost = source.GoldCost,
            PowerPointCost = source.PowerPointCost,
            PricingBreakdown = new Dictionary<string, double>(source.PricingBreakdown, StringComparer.OrdinalIgnoreCase),
            Purchased = source.Purchased
        };
    }

    private static PreparationState ClonePreparation(PreparationState source)
    {
        return new PreparationState
        {
            PreparationInstanceId = source.PreparationInstanceId,
            RunId = source.RunId,
            PreparationId = source.PreparationId,
            Options = source.Options.Select(ClonePreparationOption).ToList(),
            AppliedOptionIds = source.AppliedOptionIds.ToList()
        };
    }

    private static PreparationOptionState ClonePreparationOption(PreparationOptionState source)
    {
        return new PreparationOptionState
        {
            OptionId = source.OptionId,
            GoldCost = source.GoldCost,
            PowerPointCost = source.PowerPointCost,
            AddCardsToDiscard = source.AddCardsToDiscard.ToList(),
            ApplyModifiers = source.ApplyModifiers.Select(ClonePreparationModifierGrant).ToList(),
            AppliedModifierInstanceIds = source.AppliedModifierInstanceIds.ToList(),
            Applied = source.Applied
        };
    }

    private static PreparationModifierGrantState ClonePreparationModifierGrant(PreparationModifierGrantState source)
    {
        return new PreparationModifierGrantState
        {
            OwnerId = source.OwnerId,
            ModifierId = source.ModifierId,
            Stacks = source.Stacks,
            Duration = source.Duration,
            SourceId = source.SourceId
        };
    }

    private List<CardSelectionOptionState> GenerateCardSelectionOptions(RunState state, CardSelectionDefinition definition, IReadOnlySet<string> lockedCardIds)
    {
        var lockedOptions = lockedCardIds
            .Select(cardId => CreateCardSelectionOption(state.ConfigName, cardId))
            .Where(option => option != null)
            .Cast<CardSelectionOptionState>()
            .ToList();

        var candidates = ResolveCardSelectionCandidates(state.ConfigName, definition)
            .Where(option => !lockedCardIds.Contains(option.CardId))
            .Where(option => !lockedOptions.Any(locked => locked.CardId.Equals(option.CardId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(option => RarityRank(option.Rarity))
            .ThenBy(option => option.CardId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var desiredCount = System.Math.Max(1, definition.OfferCount) - lockedOptions.Count;
        lockedOptions.AddRange(candidates.Take(System.Math.Max(0, desiredCount)));
        return lockedOptions;
    }

    private List<CardSelectionOptionState> ResolveCardSelectionCandidates(string configName, CardSelectionDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.CardPoolId) && _cardPoolResolver != null)
        {
            var poolResult = _cardPoolResolver.ResolvePool(definition.CardPoolId, configName);
            if (poolResult.IsSuccess)
                return poolResult.Value.Cards.Select(ToOption).ToList();
        }

        return definition.CardPool
            .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
            .Select(cardId => CreateCardSelectionOption(configName, cardId) ?? new CardSelectionOptionState { CardId = cardId })
            .ToList();
    }

    private CardSelectionOptionState? CreateCardSelectionOption(string configName, string cardId)
    {
        if (_cardContentCatalog == null)
            return null;

        var cardResult = _cardContentCatalog.GetCard(cardId, configName);
        return cardResult.IsSuccess ? ToOption(cardResult.Value) : null;
    }

    private static CardSelectionOptionState ToOption(CardContentDefinition card)
    {
        return new CardSelectionOptionState
        {
            CardId = card.CardId,
            Rarity = card.Rarity,
            Tags = card.Tags.ToList(),
            DecomposePowerPoints = card.DecomposePowerPoints
        };
    }

    private static int CalculateRerollCost(RerollRulesDefinition rules, int rerollsUsed)
    {
        return rules.BaseGoldCost + System.Math.Max(0, rerollsUsed) * rules.GoldCostPerReroll;
    }

    private static int RarityRank(CardRarity rarity)
    {
        return rarity switch
        {
            CardRarity.Common => 0,
            CardRarity.Uncommon => 1,
            CardRarity.Rare => 2,
            CardRarity.Legendary => 3,
            _ => 99
        };
    }

    private static string ResolvePreparationModifierOwner(RunState state, string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Equals("run", StringComparison.OrdinalIgnoreCase))
            return $"run:{state.RunId}";

        if (ownerId.Equals("player", StringComparison.OrdinalIgnoreCase))
            return state.PlayerEntityId;

        return ownerId;
    }

    private List<ShopItemState> GenerateShopItems(RunState state, ShopDefinition definition, int offset = 0)
    {
        if (!string.IsNullOrWhiteSpace(definition.CardPoolId) && _cardPoolResolver != null)
        {
            var poolResult = _cardPoolResolver.ResolvePool(definition.CardPoolId, state.ConfigName);
            if (poolResult.IsSuccess)
            {
                return poolResult.Value.Cards
                    .OrderBy(card => RarityRank(card.Rarity))
                    .ThenBy(card => card.CardId, StringComparer.OrdinalIgnoreCase)
                    .Skip(offset)
                    .Take(System.Math.Max(1, definition.OfferCount))
                    .Select((card, index) => ToShopItem(card, definition.Pricing, index))
                    .ToList();
            }
        }

        return definition.Items.Select((item, index) => ToShopItem(state.ConfigName, item, definition.Pricing, index)).ToList();
    }

    private ShopItemState ToShopItem(string configName, ShopItemDefinition item, ShopPricingRules pricing, int index)
    {
        if (!string.IsNullOrWhiteSpace(item.CardId) && _cardContentCatalog != null)
        {
            var cardResult = _cardContentCatalog.GetCard(item.CardId, configName);
            if (cardResult.IsSuccess)
                return ToShopItem(cardResult.Value, pricing, index, item.ItemId, item.PowerPointCost, item.GoldCost);
        }

        return new ShopItemState
        {
            ItemId = string.IsNullOrWhiteSpace(item.ItemId) ? $"item_{index + 1}" : item.ItemId,
            CardId = item.CardId,
            BaseGoldPrice = item.GoldCost,
            GoldCost = item.GoldCost,
            PowerPointCost = item.PowerPointCost
        };
    }

    private static ShopItemState ToShopItem(CardContentDefinition card, ShopPricingRules pricing, int index, string? itemId = null, int powerPointCost = 0, int? explicitGoldCost = null)
    {
        var breakdown = CalculateShopPrice(card, pricing, explicitGoldCost);
        return new ShopItemState
        {
            ItemId = string.IsNullOrWhiteSpace(itemId) ? $"buy_{card.CardId}_{index + 1}" : itemId,
            CardId = card.CardId,
            Rarity = card.Rarity,
            Tags = card.Tags.ToList(),
            BaseGoldPrice = card.BaseGoldPrice,
            GoldCost = (int)System.Math.Ceiling(breakdown["final"]),
            PowerPointCost = powerPointCost,
            PricingBreakdown = breakdown
        };
    }

    private static Dictionary<string, double> CalculateShopPrice(CardContentDefinition card, ShopPricingRules pricing, int? explicitGoldCost)
    {
        var basePrice = explicitGoldCost.GetValueOrDefault(card.BaseGoldPrice);
        var rarityMultiplier = pricing.RarityMultipliers.TryGetValue(card.Rarity, out var rarityValue) ? rarityValue : 1.0;
        var tagMultiplier = card.Tags
            .Select(tag => pricing.TagMultipliers.TryGetValue(tag, out var value) ? value : 1.0)
            .Aggregate(1.0, (current, value) => current * value);
        var final = basePrice * pricing.BaseMultiplier * rarityMultiplier * tagMultiplier;

        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["base"] = basePrice,
            ["baseMultiplier"] = pricing.BaseMultiplier,
            ["rarityMultiplier"] = rarityMultiplier,
            ["tagMultiplier"] = tagMultiplier,
            ["final"] = final
        };
    }

    private static int CalculateShopRerollCost(ShopRerollRules rules, int rerollsUsed)
    {
        return rules.BaseGoldCost + System.Math.Max(0, rerollsUsed) * rules.GoldCostPerReroll;
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

    private Result<PreparationDefinition> LoadPreparationDefinition(string configName, string preparationId)
    {
        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName);
            var data = _resourceLoader.LoadResource($"preparations/{preparationId}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<PreparationDefinition>.Failure($"Preparation definition not found: {preparationId}");

            var element = data.TryGetValue(preparationId, out var exact) ? exact : data.Values.First();
            var definition = JsonSerializer.Deserialize<PreparationDefinition>(element.GetRawText(), _jsonOptions);
            return definition == null
                ? Result<PreparationDefinition>.Failure($"Failed to deserialize preparation definition: {preparationId}")
                : Result<PreparationDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<PreparationDefinition>.Failure($"Could not load preparation definition '{preparationId}': {ex.Message}", ex);
        }
    }

    private Result<IReadOnlyList<string>> MoveCards(
        Guid runId,
        IReadOnlyList<string> cardIds,
        CardConsumeDestination destination)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<IReadOnlyList<string>>.Failure($"Run not found: {runId}");

            var transition = DeckTransitions.MoveFromHand(state.Deck, cardIds, destination, state.Determinism);
            if (transition.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(transition.Error);

            state = state with
            {
                Deck = transition.Value.State,
                Determinism = transition.Value.Context.AdvanceStep()
            };
            _runs[runId] = state;
            PersistAsync(state);

            return Result<IReadOnlyList<string>>.Success(transition.Value.Cards);
        }
    }

    /// <summary>
    /// Restores a run state without incrementing sequence or triggering persistence.
    /// Used for time-travel operations (undo).
    /// </summary>
    public Result<RunState> RestoreState(RunState state)
    {
        if (state == null)
            return Result<RunState>.Failure("State cannot be null");

        RunState restored;
        lock (_lock)
        {
            restored = CloneRunState(state);
            _runs[state.RunId] = restored;
        }

        return Result<RunState>.Success(restored);
    }

    /// <summary>
    /// Fire-and-forget persistence. Failures are swallowed to avoid disrupting game flow.
    /// Increments snapshot sequence before saving.
    /// </summary>
    private RunState PersistAsync(RunState state)
    {
        if (_repository == null) return state;

        RunState snapshot;
        lock (_persistenceLock)
        {
            state = state with { Sequence = checked(state.Sequence + 1) };
            _runs[state.RunId] = state;
            snapshot = CreateSnapshot(state);
        }
        
        _ = Task.Run(async () =>
        {
            try { await _repository.SaveAsync(snapshot).ConfigureAwait(false); }
            catch { /* best effort — repository implementations log internally */ }
        });

        return state;
    }

    private RunState CreateSnapshot(RunState state)
    {
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        return JsonSerializer.Deserialize<RunState>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to create a run-state snapshot");
    }

    private static int SaturatingEconomyChange(int current, int amount)
    {
        return (int)System.Math.Clamp((long)current + amount, 0, int.MaxValue);
    }

    private static ulong CreateSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }

    private static ImmutableDictionary<string, JsonElement> ToImmutableMetadata(
        IReadOnlyDictionary<string, object> metadata)
    {
        return metadata.ToImmutableDictionary(
            entry => entry.Key,
            entry => JsonSerializer.SerializeToElement(entry.Value).Clone(),
            StringComparer.OrdinalIgnoreCase);
    }
}
