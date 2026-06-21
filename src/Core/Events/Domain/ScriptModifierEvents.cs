namespace Core.Events.Domain;

/// <summary>
/// Published when a script modifier is applied to an owner.
/// </summary>
public sealed record ModifierAppliedEvent : GameEvent
{
    public string OwnerId { get; init; } = string.Empty;
    public string ModifierId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }
    public int Stacks { get; init; }
    public int? Duration { get; init; }
    public string? SourceId { get; init; }

    public ModifierAppliedEvent(string ownerId, string modifierId, Guid instanceId, int stacks, int? duration, string? sourceId)
    {
        OwnerId = ownerId;
        ModifierId = modifierId;
        InstanceId = instanceId;
        Stacks = stacks;
        Duration = duration;
        SourceId = sourceId;
        EventType = nameof(ModifierAppliedEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.INFO;
        Subject = ownerId;
        Verb = "modifier_applied";
        Target = modifierId;
        Payload = new Dictionary<string, object>
        {
            ["ownerId"] = ownerId,
            ["modifierId"] = modifierId,
            ["instanceId"] = instanceId,
            ["stacks"] = stacks,
            ["duration"] = duration?.ToString() ?? "permanent",
            ["sourceId"] = sourceId ?? string.Empty
        };
    }
}

/// <summary>
/// Published each time a modifier's duration ticks down.
/// </summary>
public sealed record ModifierTickedEvent : GameEvent
{
    public string OwnerId { get; init; } = string.Empty;
    public string ModifierId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }
    public int RemainingDuration { get; init; }

    public ModifierTickedEvent(string ownerId, string modifierId, Guid instanceId, int remainingDuration)
    {
        OwnerId = ownerId;
        ModifierId = modifierId;
        InstanceId = instanceId;
        RemainingDuration = remainingDuration;
        EventType = nameof(ModifierTickedEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.DEBUG;
        Subject = ownerId;
        Verb = "modifier_ticked";
        Target = modifierId;
        Payload = new Dictionary<string, object>
        {
            ["ownerId"] = ownerId,
            ["modifierId"] = modifierId,
            ["instanceId"] = instanceId,
            ["remainingDuration"] = remainingDuration
        };
    }
}

/// <summary>
/// Published when a modifier's duration reaches zero and it is removed automatically.
/// </summary>
public sealed record ModifierExpiredEvent : GameEvent
{
    public string OwnerId { get; init; } = string.Empty;
    public string ModifierId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }

    public ModifierExpiredEvent(string ownerId, string modifierId, Guid instanceId)
    {
        OwnerId = ownerId;
        ModifierId = modifierId;
        InstanceId = instanceId;
        EventType = nameof(ModifierExpiredEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.INFO;
        Subject = ownerId;
        Verb = "modifier_expired";
        Target = modifierId;
        Payload = new Dictionary<string, object>
        {
            ["ownerId"] = ownerId,
            ["modifierId"] = modifierId,
            ["instanceId"] = instanceId
        };
    }
}

/// <summary>
/// Published when a modifier is explicitly removed before expiry.
/// </summary>
public sealed record ModifierRemovedEvent : GameEvent
{
    public string OwnerId { get; init; } = string.Empty;
    public string ModifierId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }

    public ModifierRemovedEvent(string ownerId, string modifierId, Guid instanceId)
    {
        OwnerId = ownerId;
        ModifierId = modifierId;
        InstanceId = instanceId;
        EventType = nameof(ModifierRemovedEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.INFO;
        Subject = ownerId;
        Verb = "modifier_removed";
        Target = modifierId;
        Payload = new Dictionary<string, object>
        {
            ["ownerId"] = ownerId,
            ["modifierId"] = modifierId,
            ["instanceId"] = instanceId
        };
    }
}

/// <summary>
/// Published when an attempt to apply a modifier is rejected (e.g. definition not found).
/// </summary>
public sealed record ModifierRejectedEvent : GameEvent
{
    public string OwnerId { get; init; } = string.Empty;
    public string ModifierId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;

    public ModifierRejectedEvent(string ownerId, string modifierId, string reason)
    {
        OwnerId = ownerId;
        ModifierId = modifierId;
        Reason = reason;
        EventType = nameof(ModifierRejectedEvent);
        Category = EventCategory.GAME;
        Severity = EventSeverity.WARN;
        Subject = ownerId;
        Verb = "modifier_rejected";
        Target = modifierId;
        Payload = new Dictionary<string, object>
        {
            ["ownerId"] = ownerId,
            ["modifierId"] = modifierId,
            ["reason"] = reason
        };
    }
}
