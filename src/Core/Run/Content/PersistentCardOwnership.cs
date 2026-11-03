using Core.CardZones;

namespace Core.Run.Content;

/// <summary>Shared ownership rule for persistent character build associations, never zone semantics.</summary>
public static class PersistentCardOwnership
{
    public static bool Owns(RunState run, string actorId, CardInstanceState card) => actorId == run.PlayerEntityId &&
        (card.OwnerId == actorId || card.OwnerId == "$run") && card.Persistence == CardInstancePersistence.Run &&
        card.Lifetime.Strategy == CardInstanceLifetimeStrategy.Run;
}
