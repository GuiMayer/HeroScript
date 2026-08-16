using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Combat.Intents;
using Core.Run;

namespace Core.Combat.Activation;

public sealed record CombatActivationResult
{
    private ImmutableList<string> _drawnCardIds = [];
    private ImmutableList<string> _discardedCardIds = [];
    private ImmutableList<CombatIntent> _intents = [];

    public CombatState CombatState { get; init; } = null!;
    public RunState RunState { get; init; } = null!;
    public ActivationState ActivationState { get; init; } = null!;
    public IReadOnlyList<string> DrawnCardIds
    {
        get => _drawnCardIds;
        init => _drawnCardIds = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<string> DiscardedCardIds
    {
        get => _discardedCardIds;
        init => _discardedCardIds = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<CombatIntent> Intents
    {
        get => _intents;
        init => _intents = value?.ToImmutableList() ?? [];
    }
}
