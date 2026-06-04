using Core.Combat.Intents;

namespace Core.Combat.Activation;

public sealed record ActivationState
{
    public string? ActiveActorId { get; init; }
    public int Round { get; init; } = 1;
    public int ActivationIndex { get; init; }
    public int ActivationNumber { get; init; }
    public IReadOnlyList<string> ActivationOrder { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CompletedActorIds { get; init; } = Array.Empty<string>();
    public bool WaitingForInput { get; init; }
    public IReadOnlyList<CombatIntent> Intents { get; init; } = Array.Empty<CombatIntent>();
    public string? RulesId { get; init; }
    public Guid? RunId { get; init; }
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
}
