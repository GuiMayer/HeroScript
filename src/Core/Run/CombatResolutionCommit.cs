using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;

namespace Core.Run;

public sealed record CombatResolutionStep
{
    public string TransitionType { get; init; } = string.Empty;
    public CombatState Combat { get; init; } = null!;
    public DeckState Deck { get; init; } = new();
    public DeterministicContext? RunDeterminism { get; init; }
    /// <summary>Transaction-only snapshot; commit copies gameplay fields, never identity or history.</summary>
    public RunState? RunSnapshot { get; init; }
    public JsonElement Payload { get; init; }
}

public sealed record CombatResolutionCommit
{
    private ImmutableArray<CombatResolutionStep> _steps = [];

    public Guid RunId { get; init; }
    public int ExpectedSequence { get; init; }
    public CombatState PreviousCombat { get; init; } = null!;
    public RunCommandIdentity RootCommand { get; init; } = null!;
    public JsonElement RootPayload { get; init; }
    public IReadOnlyList<CombatResolutionStep> Steps
    {
        get => _steps;
        init => _steps = value?.ToImmutableArray() ?? [];
    }
}

public interface IRunCombatResolutionCommitter
{
    Result<RunState> CommitCombatResolution(CombatResolutionCommit resolution);
}
