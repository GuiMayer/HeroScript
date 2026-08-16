using System.Collections.Immutable;
using System.Text.Json;
using Core.Common;

namespace Core.Run;

/// <summary>
/// Pure map state machine. It only uses the pinned map, the command payload and
/// the deterministic context already stored in the run.
/// </summary>
public static class RunMapTransitions
{
    public static Result<RunMapState> Create(IReadOnlyList<RunMapNodeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var invalid = definitions.FirstOrDefault(node => string.IsNullOrWhiteSpace(node.NodeId));
        if (invalid != null)
            return Result<RunMapState>.Failure("Map node id is required");

        invalid = definitions.FirstOrDefault(node => string.IsNullOrWhiteSpace(node.NodeType));
        if (invalid != null)
            return Result<RunMapState>.Failure($"Map node type is required: {invalid.NodeId}");

        var duplicateNode = definitions
            .GroupBy(node => node.NodeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateNode != null)
            return Result<RunMapState>.Failure($"Duplicate map node id: {duplicateNode.Key}");

        var nodeIds = definitions
            .Select(node => node.NodeId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var duplicateEdge = definition.NextNodeIds
                .GroupBy(nodeId => nodeId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateEdge != null)
            {
                return Result<RunMapState>.Failure(
                    $"Duplicate map edge from '{definition.NodeId}' to '{duplicateEdge.Key}'");
            }

            var missing = definition.NextNodeIds.FirstOrDefault(nodeId => !nodeIds.Contains(nodeId));
            if (missing != null)
            {
                return Result<RunMapState>.Failure(
                    $"Map node '{definition.NodeId}' references unknown node '{missing}'");
            }
        }

        var nodes = definitions.Select(ToState).ToImmutableList();
        var firstNodeId = nodes.FirstOrDefault()?.NodeId;
        return Result<RunMapState>.Success(new RunMapState
        {
            Nodes = nodes,
            VisitedNodeIds = firstNodeId == null ? [] : [firstNodeId]
        });
    }

    public static Result<RunStateTransition<RunMapNodeState>> Resolve(
        RunState state,
        string currentNodeId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(currentNodeId))
            return Result<RunStateTransition<RunMapNodeState>>.Failure("Current node id is required");
        if (!string.Equals(state.CurrentNodeId, currentNodeId, StringComparison.Ordinal))
        {
            return Result<RunStateTransition<RunMapNodeState>>.Failure(
                $"Current map node mismatch. Expected '{state.CurrentNodeId}', received '{currentNodeId}'");
        }

        var current = FindNode(state.Map, currentNodeId);
        if (current == null)
            return Result<RunStateTransition<RunMapNodeState>>.Failure($"Map node not found: {currentNodeId}");
        if (Contains(state.Map.ResolvedNodeIds, currentNodeId))
        {
            return Result<RunStateTransition<RunMapNodeState>>.Failure(
                $"Map node already resolved: {currentNodeId}");
        }

