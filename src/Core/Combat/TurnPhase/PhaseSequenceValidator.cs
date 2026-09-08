using Core.Common;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Pure structural validation for executable phase graphs.
/// </summary>
public static class PhaseSequenceValidator
{
    public static Result Validate(PhaseSequenceDefinition sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (string.IsNullOrWhiteSpace(sequence.SequenceId))
            return Result.Failure("Phase sequence requires sequenceId");
        if (string.IsNullOrWhiteSpace(sequence.EntryPhaseId))
            return Result.Failure("Phase sequence requires entryPhaseId");
        if (sequence.MaxAutomaticTransitions <= 0)
            return Result.Failure("maxAutomaticTransitions must be greater than zero");
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

        if (sequence.Phases.Select(phase => phase.Order).Distinct().Count() != sequence.Phases.Count)
            return Result.Failure("Phase order values must be unique");

        var known = sequence.Phases.Select(phase => phase.PhaseId).ToHashSet(StringComparer.Ordinal);
        if (!known.Contains(sequence.EntryPhaseId))
            return Result.Failure($"Entry phase does not exist: {sequence.EntryPhaseId}");
        if (sequence.Find(sequence.EntryPhaseId)!.Role != PhaseRole.Start)
            return Result.Failure("Entry phase must have the START role");
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var phase in sequence.Phases)
        {
            if (phase.AllowedCommandTags.Any(string.IsNullOrWhiteSpace))
                return Result.Failure($"Phase {phase.PhaseId} contains an empty allowed command tag");
            if (phase.AllowedCommandTags.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                phase.AllowedCommandTags.Count)
                return Result.Failure($"Phase {phase.PhaseId} contains duplicate allowed command tags");
            if (phase.AllowedActions.Distinct().Count() != phase.AllowedActions.Count)
                return Result.Failure($"Phase {phase.PhaseId} contains duplicate allowed actions");
            foreach (var edge in phase.Edges)
            {
                if (string.IsNullOrWhiteSpace(edge.EdgeId))
                    return Result.Failure($"Phase {phase.PhaseId} contains an edge without edgeId");
                if (!edgeIds.Add(edge.EdgeId))
                    return Result.Failure($"Phase edge ids must be unique: {edge.EdgeId}");
                if (edge.Trigger == PhaseEdgeTrigger.Unspecified)
                    return Result.Failure($"Phase edge {edge.EdgeId} requires a trigger");
                if (edge.CommandTags.Any(string.IsNullOrWhiteSpace) ||
                    edge.CommandTags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != edge.CommandTags.Count)
                    return Result.Failure($"Phase edge {edge.EdgeId} contains invalid or duplicate command tags");
                if (edge.ActionTypes.Distinct().Count() != edge.ActionTypes.Count)
                    return Result.Failure($"Phase edge {edge.EdgeId} contains duplicate action types");
                if (!known.Contains(edge.TargetPhaseId))
                    return Result.Failure(
                        $"Phase {phase.PhaseId} references invalid next phase: {edge.TargetPhaseId}");
                if (edge.Trigger != PhaseEdgeTrigger.Command &&
                    (edge.ActionTypes.Count > 0 || edge.CommandTags.Count > 0))
                    return Result.Failure(
                        $"Phase edge {edge.EdgeId} can filter commands only with the COMMAND trigger");
            }

            foreach (var group in phase.Edges.GroupBy(edge => (edge.Trigger, edge.Priority)))
            {
                var edges = group.ToArray();
                for (var left = 0; left < edges.Length; left++)
                for (var right = left + 1; right < edges.Length; right++)
                {
                    if (MayOverlap(edges[left], edges[right]))
                        return Result.Failure(
                            $"Phase {phase.PhaseId} has ambiguous {group.Key.Trigger} edges at priority {group.Key.Priority}");
                }
            }
        }

        var automaticCycle = FindAutomaticCycle(sequence);
        if (automaticCycle != null)
            return Result.Failure($"Phase graph contains an automatic cycle at: {automaticCycle}");

        foreach (var phase in sequence.Phases.Where(phase => phase.Role != PhaseRole.End))
        {
            if (phase.Edges.Count == 0)
                return Result.Failure($"Non-END phase is a dead end: {phase.PhaseId}");
        }
        foreach (var phase in sequence.Phases.Where(phase => phase.Role != PhaseRole.End))
        {
            var exitReachable = Reachable(sequence, phase.PhaseId,
                edge => edge.Trigger is PhaseEdgeTrigger.Automatic or PhaseEdgeTrigger.ActivationExit);
            if (!sequence.Phases.Any(candidate =>
                    exitReachable.Contains(candidate.PhaseId) && candidate.Role == PhaseRole.End))
                return Result.Failure($"Phase cannot reach END during activation exit: {phase.PhaseId}");
        }

        var reachable = Reachable(sequence, sequence.EntryPhaseId, _ => true);
        var unreachable = sequence.Phases.Select(phase => phase.PhaseId)
            .Where(id => !reachable.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (unreachable.Length > 0)
            return Result.Failure($"Phase graph contains unreachable phases: {string.Join(", ", unreachable)}");
        foreach (var role in new[] { PhaseRole.Start, PhaseRole.Middle, PhaseRole.End })
        {
            if (!sequence.Phases.Any(phase => reachable.Contains(phase.PhaseId) && phase.Role == role))
                return Result.Failure($"Phase graph has no reachable {role.ToString().ToUpperInvariant()} phase");
        }

        return Result.Success();
    }

    private static bool MayOverlap(PhaseEdgeDefinition left, PhaseEdgeDefinition right)
    {
        if (left.Trigger != PhaseEdgeTrigger.Command)
            return true;
        return left.ActionTypes.Count == 0 || right.ActionTypes.Count == 0 ||
               left.ActionTypes.Intersect(right.ActionTypes).Any();
    }

    private static HashSet<string> Reachable(
        PhaseSequenceDefinition sequence,
        string start,
        Func<PhaseEdgeDefinition, bool> include)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            foreach (var edge in sequence.Find(current)!.Edges.Where(include)
                         .OrderByDescending(edge => edge.Priority)
                         .ThenBy(edge => edge.EdgeId, StringComparer.Ordinal))
                pending.Push(edge.TargetPhaseId);
        }
        return visited;
    }

    private static string? FindAutomaticCycle(PhaseSequenceDefinition sequence)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var phase in sequence.Phases)
        {
            var cycle = Visit(phase.PhaseId);
            if (cycle != null) return cycle;
        }
        return null;

        string? Visit(string id)
        {
            if (visiting.Contains(id)) return id;
            if (!visited.Add(id)) return null;
            visiting.Add(id);
            foreach (var edge in sequence.Find(id)!.Edges
                         .Where(edge => edge.Trigger == PhaseEdgeTrigger.Automatic))
            {
                var cycle = Visit(edge.TargetPhaseId);
                if (cycle != null) return cycle;
            }
            visiting.Remove(id);
            return null;
        }
    }
}
