using System.Collections.Immutable;

namespace Core.Events.Domain;

public sealed record ActivationStartedEvent : GameEvent
{
    private ImmutableList<string> _affectedCardInstanceIds = [];

    public Guid CombatId { get; init; }
    public Guid? RunId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public int Round { get; init; }
    public int ActivationIndex { get; init; }
    public IReadOnlyList<string> AffectedCardInstanceIds
    {
        get => _affectedCardInstanceIds;
        init => _affectedCardInstanceIds = value?.ToImmutableList() ?? [];
    }

    public ActivationStartedEvent(Guid combatId, Guid? runId, string actorId, int round, int activationIndex, IReadOnlyList<string> affectedCardInstanceIds, int turn)
    {
        CombatId = combatId;
        RunId = runId;
        ActorId = actorId;
        Round = round;
        ActivationIndex = activationIndex;
        AffectedCardInstanceIds = affectedCardInstanceIds;
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
            ["affectedCardInstanceIds"] = affectedCardInstanceIds.ToArray()
        };
    }
}

public sealed record ActivationEndedEvent : GameEvent
{
    private ImmutableList<string> _affectedCardInstanceIds = [];

    public Guid CombatId { get; init; }
    public Guid? RunId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public int Round { get; init; }
    public int ActivationIndex { get; init; }
    public IReadOnlyList<string> AffectedCardInstanceIds
    {
        get => _affectedCardInstanceIds;
        init => _affectedCardInstanceIds = value?.ToImmutableList() ?? [];
    }

    public ActivationEndedEvent(Guid combatId, Guid? runId, string actorId, int round, int activationIndex, IReadOnlyList<string> affectedCardInstanceIds, int turn)
    {
        CombatId = combatId;
        RunId = runId;
        ActorId = actorId;
        Round = round;
        ActivationIndex = activationIndex;
        AffectedCardInstanceIds = affectedCardInstanceIds;
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
            ["affectedCardInstanceIds"] = affectedCardInstanceIds.ToArray()
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
