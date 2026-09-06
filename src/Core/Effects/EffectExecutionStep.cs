using System.Collections.Immutable;
using Core.Calculations;

namespace Core.Effects;

/// <summary>Deterministic diagnostic data, never an externally published event before commit.</summary>
public sealed record EffectExecutionStep
{
    public int Index { get; init; }
    public string EffectInstanceId { get; init; } = string.Empty;
    public string TargetEntityId { get; init; } = string.Empty;
    public int RepeatIndex { get; init; }
    public int TargetIndex { get; init; }
    public bool Applied { get; init; }
    public string? SkipReason { get; init; }
    public double? ChanceRoll { get; init; }
    public string ContentRevision { get; init; } = string.Empty;
    public EffectProvenance Provenance { get; init; } = new();
    public CalculationResult? Calculation { get; init; }
    public ImmutableArray<EffectApplicationRecord> Applications { get; init; } = [];
    public string StateBeforeHash { get; init; } = string.Empty;
    public string StateAfterHash { get; init; } = string.Empty;
    public string? RunBeforeHash { get; init; }
    public string? RunAfterHash { get; init; }
}

public static class EffectExecutionLimits
{
    public const int MaximumDepth = 32;
    public const int MaximumRepeat = 256;
    public const int MaximumSteps = 4096;
}
