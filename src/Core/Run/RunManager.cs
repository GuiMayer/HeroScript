using Core.Abstractions.Persistence;
using Core.Common;
using Core.Combat.Modifiers;
using Core.Config;
using Core.Content;
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
    private readonly IContentManifestProvider? _contentManifestProvider;
    private readonly Dictionary<Guid, RunState> _runs = new();
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public RunManager(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        ICardPoolResolver? cardPoolResolver = null,
        ICardContentCatalog? cardContentCatalog = null,
        IScriptModifierManager? scriptModifierManager = null,
        IEventBus? eventBus = null,
        IRunStateRepository? repository = null,
        IContentManifestProvider? contentManifestProvider = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _cardPoolResolver = cardPoolResolver;
        _cardContentCatalog = cardContentCatalog;
        _scriptModifierManager = scriptModifierManager;
        _eventBus = eventBus;
        _repository = repository;
        _contentManifestProvider = contentManifestProvider;
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
        var manifestResult = ResolveContentManifest(options, definition);
        if (manifestResult.IsFailure)
            return Result<RunState>.Failure(manifestResult.Error);

        var manifest = manifestResult.Value.Manifest;
        var contentRevision = manifestResult.Value.Revision;
        var seed = options.Seed ?? CreateSeed();
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
            ContentManifest = manifest,
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

            var persisted = Persist(state, "run.start", options);
            if (persisted.IsFailure)
                return Result<RunState>.Failure(persisted.Error);
            state = persisted.Value;
        }

        _eventBus?.Publish(new RunStartedEvent(state.RunId, options.ConfigName, options.PlayerEntityId, definition.StartingGold, definition.StartingPowerPoints));
        if (!initialDraw.Value.Cards.IsEmpty)
            _eventBus?.Publish(new CardDrawnEvent(state.RunId, initialDraw.Value.Cards));

        return Result<RunState>.Success(state);
    }

    private Result<ResolvedContentManifest> ResolveContentManifest(
        RunStartOptions options,
        RunDefinition definition)
    {
        if (_contentManifestProvider == null)
        {
            // Compatibility boundary for isolated callers that have not registered
            // the content catalog. The production composition always supplies it.
            var revision = string.IsNullOrWhiteSpace(options.ContentRevision)
                ? CanonicalJson.ComputeHash(definition, _jsonOptions)
                : options.ContentRevision;
            return Result<ResolvedContentManifest>.Success(
                new ResolvedContentManifest(revision!, null));
        }

        var manifestResult = _contentManifestProvider.GetManifest(options.ConfigName);
        if (manifestResult.IsFailure)
            return Result<ResolvedContentManifest>.Failure(manifestResult.Error);

        var manifest = manifestResult.Value;
        if (!string.IsNullOrWhiteSpace(options.ContentRevision) &&
            !string.Equals(options.ContentRevision, manifest.Revision, StringComparison.Ordinal))
        {
            return Result<ResolvedContentManifest>.Failure(
                $"Requested content revision '{options.ContentRevision}' is not active for configuration " +
                $"'{options.ConfigName}'. Active revision: {manifest.Revision}");
        }

        return Result<ResolvedContentManifest>.Success(
            new ResolvedContentManifest(manifest.Revision, manifest));
    }

    private sealed record ResolvedContentManifest(string Revision, ContentManifest? Manifest);

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
            var persisted = Persist(
                state,
                "run.economy.apply",
                new { resource, amount });
            if (persisted.IsFailure)
                return Result<RunState>.Failure(persisted.Error);
            state = persisted.Value;
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
                var persisted = Persist(state, "run.deck.draw", new { count });
                if (persisted.IsFailure)
                    return Result<IReadOnlyList<string>>.Failure(persisted.Error);
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
                var persisted = Persist(
                    state,
                    "run.deck.add-to-hand",
                    new { cardIds });
                if (persisted.IsFailure)
                    return Result<IReadOnlyList<string>>.Failure(persisted.Error);
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
                var persisted = Persist(
                    state,
                    "run.deck.consume",
                    new { cardIds, destination = destination.ToString() });
                if (persisted.IsFailure)
                    return Result<IReadOnlyList<string>>.Failure(persisted.Error);
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
                var persisted = Persist(state, "run.deck.shuffle-discard", new { });
                if (persisted.IsFailure)
                    return Result.Failure(persisted.Error);
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
            var options = GenerateCardSelectionOptions(
                state,
                definition,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var transition = CardSelectionTransitions.Create(state, definition, options);
            return CommitTransition(
                transition,
                "run.card-selection.create",
                new { selectionId });
        }
    }

    public Result<CardSelectionState> PickCards(Guid runId, Guid selectionInstanceId, IReadOnlyList<string> cardIds)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var transition = CardSelectionTransitions.Pick(state, selectionInstanceId, cardIds);
            return transition.IsFailure
                ? Result<CardSelectionState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.card-selection.pick",
                    new { selectionInstanceId, cardIds });
        }
    }

    public Result<CardSelectionState> RerollCardSelection(Guid runId, Guid selectionInstanceId, IReadOnlyList<string>? lockedCardIds = null)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var selection = state.CardSelections.FirstOrDefault(item => item.SelectionInstanceId == selectionInstanceId);
            if (selection == null)
                return Result<CardSelectionState>.Failure($"Card selection not found: {selectionInstanceId}");

            var definitionResult = LoadCardSelectionDefinition(state.ConfigName, selection.SelectionId);
            if (definitionResult.IsFailure)
                return Result<CardSelectionState>.Failure(definitionResult.Error);

            var locked = new HashSet<string>(lockedCardIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var generated = GenerateCardSelectionOptions(state, definitionResult.Value, locked);
            var transition = CardSelectionTransitions.Reroll(state, selectionInstanceId, generated);
            return transition.IsFailure
                ? Result<CardSelectionState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.card-selection.reroll",
                    new { selectionInstanceId, lockedCardIds });
        }
    }

    public Result<CardSelectionState> DecomposeCardSelectionOption(Guid runId, Guid selectionInstanceId, string cardId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<CardSelectionState>.Failure($"Run not found: {runId}");

            var transition = CardSelectionTransitions.Decompose(state, selectionInstanceId, cardId);
            return transition.IsFailure
                ? Result<CardSelectionState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.card-selection.decompose",
                    new { selectionInstanceId, cardId });
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
            var transition = ShopTransitions.Create(state, definition, GenerateShopItems(state, definition));
            return CommitTransition(
                transition,
                "run.shop.create",
                new { shopId });
        }
    }

    public Result<ShopItemState> BuyShopItem(Guid runId, Guid shopInstanceId, string itemId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<ShopItemState>.Failure($"Run not found: {runId}");

            var transition = ShopTransitions.Buy(state, shopInstanceId, itemId);
            return transition.IsFailure
                ? Result<ShopItemState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.shop.buy",
                    new { shopInstanceId, itemId });
        }
    }

    public Result<ShopState> RerollShop(Guid runId, Guid shopInstanceId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<ShopState>.Failure($"Run not found: {runId}");

            var shop = state.Shops.FirstOrDefault(item => item.ShopInstanceId == shopInstanceId);
            if (shop == null)
                return Result<ShopState>.Failure($"Shop not found: {shopInstanceId}");

            var definitionResult = LoadShopDefinition(state.ConfigName, shop.ShopId);
            if (definitionResult.IsFailure)
                return Result<ShopState>.Failure(definitionResult.Error);

            var items = GenerateShopItems(state, definitionResult.Value, checked(shop.RerollsUsed + 1));
            var transition = ShopTransitions.Reroll(state, shopInstanceId, items);
            return transition.IsFailure
                ? Result<ShopState>.Failure(transition.Error)
                : CommitTransition(
                    transition.Value,
                    "run.shop.reroll",
                    new { shopInstanceId });
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

            var transition = PreparationTransitions.Create(state, definitionResult.Value);
            return CommitTransition(
                transition,
                "run.preparation.create",
                new { preparationId });
        }
    }

    public Result<PreparationOptionState> ApplyPreparationOption(Guid runId, Guid preparationInstanceId, string optionId)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out var state))
                return Result<PreparationOptionState>.Failure($"Run not found: {runId}");

            var plan = PreparationTransitions.PlanApply(state, preparationInstanceId, optionId);
            if (plan.IsFailure)
                return Result<PreparationOptionState>.Failure(plan.Error);
            if (!plan.Value.ModifierGrants.IsEmpty && _scriptModifierManager == null)
                return Result<PreparationOptionState>.Failure(
                    "Script modifier manager is not available for preparation modifier grants");

            var appliedModifiers = new List<(string OwnerId, Guid InstanceId)>();
            foreach (var grant in plan.Value.ModifierGrants)
            {
                var apply = _scriptModifierManager!.ApplyModifier(
                    grant.InstanceId,
                    grant.OwnerId,
                    grant.ModifierId,
                    grant.Stacks,
                    grant.Duration,
                    grant.SourceId);
                if (apply.IsFailure)
                {
                    var rollback = RollbackAppliedModifiers(appliedModifiers);
                    return rollback.IsFailure
                        ? Result<PreparationOptionState>.Failure($"{apply.Error}; modifier rollback failed: {rollback.Error}")
                        : Result<PreparationOptionState>.Failure(apply.Error);
                }

                appliedModifiers.Add((grant.OwnerId, apply.Value.InstanceId));
            }

            var transition = PreparationTransitions.CommitApply(
                state,
                plan.Value,
                appliedModifiers.Select(item => item.InstanceId).ToArray());
            if (transition.IsFailure)
            {
                var rollback = RollbackAppliedModifiers(appliedModifiers);
                return rollback.IsFailure
                    ? Result<PreparationOptionState>.Failure($"{transition.Error}; modifier rollback failed: {rollback.Error}")
                    : Result<PreparationOptionState>.Failure(transition.Error);
            }

            var committed = CommitTransition(
                transition.Value,
                "run.preparation.apply",
                new { preparationInstanceId, optionId });
            if (committed.IsSuccess)
                return committed;

            var persistenceRollback = RollbackAppliedModifiers(appliedModifiers);
            return persistenceRollback.IsFailure
                ? Result<PreparationOptionState>.Failure(
                    $"{committed.Error}; modifier rollback failed: {persistenceRollback.Error}")
                : committed;
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

    private Result<T> CommitTransition<T>(
        RunStateTransition<T> transition,
        string commandType,
        object command)
    {
        var persisted = Persist(transition.State, commandType, command);
        return persisted.IsSuccess
            ? Result<T>.Success(transition.Value)
            : Result<T>.Failure(persisted.Error);
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
            var persisted = Persist(
                state,
                "run.deck.move",
                new { cardIds, destination = destination.ToString() });
            if (persisted.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(persisted.Error);

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

        lock (_lock)
        {
            var candidate = _runs.TryGetValue(state.RunId, out var current)
                ? state with
                {
                    Sequence = current.Sequence,
                    Determinism = current.Determinism.AdvanceStep()
                }
                : state with { Determinism = state.Determinism.AdvanceStep() };
            var restored = Persist(
                candidate,
                "run.restore",
                new { targetSequence = state.Sequence });
            return restored.IsSuccess
                ? restored
                : Result<RunState>.Failure(restored.Error);
        }
    }

    /// <summary>
    /// Cria e grava um checkpoint antes de publicar o novo snapshot em memória.
    /// Falhas de durabilidade são devolvidas ao chamador e não alteram a run ativa.
    /// </summary>
    private Result<RunState> Persist(
        RunState state,
        string commandType,
        object command)
    {
        _runs.TryGetValue(state.RunId, out var previous);
        try
        {
            var nextSequence = checked((previous?.Sequence ?? 0) + 1);
            state = state with { Sequence = nextSequence };
            var snapshot = CreateSnapshot(state);
            var stateHash = CanonicalJson.ComputeHash(snapshot);
            var previousHash = previous == null
                ? string.Empty
                : CanonicalJson.ComputeHash(previous);
            var entry = new RunJournalEntry
            {
                RunId = snapshot.RunId,
                Sequence = snapshot.Sequence,
                Step = snapshot.Determinism.Step,
                CommandType = commandType,
                Command = JsonSerializer.SerializeToElement(command, _jsonOptions).Clone(),
                PreviousStateHash = previousHash,
                StateHash = stateHash,
                LogicalTimestamp = snapshot.Determinism.LogicalTimestamp.UtcDateTime
            };

            if (_repository is IRunCheckpointRepository checkpoints)
            {
                checkpoints.SaveCheckpointAsync(new RunCheckpoint(snapshot, entry))
                    .GetAwaiter().GetResult();
            }
            else if (_repository != null)
            {
                _repository.SaveAsync(snapshot).GetAwaiter().GetResult();
            }

            _runs[state.RunId] = state;
            return Result<RunState>.Success(state);
        }
        catch (Exception exception)
        {
            if (previous == null)
                _runs.Remove(state.RunId);
            else
                _runs[state.RunId] = previous;
            return Result<RunState>.Failure(
                $"Failed to persist run transition '{commandType}': {exception.Message}",
                exception);
        }
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
