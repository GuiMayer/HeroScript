using Core.Combat.Models;

namespace Core.Effects;

/// <summary>
/// Contexto minimo para aplicar um efeito sem acoplar todos os efeitos a CombatState.
/// </summary>
public interface IEffectContext
{
    EffectScope Scope { get; }
    string SourceEntityId { get; }
    string TargetEntityId { get; }
    string? SourceActionId { get; }
    string? SourceCardId { get; }
    CombatState? CombatState { get; }
    string? ContentRevision { get; }
}
