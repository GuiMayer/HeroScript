namespace Core.Abstractions.Persistence;

/// <summary>
/// Filter criteria for querying the event store.
/// </summary>
public record EventStoreFilter(
    int AfterSequence = -1,
    Guid? RunId = null,
    Guid? CombatId = null,
    string? EventType = null,
    int Limit = 500);
