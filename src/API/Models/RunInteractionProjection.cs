using System.Text.Json;
using Core.Run;
using Core.Run.Projections;

namespace API.Models;

/// <summary>UI contract, not a serialized aggregate. JsonElement is an immutable owned value.</summary>
public static class RunInteractionProjection
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static JsonElement Project(InteractionReadContext context, bool includeToolFlows = false)
    {
        var run = context.Run;
        var mode = run.ResolvedMode;
        // A terminal encounter remains referenced until its result is confirmed.
        // Navigation is already visible then, so only an ACTIVE combat may compact it.
        var nodes = run.GetActiveEncounter()?.Combat.IsActive == true
            ? run.Map.Nodes.Where(node => node.NodeId == run.CurrentNodeId) : run.Map.Nodes;
        return JsonSerializer.SerializeToElement(new
        {
            run.RunId, run.SettingId, run.ConfigName, run.Sequence, run.CurrentNodeId, run.Lifecycle,
            run.PlayerEntityId, run.PlayerEntity, run.ModeId, run.ActiveEncounterId,
            loadout = run.Loadouts == null ? null : new { policyId = run.Loadouts.PolicyId, run.Loadouts.InventoryScope,
                actorIds = run.Loadouts.Actors.Keys, equippedCount = run.Loadouts.Actors.Values.Sum(actor => actor.Assignments.Count) },
            run.Determinism.Step, run.Determinism.Seed, run.Determinism.ContentRevision,
            run.Determinism.EngineVersion, stateHash = context.Version.StateFingerprint,
            characterResources = RunResourceProjection.Character(run), runResourceOwnerId = run.ResourceState.OwnerId,
            resources = run.ResourceState.Resources.ToDictionary(pair => pair.Key,
                pair => new { pair.Value.Current, pair.Value.Minimum, pair.Value.Maximum }),
            resolvedMode = mode == null ? null : new
            {
                mode.CapabilityPolicy, mode.ReplayPolicy, mode.TimelinePolicy,
                cardZoneSystem = new { flows = includeToolFlows ? mode.CardZoneSystem?.Flows.Where(
                    flow => flow.AllowedInvocations.Contains(Core.CardZones.CardZoneFlowInvocation.Tool)).ToArray() : [] }
            },
            map = new
            {
                run.RunId, run.CurrentNodeId, run.Map.VisitedNodeIds, run.Map.ResolvedNodeIds,
                legalNextNodeIds = RunMapTransitions.GetLegalNextNodeIds(run),
                nodes = nodes.Select(node => new { node.NodeId, node.Activity, node.CompletionPolicy,
                    node.NextNodeIds, node.Metadata, visited = run.Map.VisitedNodeIds.Contains(node.NodeId),
                    resolved = run.Map.ResolvedNodeIds.Contains(node.NodeId) })
            },
            cardSelections = run.CardSelections.Where(value => value.NodeId == run.CurrentNodeId),
            shops = run.Shops.Where(value => value.NodeId == run.CurrentNodeId),
            preparations = run.Preparations.Where(value => value.NodeId == run.CurrentNodeId),
            dialogues = run.Dialogues.Select(dialogue => Core.Run.Dialogue.DialogueTransitions.View(run, dialogue)),
            run.NarrativeFlags, run.Relics, run.Modifiers, run.CompletedActivityNodeIds, run.Metadata,
            rootRunId = run.Lineage?.RootRunId, parentRunId = run.Lineage?.ParentRunId,
            sourceCombatId = run.Lineage?.SourceCombatId, sourceSequence = run.Lineage?.SourceSequence,
            sourceStateHash = run.Lineage?.SourceStateHash, branchKey = run.Lineage?.BranchKey
        }, Options);
    }
}
