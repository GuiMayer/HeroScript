using System.Collections.Immutable;
using Core.Resources;

namespace Core.Events.Domain;

/// <summary>
/// Published when a new run session is created and initial state is set up.
/// </summary>
public sealed record RunStartedEvent : GameEvent
{
    public Guid RunId { get; init; }
    public string ConfigName { get; init; } = string.Empty;
    public string PlayerEntityId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, float> StartingResources { get; init; }

    public RunStartedEvent(
        Guid runId,
        string configName,
        string playerEntityId,
        IReadOnlyDictionary<string, float> startingResources)
    {
        RunId = runId;
        ConfigName = configName;
        PlayerEntityId = playerEntityId;
        StartingResources = new Dictionary<string, float>(
            startingResources,
            StringComparer.OrdinalIgnoreCase);
        EventType = nameof(RunStartedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = playerEntityId;
        Verb = "run_started";
        Target = runId.ToString();
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["configName"] = configName,
            ["playerEntityId"] = playerEntityId,
            ["startingResources"] = StartingResources
        };
    }
}

/// <summary>
/// Published when a run session ends (win, loss, or abandon).
/// </summary>
public sealed record RunEndedEvent : GameEvent
{
    public Guid RunId { get; init; }
    public string Outcome { get; init; } = string.Empty;

    public RunEndedEvent(Guid runId, string outcome)
    {
        RunId = runId;
        Outcome = outcome;
        EventType = nameof(RunEndedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "run_ended";
        Target = outcome;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["outcome"] = outcome
        };
    }
}

/// <summary>
/// Published when any field of an owner-scoped run resource changes.
/// </summary>
public sealed record RunResourceChangedEvent : GameEvent
{
    public Guid RunId { get; init; }
    public string ResourceId { get; init; } = string.Empty;
    public ResourceValueField Field { get; init; }
    public ResourceMutationOperation Operation { get; init; }
    public float OldValue { get; init; }
    public float NewValue { get; init; }
    public float ValueDelta { get; init; }

    public RunResourceChangedEvent(
        Guid runId,
        string resourceId,
        ResourceValueField field,
        ResourceMutationOperation operation,
        float oldValue,
        float newValue)
    {
        RunId = runId;
        ResourceId = resourceId;
        Field = field;
        Operation = operation;
        OldValue = oldValue;
        NewValue = newValue;
        ValueDelta = newValue - oldValue;
        EventType = nameof(RunResourceChangedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "run_resource_changed";
        Target = resourceId;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["resourceId"] = resourceId,
            ["field"] = field.ToString(),
            ["operation"] = operation.ToString(),
            ["oldValue"] = oldValue,
            ["newValue"] = newValue,
            ["delta"] = ValueDelta
        };
    }
}

/// <summary>
/// Published when one or more cards are drawn from the draw pile into hand.
/// </summary>
public sealed record CardDrawnEvent : GameEvent
{
    private ImmutableList<string> _cardIds = [];

    public Guid RunId { get; init; }
    public IReadOnlyList<string> CardIds
    {
        get => _cardIds;
        init => _cardIds = value?.ToImmutableList() ?? [];
    }

    public CardDrawnEvent(Guid runId, IReadOnlyList<string> cardIds)
    {
        RunId = runId;
        CardIds = cardIds;
        EventType = nameof(CardDrawnEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.DEBUG;
        Subject = runId.ToString();
        Verb = "cards_drawn";
        Target = string.Join(",", cardIds);
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["cardIds"] = cardIds.ToArray(),
            ["count"] = cardIds.Count
        };
    }
}

/// <summary>
/// Published when cards are discarded from hand.
/// </summary>
public sealed record CardDiscardedEvent : GameEvent
{
    private ImmutableList<string> _cardIds = [];

    public Guid RunId { get; init; }
    public IReadOnlyList<string> CardIds
    {
        get => _cardIds;
        init => _cardIds = value?.ToImmutableList() ?? [];
    }

    public CardDiscardedEvent(Guid runId, IReadOnlyList<string> cardIds)
    {
        RunId = runId;
        CardIds = cardIds;
        EventType = nameof(CardDiscardedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.DEBUG;
        Subject = runId.ToString();
        Verb = "cards_discarded";
        Target = string.Join(",", cardIds);
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["cardIds"] = cardIds.ToArray(),
            ["count"] = cardIds.Count
        };
    }
}

/// <summary>
/// Published when a new card is permanently added to the run deck.
/// </summary>
public sealed record CardAddedToDeckEvent : GameEvent
{
    public Guid RunId { get; init; }
    public string CardId { get; init; } = string.Empty;

    public CardAddedToDeckEvent(Guid runId, string cardId)
    {
        RunId = runId;
        CardId = cardId;
        EventType = nameof(CardAddedToDeckEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "card_added_to_deck";
        Target = cardId;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["cardId"] = cardId
        };
    }
}

/// <summary>
/// Published when a card reward selection is generated for the player.
/// </summary>
public sealed record RewardGeneratedEvent : GameEvent
{
    private ImmutableList<string> _offeredCardIds = [];

    public Guid RunId { get; init; }
    public Guid SelectionId { get; init; }
    public IReadOnlyList<string> OfferedCardIds
    {
        get => _offeredCardIds;
        init => _offeredCardIds = value?.ToImmutableList() ?? [];
    }

    public RewardGeneratedEvent(Guid runId, Guid selectionId, IReadOnlyList<string> offeredCardIds)
    {
        RunId = runId;
        SelectionId = selectionId;
        OfferedCardIds = offeredCardIds;
        EventType = nameof(RewardGeneratedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "reward_generated";
        Target = selectionId.ToString();
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["selectionId"] = selectionId,
            ["offeredCardIds"] = offeredCardIds.ToArray()
        };
    }
}

