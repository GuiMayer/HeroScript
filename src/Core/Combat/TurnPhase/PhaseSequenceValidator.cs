using Core.Common;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Pure structural validation for phase-sequence content. Runtime transitions
/// belong exclusively to CombatFlowPlanner.
/// </summary>
public static class PhaseSequenceValidator
{
    public static Result Validate(PhaseSequenceDefinition sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (sequence.Phases.Count < 3)
            return Result.Failure("A phase sequence requires at least START, MIDDLE and END phases");
        if (sequence.Phases.Any(phase => string.IsNullOrWhiteSpace(phase.PhaseId)))
            return Result.Failure("Every phase requires phaseId");
        if (sequence.Phases.Select(phase => phase.PhaseId).Distinct(StringComparer.Ordinal).Count() !=
            sequence.Phases.Count)
            return Result.Failure("Phase ids must be unique");
        if (sequence.Phases.Any(phase => phase.Role == PhaseRole.Unspecified))
            return Result.Failure("Every phase requires a semantic role");
        foreach (var role in new[] { PhaseRole.Start, PhaseRole.Middle, PhaseRole.End })
        {
            if (!sequence.Phases.Any(phase => phase.Role == role))
                return Result.Failure($"Phase sequence requires at least one {role.ToString().ToUpperInvariant()} phase");
        }

        var ordered = sequence.Phases.OrderBy(phase => phase.Order).ToArray();
        if (!ordered.SequenceEqual(sequence.Phases) ||
            ordered.Select(phase => phase.Order).Distinct().Count() != ordered.Length)
            return Result.Failure("Phase order values must be unique and ascending");
        var roleRanks = ordered.Select(phase => phase.Role switch
        {
            PhaseRole.Start => 0,
            PhaseRole.Middle => 1,
            PhaseRole.End => 2,
            _ => -1
        }).ToArray();
        if (!roleRanks.SequenceEqual(roleRanks.OrderBy(rank => rank)))
            return Result.Failure("Phase roles must follow START, MIDDLE, END order");

        var known = sequence.Phases.Select(phase => phase.PhaseId).ToHashSet(StringComparer.Ordinal);
        foreach (var phase in sequence.Phases)
        foreach (var next in phase.ValidNextPhaseIds)
        {
            if (!known.Contains(next))
                return Result.Failure($"Phase {phase.PhaseId} references invalid next phase: {next}");
        }

        return Result.Success();
    }

    /// <summary>
    /// Operational subset supported by the canonical activation planner.
    /// Richer graphs remain valid content but cannot be selected by a combat
    /// rule until their commands and transitions are implemented.
    /// </summary>
    public static Result ValidateCanonicalActivationSequence(PhaseSequenceDefinition sequence)
    {
        var validation = Validate(sequence);
        if (validation.IsFailure)
            return validation;

        foreach (var role in new[] { PhaseRole.Start, PhaseRole.Middle, PhaseRole.End })
        {
            var count = sequence.Phases.Count(phase => phase.Role == role);
            if (count != 1)
            {
                return Result.Failure(
                    $"Canonical activation flow currently requires exactly one {role.ToString().ToUpperInvariant()} phase; found {count}");
            }
        }

        return Result.Success();
    }
}
