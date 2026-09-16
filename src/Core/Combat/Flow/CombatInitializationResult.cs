using System.Collections.Immutable;
using Core.Calculations;
using Core.CardZones;
using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Effects;
using Core.Run;

namespace Core.Combat.Flow;

public sealed record CombatInitializationResult(CombatState Combat, RunState Run)
{
    private ImmutableArray<EffectExecutionStep> _effectSteps = [];
    private ImmutableArray<CalculationResult> _calculations = [];
    private ImmutableArray<EffectApplicationRecord> _applications = [];
    private ImmutableArray<PhaseTransitionRecord> _phaseTransitions = [];
    private ImmutableArray<CardZoneFlowStepRecord> _cardZoneSteps = [];

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

    public IReadOnlyList<PhaseTransitionRecord> PhaseTransitions
    {
        get => _phaseTransitions;
        init => _phaseTransitions = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyList<CardZoneFlowStepRecord> CardZoneSteps
    {
        get => _cardZoneSteps;
        init => _cardZoneSteps = value?.ToImmutableArray() ?? [];
    }

    public string Fingerprint { get; init; } = string.Empty;
}
