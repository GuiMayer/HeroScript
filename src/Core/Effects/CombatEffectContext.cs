using Core.Combat.Models;

namespace Core.Effects;

/// <summary>
/// Contexto de aplicacao de efeitos durante combate.
/// </summary>
public record CombatEffectContext : IEffectContext
{
    public EffectScope Scope => EffectScope.COMBAT;
    public string SourceEntityId { get; init; } = string.Empty;
    public string TargetEntityId { get; init; } = string.Empty;
    public string? SourceActionId { get; init; }
    public string? SourceCardId { get; init; }
    public CombatState CombatState { get; init; } = null!;

    CombatState? IEffectContext.CombatState => CombatState;

    public static CombatEffectContext FromEffect(EffectInstance effect, CombatState state)
    {
        return new CombatEffectContext
        {
            SourceEntityId = effect.SourceEntityId,
            TargetEntityId = effect.TargetEntityId,
            SourceActionId = effect.SourceActionId,
            SourceCardId = effect.SourceCardId,
            CombatState = state
        };
    }
}
