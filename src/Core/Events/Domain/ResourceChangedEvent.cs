using Core.Resources;

namespace Core.Events.Domain;

/// <summary>
/// Published for an observed resource field change caused by a combat action.
/// The resource id and field are content data; the event assigns no privileged
/// meaning to health, energy or any other resource.
/// </summary>
public sealed record ResourceChangedEvent : GameEvent
{
    public Guid CombatId { get; init; }
    public Guid ActionId { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public ResourceValueField Field { get; init; }
    public float PreviousValue { get; init; }
    public float CurrentValue { get; init; }
    public float SignedAmount => CurrentValue - PreviousValue;
    public string Reason { get; init; } = string.Empty;

    public ResourceChangedEvent()
    {
        EventType = nameof(ResourceChangedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.DEBUG;
        Verb = "changed";
    }
}
