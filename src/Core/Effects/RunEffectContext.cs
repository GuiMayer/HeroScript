using Core.Combat.Models;

namespace Core.Effects;

/// <summary>
/// Contexto de aplicacao de efeitos de run/economia enquanto RunState real ainda nao existe.
/// </summary>
public record RunEffectContext : IEffectContext
{
    public EffectScope Scope => EffectScope.RUN;
    public string RunId { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string TargetEntityId { get; init; } = string.Empty;
    public string? SourceActionId { get; init; }
    public string? SourceCardId { get; init; }
    public CombatState? CombatState => null;
}
