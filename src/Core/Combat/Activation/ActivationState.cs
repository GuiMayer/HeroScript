using System.Collections.Immutable;
using Core.Combat.Intents;

namespace Core.Combat.Activation;

public sealed record ActivationState
{
    private ImmutableList<string> _activationOrder = [];
    private ImmutableList<string> _completedActorIds = [];
    private ImmutableList<CombatIntent> _intents = [];

    public string? ActiveActorId { get; init; }
    public int Round { get; init; } = 1;
    public int ActivationIndex { get; init; }
    public int ActivationNumber { get; init; }
    public int ActionsTaken { get; init; }
    public IReadOnlyList<string> ActivationOrder
    {
        get => _activationOrder;
        init => _activationOrder = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<string> CompletedActorIds
    {
        get => _completedActorIds;
        init => _completedActorIds = value?.ToImmutableList() ?? [];
    }
    public bool WaitingForInput { get; init; }
    public IReadOnlyList<CombatIntent> Intents
    {
        get => _intents;
        init => _intents = value?.ToImmutableList() ?? [];
    }
    public string? RulesId { get; init; }
    public Guid? RunId { get; init; }
    public DateTime StartedAtUtc { get; init; } = DateTime.UnixEpoch;
}
