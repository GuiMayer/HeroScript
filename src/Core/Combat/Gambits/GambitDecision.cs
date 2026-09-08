using Core.Combat.LegalActions;
using Core.Determinism;

namespace Core.Combat.Gambits;

public sealed record DecisionPolicyResult
{
    public LegalActionCandidate Candidate { get; init; } = null!;
    public string PolicyId { get; init; } = string.Empty;
    public string RuleId { get; init; } = string.Empty;
    public int Priority { get; init; }
    public GambitIntentDefinition Intent { get; init; } = new();
    public DeterministicContext Determinism { get; init; } = null!;
    public string StateFingerprint { get; init; } = string.Empty;
    public string DecisionFingerprint { get; init; } = string.Empty;
}