/// <summary>
/// Published when the player picks a card from a reward selection.
/// </summary>
public sealed record CardRewardPickedEvent : GameEvent
{
    public Guid RunId { get; init; }
    public Guid SelectionId { get; init; }
    public string PickedCardId { get; init; } = string.Empty;

    public CardRewardPickedEvent(Guid runId, Guid selectionId, string pickedCardId)
    {
        RunId = runId;
        SelectionId = selectionId;
        PickedCardId = pickedCardId;
        EventType = nameof(CardRewardPickedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "card_reward_picked";
        Target = pickedCardId;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["selectionId"] = selectionId,
            ["pickedCardId"] = pickedCardId
        };
    }
}

/// <summary>
/// Published when the player rerolls a card reward selection.
/// </summary>
public sealed record CardRewardRerolledEvent : GameEvent
{
    public Guid RunId { get; init; }
    public Guid SelectionId { get; init; }
    public int Cost { get; init; }

    public CardRewardRerolledEvent(Guid runId, Guid selectionId, int cost)
    {
        RunId = runId;
        SelectionId = selectionId;
        Cost = cost;
        EventType = nameof(CardRewardRerolledEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "card_reward_rerolled";
        Target = selectionId.ToString();
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["selectionId"] = selectionId,
            ["cost"] = cost
        };
    }
}

/// <summary>
/// Published when the player decomposes (sacrifices) a card from their deck.
/// </summary>
public sealed record CardDecomposedEvent : GameEvent
{
    public Guid RunId { get; init; }
    public string CardId { get; init; } = string.Empty;
    public int PowerPointsGained { get; init; }

    public CardDecomposedEvent(Guid runId, string cardId, int powerPointsGained)
    {
        RunId = runId;
        CardId = cardId;
        PowerPointsGained = powerPointsGained;
        EventType = nameof(CardDecomposedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "card_decomposed";
        Target = cardId;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["cardId"] = cardId,
            ["powerPointsGained"] = powerPointsGained
        };
    }
}

/// <summary>
/// Published when a shop is opened for the player.
/// </summary>
public sealed record ShopOpenedEvent : GameEvent
{
    private ImmutableList<string> _itemIds = [];

    public Guid RunId { get; init; }
    public string ShopId { get; init; } = string.Empty;
    public IReadOnlyList<string> ItemIds
    {
        get => _itemIds;
        init => _itemIds = value?.ToImmutableList() ?? [];
    }

    public ShopOpenedEvent(Guid runId, string shopId, IReadOnlyList<string> itemIds)
    {
        RunId = runId;
        ShopId = shopId;
        ItemIds = itemIds;
        EventType = nameof(ShopOpenedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "shop_opened";
        Target = shopId;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["shopId"] = shopId,
            ["itemIds"] = itemIds.ToArray()
        };
    }
}

/// <summary>
/// Published when the player purchases an item from the shop.
/// </summary>
public sealed record ShopItemPurchasedEvent : GameEvent
{
    public Guid RunId { get; init; }
    public string ShopId { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public int GoldSpent { get; init; }

    public ShopItemPurchasedEvent(Guid runId, string shopId, string itemId, int goldSpent)
    {
        RunId = runId;
        ShopId = shopId;
        ItemId = itemId;
        GoldSpent = goldSpent;
        EventType = nameof(ShopItemPurchasedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "shop_item_purchased";
        Target = itemId;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["shopId"] = shopId,
            ["itemId"] = itemId,
            ["goldSpent"] = goldSpent
        };
    }
}

/// <summary>
/// Published when the player rerolls shop inventory.
/// </summary>
public sealed record ShopRerolledEvent : GameEvent
{
    public Guid RunId { get; init; }
    public string ShopId { get; init; } = string.Empty;
    public int GoldSpent { get; init; }

    public ShopRerolledEvent(Guid runId, string shopId, int goldSpent)
    {
        RunId = runId;
        ShopId = shopId;
        GoldSpent = goldSpent;
        EventType = nameof(ShopRerolledEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "shop_rerolled";
        Target = shopId;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["shopId"] = shopId,
            ["goldSpent"] = goldSpent
        };
    }
}

/// <summary>
/// Published when a preparation (buff/setup) is applied between combats.
/// </summary>
public sealed record PreparationAppliedEvent : GameEvent
{
    public Guid RunId { get; init; }
    public string PreparationId { get; init; } = string.Empty;

    public PreparationAppliedEvent(Guid runId, string preparationId)
    {
        RunId = runId;
        PreparationId = preparationId;
        EventType = nameof(PreparationAppliedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.INFO;
        Subject = runId.ToString();
        Verb = "preparation_applied";
        Target = preparationId;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["preparationId"] = preparationId
        };
    }
}

/// <summary>
/// Published when the player's deck composition changes (cards added, removed, or upgraded).
/// </summary>
public sealed record DeckChangedEvent : GameEvent
{
    public Guid RunId { get; init; }
    public int TotalCards { get; init; }
    public string ChangeReason { get; init; } = string.Empty;

    public DeckChangedEvent(Guid runId, int totalCards, string changeReason)
    {
        RunId = runId;
        TotalCards = totalCards;
        ChangeReason = changeReason;
        EventType = nameof(DeckChangedEvent);
        Category = EventCategory.RUN;
        Severity = EventSeverity.DEBUG;
        Subject = runId.ToString();
        Verb = "deck_changed";
        Target = changeReason;
        Payload = new Dictionary<string, object>
        {
            ["runId"] = runId,
            ["totalCards"] = totalCards,
            ["changeReason"] = changeReason
        };
    }
}
