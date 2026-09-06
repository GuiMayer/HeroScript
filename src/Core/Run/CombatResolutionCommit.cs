using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Calculations;
using Core.Effects;

namespace Core.Run;

public sealed record CombatResolutionStep
{
    private ImmutableArray<EffectExecutionStep> _effectSteps = [];
    private ImmutableArray<CalculationResult> _calculations = [];
    private ImmutableArray<EffectApplicationRecord> _applications = [];

    public string TransitionType { get; init; } = string.Empty;
    public CombatState Combat { get; init; } = null!;
    public DeckState Deck { get; init; } = new();
    public DeterministicContext? RunDeterminism { get; init; }
    /// <summary>Transaction-only snapshot; commit copies gameplay fields, never identity or history.</summary>
    public RunState? RunSnapshot { get; init; }
    public JsonElement Payload { get; init; }
    public IReadOnlyList<EffectExecutionStep> EffectSteps
    {
        get => _effectSteps;
        init => _effectSteps = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CalculationResult> Calculations
    {
        get => _calculations;
        init => _calculations = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectApplicationRecord> Applications
    {
        get => _applications;
        init => _applications = value?.ToImmutableArray() ?? [];
    }
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
