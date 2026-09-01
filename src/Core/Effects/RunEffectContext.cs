using Core.Combat.Models;
using Core.Run;

namespace Core.Effects;

/// <summary>
/// Contexto de aplicacao de efeitos de run/economia.
/// </summary>
public record RunEffectContext : IEffectContext
{
    public EffectScope Scope => EffectScope.RUN;
    public string RunId { get; init; } = string.Empty;
    public RunState? RunState { get; init; }
    public string SourceEntityId { get; init; } = string.Empty;
    public string TargetEntityId { get; init; } = string.Empty;
    public string? SourceActionId { get; init; }
    public string? SourceCardId { get; init; }
    public CombatState? CombatState => null;
    public string? ContentRevision => RunState?.Determinism.ContentRevision;
}