        var map = state.Map with
        {
            ResolvedNodeIds = AddOrdered(state.Map.ResolvedNodeIds, currentNodeId)
        };
        var next = state with
        {
            Map = map,
            Determinism = state.Determinism.AdvanceStep()
        };
        return Result<RunStateTransition<RunMapNodeState>>.Success(new(next, current));
    }

    public static Result<RunStateTransition<RunMapNodeState>> Advance(
        RunState state,
        string targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(targetNodeId))
            return Result<RunStateTransition<RunMapNodeState>>.Failure("Target node id is required");
        if (state.CurrentNodeId == null)
            return Result<RunStateTransition<RunMapNodeState>>.Failure("Run has no current map node");

        var current = FindNode(state.Map, state.CurrentNodeId);
        if (current == null)
        {
            return Result<RunStateTransition<RunMapNodeState>>.Failure(
                $"Map node not found: {state.CurrentNodeId}");
        }
        if (!Contains(state.Map.ResolvedNodeIds, current.NodeId))
        {
            return Result<RunStateTransition<RunMapNodeState>>.Failure(
                $"Current map node must be resolved before advancing: {current.NodeId}");
        }
        if (!Contains(current.NextNodeIds, targetNodeId))
        {
            return Result<RunStateTransition<RunMapNodeState>>.Failure(
                $"Illegal map transition from '{current.NodeId}' to '{targetNodeId}'");
        }
        if (Contains(state.Map.VisitedNodeIds, targetNodeId))
            return Result<RunStateTransition<RunMapNodeState>>.Failure($"Map node already visited: {targetNodeId}");

        var target = FindNode(state.Map, targetNodeId);
        if (target == null)
            return Result<RunStateTransition<RunMapNodeState>>.Failure($"Map node not found: {targetNodeId}");

        var map = state.Map with
        {
            VisitedNodeIds = AddOrdered(state.Map.VisitedNodeIds, targetNodeId)
        };
        var next = state with
        {
            CurrentNodeId = targetNodeId,
            Map = map,
            Determinism = state.Determinism.AdvanceStep()
        };
        return Result<RunStateTransition<RunMapNodeState>>.Success(new(next, target));
    }

    public static IReadOnlyList<RunAvailableCommand> GetAvailableCommands(RunState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CurrentNodeId == null)
            return [];

        var current = FindNode(state.Map, state.CurrentNodeId);
        if (current == null)
            return [];

        var activeEncounter = state.GetActiveEncounter();
        if (activeEncounter != null)
        {
            return activeEncounter.Combat.IsActive
                ? []
                :
                [
                    new RunAvailableCommand
                    {
                        Type = RunCommandTypes.ResolveCombat,
                        CurrentNodeId = current.NodeId
                    }
                ];
        }

        if (!Contains(state.Map.ResolvedNodeIds, current.NodeId))
        {
            return
            [
                new RunAvailableCommand
                {
                    Type = IsEncounterNode(current.NodeType)
                        ? RunCommandTypes.StartEncounter
                        : RunCommandTypes.ResolveNode,
                    CurrentNodeId = current.NodeId
                }
            ];
        }

        var targets = current.NextNodeIds
            .Where(nodeId => !Contains(state.Map.VisitedNodeIds, nodeId))
            .ToImmutableList();
        return targets.Count == 0
            ? []
            :
            [
                new RunAvailableCommand
                {
                    Type = RunCommandTypes.AdvanceNode,
                    CurrentNodeId = current.NodeId,
                    TargetNodeIds = targets
                }
            ];
    }

    public static IReadOnlyList<string> GetLegalNextNodeIds(RunState state)
    {
        var advance = GetAvailableCommands(state)
            .FirstOrDefault(command => command.Type == RunCommandTypes.AdvanceNode);
        return advance?.TargetNodeIds ?? [];
    }

    public static bool IsEncounterNode(string nodeType)
    {
        return string.Equals(nodeType, "combat", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(nodeType, "elite", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(nodeType, "boss", StringComparison.OrdinalIgnoreCase);
    }

    private static RunMapNodeState ToState(RunMapNodeDefinition definition)
    {
        return new RunMapNodeState
        {
            NodeId = definition.NodeId,
            NodeType = definition.NodeType,
            NextNodeIds = definition.NextNodeIds,
            Metadata = definition.Metadata.ToImmutableDictionary(
                entry => entry.Key,
                entry => JsonSerializer.SerializeToElement(entry.Value).Clone(),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static RunMapNodeState? FindNode(RunMapState map, string nodeId)
    {
        return map.Nodes.FirstOrDefault(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
    }

    private static bool Contains(IEnumerable<string> values, string value)
    {
        return values.Contains(value, StringComparer.Ordinal);
    }

    private static ImmutableList<string> AddOrdered(IEnumerable<string> values, string value)
    {
        return values
            .Append(value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToImmutableList();
    }
}
