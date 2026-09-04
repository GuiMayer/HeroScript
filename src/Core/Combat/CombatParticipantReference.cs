namespace Core.Combat;

/// <summary>
/// Separates the identity used inside one combat from the immutable content
/// definition used to materialize that participant.
/// </summary>
public sealed record CombatParticipantReference(
    string EntityId,
    string DefinitionId);
