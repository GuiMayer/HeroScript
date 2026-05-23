using Core.Combat.Models;
using Core.Run;

namespace Core.Combat.Activation;

public sealed record CombatActivationResult
{
    public CombatState CombatState { get; init; } = null!;
    public RunState RunState { get; init; } = null!;
    public ActivationState ActivationState { get; init; } = null!;
    public IReadOnlyList<string> DrawnCardIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DiscardedCardIds { get; init; } = Array.Empty<string>();
}
