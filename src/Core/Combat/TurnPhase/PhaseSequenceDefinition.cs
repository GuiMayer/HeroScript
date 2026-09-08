using System.Collections.Immutable;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Ordered data-driven turn graph. Phase ids are content identifiers; roles
/// provide the only engine-level semantics required by every turn-based game.
/// </summary>
public sealed record PhaseSequenceDefinition
{
    private ImmutableArray<PhaseDefinition> _phases = [];

    public string SequenceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string EntryPhaseId { get; init; } = string.Empty;
    public int MaxAutomaticTransitions { get; init; } = 32;
    public IReadOnlyList<PhaseDefinition> Phases
    {
        get => _phases;
        init => _phases = value?.ToImmutableArray() ?? [];
    }
    public PhaseDefinition? Find(string phaseId) =>
        _phases.FirstOrDefault(phase =>
            string.Equals(phase.PhaseId, phaseId, StringComparison.Ordinal));
}
