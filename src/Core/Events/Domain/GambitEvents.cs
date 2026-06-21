namespace Core.Events.Domain;

/// <summary>
/// Published when the gambit engine is asked to make a decision for an actor.
/// </summary>
public sealed record GambitDecisionRequestedEvent : GameEvent
{
    public Guid CombatId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string GambitId { get; init; } = string.Empty;

    public GambitDecisionRequestedEvent(Guid combatId, string actorId, string gambitId)
    {
        CombatId = combatId;
        ActorId = actorId;
        GambitId = gambitId;
        EventType = nameof(GambitDecisionRequestedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.DEBUG;
        Subject = actorId;
        Verb = "gambit_decision_requested";
        Target = gambitId;
        Payload = new Dictionary<string, object>
        {
            ["combatId"] = combatId,
            ["actorId"] = actorId,
            ["gambitId"] = gambitId
        };
    }
}

/// <summary>
/// Published when a gambit rule matches the current combat state.
/// </summary>
public sealed record GambitRuleMatchedEvent : GameEvent
{
    public Guid CombatId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string GambitId { get; init; } = string.Empty;
    public string RuleId { get; init; } = string.Empty;
    public int Priority { get; init; }

    public GambitRuleMatchedEvent(Guid combatId, string actorId, string gambitId, string ruleId, int priority)
    {
        CombatId = combatId;
        ActorId = actorId;
        GambitId = gambitId;
        RuleId = ruleId;
        Priority = priority;
        EventType = nameof(GambitRuleMatchedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.DEBUG;
        Subject = actorId;
        Verb = "gambit_rule_matched";
        Target = ruleId;
        Payload = new Dictionary<string, object>
        {
            ["combatId"] = combatId,
            ["actorId"] = actorId,
            ["gambitId"] = gambitId,
            ["ruleId"] = ruleId,
            ["priority"] = priority
        };
    }
}

/// <summary>
/// Published when the gambit engine selects an action for an actor to execute.
/// </summary>
public sealed record GambitActionSelectedEvent : GameEvent
{
    public Guid CombatId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string GambitId { get; init; } = string.Empty;
    public string SelectedActionId { get; init; } = string.Empty;
    public string? TargetId { get; init; }

    public GambitActionSelectedEvent(Guid combatId, string actorId, string gambitId, string selectedActionId, string? targetId)
    {
        CombatId = combatId;
        ActorId = actorId;
        GambitId = gambitId;
        SelectedActionId = selectedActionId;
        TargetId = targetId;
        EventType = nameof(GambitActionSelectedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.INFO;
        Subject = actorId;
        Verb = "gambit_action_selected";
        Target = selectedActionId;
        Payload = new Dictionary<string, object>
        {
            ["combatId"] = combatId,
            ["actorId"] = actorId,
            ["gambitId"] = gambitId,
            ["selectedActionId"] = selectedActionId,
            ["targetId"] = targetId ?? string.Empty
        };
    }
}

/// <summary>
/// Published when the gambit engine fails to make a decision (no rules matched, error, etc.).
/// </summary>
public sealed record GambitDecisionFailedEvent : GameEvent
{
    public Guid CombatId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string GambitId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;

    public GambitDecisionFailedEvent(Guid combatId, string actorId, string gambitId, string reason)
    {
        CombatId = combatId;
        ActorId = actorId;
        GambitId = gambitId;
        Reason = reason;
        EventType = nameof(GambitDecisionFailedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.WARN;
        Subject = actorId;
        Verb = "gambit_decision_failed";
        Target = gambitId;
        Payload = new Dictionary<string, object>
        {
            ["combatId"] = combatId,
            ["actorId"] = actorId,
            ["gambitId"] = gambitId,
            ["reason"] = reason
        };
    }
}
