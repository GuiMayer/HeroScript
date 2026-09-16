using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Calculations;
using Core.Effects;
using Core.CardZones;

namespace Core.Run;

public sealed record CombatAnimationFrame
{
    private ImmutableArray<EffectExecutionStep> _effectSteps = [];
    private ImmutableArray<CalculationResult> _calculations = [];
    private ImmutableArray<EffectApplicationRecord> _applications = [];
    private ImmutableArray<CardZoneFlowStepRecord> _cardZoneSteps = [];

    public Guid FrameId { get; init; }
    public int Index { get; init; }
    public int RunSequence { get; init; }
    public ulong CombatStep { get; init; }
    public string TransitionType { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
    public CombatState? StateAfter { get; init; }
    public int SnapshotSequence { get; init; }
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
    public IReadOnlyList<CardZoneFlowStepRecord> CardZoneSteps
    {
        get => _cardZoneSteps;
        init => _cardZoneSteps = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CombatResolutionRecord
{
    private ImmutableArray<CombatAnimationFrame> _frames = [];

    public Guid CommandId { get; init; }
    public Guid CombatId { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public AnimationFrameMode Mode { get; init; }
    public int RootSequence { get; init; }
    public string InitialCombatStateHash { get; init; } = string.Empty;
    public string FinalCombatStateHash { get; init; } = string.Empty;
    public string ResolutionFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<CombatAnimationFrame> Frames
    {
        get => _frames;
        init => _frames = value?.ToImmutableArray() ?? [];
    }
}
