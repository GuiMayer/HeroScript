namespace Core.Events.Domain;

/// <summary>
/// Published when a status effect is first applied to an entity.
/// </summary>
public sealed record StatusAppliedEvent : GameEvent
{
    public Guid TargetId { get; init; }
    public string StatusId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }
    public int Stacks { get; init; }
    public int? Duration { get; init; }
    public Guid? SourceId { get; init; }

    public StatusAppliedEvent(Guid targetId, string statusId, Guid instanceId, int stacks, int? duration, Guid? sourceId)
    {
        TargetId = targetId;
        StatusId = statusId;
        InstanceId = instanceId;
        Stacks = stacks;
        Duration = duration;
        SourceId = sourceId;
        EventType = nameof(StatusAppliedEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.INFO;
        Subject = targetId.ToString();
        Verb = "status_applied";
        Target = statusId;
        Payload = new Dictionary<string, object>
        {
            ["targetId"] = targetId,
            ["statusId"] = statusId,
            ["instanceId"] = instanceId,
            ["stacks"] = stacks,
            ["duration"] = duration?.ToString() ?? "permanent",
            ["sourceId"] = sourceId?.ToString() ?? string.Empty
        };
    }
}

/// <summary>
/// Published when a status effect is explicitly removed from an entity.
/// </summary>
public sealed record StatusRemovedEvent : GameEvent
{
    public Guid TargetId { get; init; }
    public string StatusId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }
    public string Reason { get; init; } = string.Empty;

    public StatusRemovedEvent(Guid targetId, string statusId, Guid instanceId, string reason = "removed")
    {
        TargetId = targetId;
        StatusId = statusId;
        InstanceId = instanceId;
        Reason = reason;
        EventType = nameof(StatusRemovedEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.INFO;
        Subject = targetId.ToString();
        Verb = "status_removed";
        Target = statusId;
        Payload = new Dictionary<string, object>
        {
            ["targetId"] = targetId,
            ["statusId"] = statusId,
            ["instanceId"] = instanceId,
            ["reason"] = reason
        };
    }
}

/// <summary>
/// Published when a status effect's stack count changes (added or removed stacks on existing instance).
/// </summary>
public sealed record StatusStackChangedEvent : GameEvent
{
    public Guid TargetId { get; init; }
    public string StatusId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }
    public int OldStacks { get; init; }
    public int NewStacks { get; init; }

    public StatusStackChangedEvent(Guid targetId, string statusId, Guid instanceId, int oldStacks, int newStacks)
    {
        TargetId = targetId;
        StatusId = statusId;
        InstanceId = instanceId;
        OldStacks = oldStacks;
        NewStacks = newStacks;
        EventType = nameof(StatusStackChangedEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.INFO;
        Subject = targetId.ToString();
        Verb = "status_stacks_changed";
        Target = statusId;
        Payload = new Dictionary<string, object>
        {
            ["targetId"] = targetId,
            ["statusId"] = statusId,
            ["instanceId"] = instanceId,
            ["oldStacks"] = oldStacks,
            ["newStacks"] = newStacks,
            ["delta"] = newStacks - oldStacks
        };
    }
}

/// <summary>
/// Published when a status effect's duration is refreshed without changing stacks.
/// </summary>
public sealed record StatusDurationRefreshedEvent : GameEvent
{
    public Guid TargetId { get; init; }
    public string StatusId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }
    public int? OldDuration { get; init; }
    public int? NewDuration { get; init; }

    public StatusDurationRefreshedEvent(Guid targetId, string statusId, Guid instanceId, int? oldDuration, int? newDuration)
    {
        TargetId = targetId;
        StatusId = statusId;
        InstanceId = instanceId;
        OldDuration = oldDuration;
        NewDuration = newDuration;
        EventType = nameof(StatusDurationRefreshedEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.INFO;
        Subject = targetId.ToString();
        Verb = "status_duration_refreshed";
        Target = statusId;
        Payload = new Dictionary<string, object>
        {
            ["targetId"] = targetId,
            ["statusId"] = statusId,
            ["instanceId"] = instanceId,
            ["oldDuration"] = oldDuration?.ToString() ?? "permanent",
            ["newDuration"] = newDuration?.ToString() ?? "permanent"
        };
    }
}

/// <summary>
/// Published when a status effect processes a tick (e.g. DoT, HoT, duration countdown).
/// </summary>
public sealed record StatusTickProcessedEvent : GameEvent
{
    public Guid TargetId { get; init; }
    public string StatusId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }
    public int RemainingDuration { get; init; }
    public float? ValueApplied { get; init; }

    public StatusTickProcessedEvent(Guid targetId, string statusId, Guid instanceId, int remainingDuration, float? valueApplied = null)
    {
        TargetId = targetId;
        StatusId = statusId;
        InstanceId = instanceId;
        RemainingDuration = remainingDuration;
        ValueApplied = valueApplied;
        EventType = nameof(StatusTickProcessedEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.DEBUG;
        Subject = targetId.ToString();
        Verb = "status_ticked";
        Target = statusId;
        Payload = new Dictionary<string, object>
        {
            ["targetId"] = targetId,
            ["statusId"] = statusId,
            ["instanceId"] = instanceId,
            ["remainingDuration"] = remainingDuration,
            ["valueApplied"] = valueApplied?.ToString() ?? "none"
        };
    }
}

/// <summary>
/// Published when a status effect expires naturally (duration reaches zero).
/// </summary>
public sealed record StatusExpiredEvent : GameEvent
{
    public Guid TargetId { get; init; }
    public string StatusId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }

    public StatusExpiredEvent(Guid targetId, string statusId, Guid instanceId)
    {
        TargetId = targetId;
        StatusId = statusId;
        InstanceId = instanceId;
        EventType = nameof(StatusExpiredEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.INFO;
        Subject = targetId.ToString();
        Verb = "status_expired";
        Target = statusId;
        Payload = new Dictionary<string, object>
        {
            ["targetId"] = targetId,
            ["statusId"] = statusId,
            ["instanceId"] = instanceId
        };
    }
}
