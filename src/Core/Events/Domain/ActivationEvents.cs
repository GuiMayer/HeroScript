using System.Collections.Immutable;

namespace Core.Events.Domain;

public sealed record ActivationStartedEvent : GameEvent
{
    private ImmutableList<string> _drawnCardIds = [];

    public Guid CombatId { get; init; }
    public Guid? RunId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public int Round { get; init; }
    public int ActivationIndex { get; init; }
    public IReadOnlyList<string> DrawnCardIds
    {
        get => _drawnCardIds;
        init => _drawnCardIds = value?.ToImmutableList() ?? [];
    }

    public ActivationStartedEvent(Guid combatId, Guid? runId, string actorId, int round, int activationIndex, IReadOnlyList<string> drawnCardIds, int turn)
    {
        CombatId = combatId;
        RunId = runId;
        ActorId = actorId;
        Round = round;
        ActivationIndex = activationIndex;
        DrawnCardIds = drawnCardIds;
        EventType = nameof(ActivationStartedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.INFO;
        Subject = actorId;
        Verb = "activation_started";
        Target = combatId.ToString();
        Turn = turn;
        Payload = new Dictionary<string, object>
        {
            ["combatId"] = combatId,
            ["runId"] = runId?.ToString() ?? string.Empty,
            ["actorId"] = actorId,
            ["round"] = round,
            ["activationIndex"] = activationIndex,
            ["drawnCardIds"] = drawnCardIds.ToArray()
        };
    }
}

public sealed record ActivationEndedEvent : GameEvent
{
    private ImmutableList<string> _discardedCardIds = [];

    public Guid CombatId { get; init; }
    public Guid? RunId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public int Round { get; init; }
    public int ActivationIndex { get; init; }
    public IReadOnlyList<string> DiscardedCardIds
    {
        get => _discardedCardIds;
        init => _discardedCardIds = value?.ToImmutableList() ?? [];
    }

    public ActivationEndedEvent(Guid combatId, Guid? runId, string actorId, int round, int activationIndex, IReadOnlyList<string> discardedCardIds, int turn)
    {
        CombatId = combatId;
        RunId = runId;
        ActorId = actorId;
        Round = round;
        ActivationIndex = activationIndex;
        DiscardedCardIds = discardedCardIds;
        EventType = nameof(ActivationEndedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.INFO;
        Subject = actorId;
        Verb = "activation_ended";
        Target = combatId.ToString();
        Turn = turn;
        Payload = new Dictionary<string, object>
        {
            ["combatId"] = combatId,
            ["runId"] = runId?.ToString() ?? string.Empty,
            ["actorId"] = actorId,
            ["round"] = round,
            ["activationIndex"] = activationIndex,
            ["discardedCardIds"] = discardedCardIds.ToArray()
        };
    }
}

public sealed record ActivationAdvancedEvent : GameEvent
{
    public Guid CombatId { get; init; }
    public Guid? RunId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public int Round { get; init; }
    public int ActivationIndex { get; init; }

    public ActivationAdvancedEvent(Guid combatId, Guid? runId, string actorId, int round, int activationIndex, int turn)
    {
        CombatId = combatId;
        RunId = runId;
        ActorId = actorId;
        Round = round;
        ActivationIndex = activationIndex;
        EventType = nameof(ActivationAdvancedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.INFO;
        Subject = actorId;
        Verb = "activation_advanced";
        Target = combatId.ToString();
        Turn = turn;
        Payload = new Dictionary<string, object>
        {
            ["combatId"] = combatId,
            ["runId"] = runId?.ToString() ?? string.Empty,
            ["actorId"] = actorId,
            ["round"] = round,
            ["activationIndex"] = activationIndex
        };
    }
}
