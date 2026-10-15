using System.Collections.Immutable;
using Core.Common;
using Core.Content;

namespace Core.Run.Loadouts;

public sealed record LoadoutChangePlan(RunState Candidate, string ActorId,
    ImmutableSortedDictionary<string, Guid> Before, ImmutableSortedDictionary<string, Guid> After,
    float UsedCapacity, float Capacity);

public static class LoadoutContentAccess
{
    public static Result<LoadoutPolicyDefinition> Policy(RunState run, ContentRuntime runtime)
    {
        if (run.Determinism.ContentRevision != runtime.Manifest.Revision || !string.Equals(run.ConfigName, runtime.Manifest.ConfigName, StringComparison.OrdinalIgnoreCase))
            return Result<LoadoutPolicyDefinition>.Failure("Loadout runtime does not match the run's setting and revision");
        var id = run.ResolvedMode?.Definition.LoadoutPolicyId;
        if (string.IsNullOrWhiteSpace(id)) return Result<LoadoutPolicyDefinition>.Failure("Inscriptions are not enabled by this mode");
        var policy = runtime.GetDefinition<LoadoutPolicyDefinition>("loadout-policies", id);
        if (policy.IsFailure) return policy;
        var valid = policy.Value.ValidateBindings(runtime, run.ResolvedMode!.Definition);
        return valid.IsFailure ? Result<LoadoutPolicyDefinition>.Failure(valid.Error) : policy;
    }

    public static bool CanEquip(RunState run, LoadoutPolicyDefinition policy)
    {
        if (run.Lifecycle != RunLifecycleState.Active) return false;
        if (run.ActiveEncounterId != null) return policy.EquipmentWindows.Contains(LoadoutEquipmentWindow.Combat);
        if (policy.EquipmentWindows.Contains(LoadoutEquipmentWindow.OutsideCombat)) return true;
        var node = run.Map.Nodes.FirstOrDefault(value => value.NodeId == run.CurrentNodeId);
        return policy.EquipmentWindows.Contains(LoadoutEquipmentWindow.Preparation) && node?.Activity.Type == RunActivityType.Preparation &&
            !run.CompletedActivityNodeIds.Contains(node.NodeId) && !run.Map.ResolvedNodeIds.Contains(node.NodeId);
    }
}

public static class InscriptionAcquisition
{
    /// <summary>Trusted run lifecycle registers stable character IDs, never the client.</summary>
    public static Result<RunState> Initialize(RunState run, ContentRuntime runtime, IEnumerable<string> actorIds)
    {
        if (run.Lifecycle != RunLifecycleState.Active || run.ActiveEncounterId != null || actorIds == null)
            return Result<RunState>.Failure("Loadout initialization requires an active run outside combat");
        var policy = LoadoutContentAccess.Policy(run, runtime);
        if (policy.IsFailure) return Result<RunState>.Failure(policy.Error);
        if (run.Loadouts != null) return Result<RunState>.Failure("Loadout ownership is already initialized");
        var actors = actorIds.ToArray();
        if (actors.Length is < 1 or > 64 || actors.Any(string.IsNullOrWhiteSpace) || actors.Distinct(StringComparer.Ordinal).Count() != actors.Length)
            return Result<RunState>.Failure("Loadouts require unique persistent character identities");
        var state = new InscriptionLoadoutState { PolicyId = policy.Value.LoadoutPolicyId, InventoryScope = policy.Value.InventoryScope!.Value };
        foreach (var actor in actors.Order(StringComparer.Ordinal))
        {
            var inventoryId = state.InventoryId(run.RunId, actor);
            state = state with { Actors = state.Actors.Add(actor, new() { ActorId = actor }),
                Inventories = state.Inventories.SetItem(inventoryId, new() { InventoryId = inventoryId }) };
        }
        var valid = state.Validate(run.RunId, policy.Value);
        return valid.IsFailure ? Result<RunState>.Failure(valid.Error) : Result<RunState>.Success(run with { Loadouts = state });
    }

    /// <summary>Acquisition primitive; callers must authorize the offer or development capability.</summary>
    public static Result<RunState> Grant(RunState run, ContentRuntime runtime, string actorId, string definitionId, string source)
    {
        var policy = LoadoutContentAccess.Policy(run, runtime);
        if (policy.IsFailure) return Result<RunState>.Failure(policy.Error);
        if (run.Lifecycle != RunLifecycleState.Active || run.Loadouts == null || string.IsNullOrWhiteSpace(actorId) || !run.Loadouts.Actors.ContainsKey(actorId) || string.IsNullOrWhiteSpace(source))
            return Result<RunState>.Failure("Inscription acquisition requires an active run, registered character and provenance");
        var valid = run.Loadouts.Validate(run.RunId, policy.Value);
        if (valid.IsFailure) return Result<RunState>.Failure(valid.Error);
        if (!policy.Value.InscriptionIds.Contains(definitionId)) return Result<RunState>.Failure("Inscription is not allowed by this loadout policy");
        var item = runtime.GetDefinition<InscriptionDefinition>("inscriptions", definitionId);
        if (item.IsFailure) return Result<RunState>.Failure(item.Error);
        var allocated = run.Determinism.AllocateId("inscription-instance");
        var id = run.Loadouts.InventoryId(run.RunId, actorId);
        var inventory = run.Loadouts.Inventories[id];
        var next = run.Loadouts with { Inventories = run.Loadouts.Inventories.SetItem(id, inventory with
        { Items = inventory.Items.Add(new() { InstanceId = allocated.Value, DefinitionId = definitionId,
            AcquisitionRevision = runtime.Manifest.Revision, AcquisitionSequence = run.Sequence + 1, AcquisitionSource = source }) }) };
        valid = next.Validate(run.RunId, policy.Value);
        return valid.IsFailure ? Result<RunState>.Failure(valid.Error) : Result<RunState>.Success(run with { Loadouts = next, Determinism = allocated.Context });
    }
}

/// <summary>Pure, complete equipment transaction shared by preview and canonical commands.</summary>
public sealed class LoadoutChangePlanner(IContentRuntimeResolver runtimes)
{
    public Result<LoadoutChangePlan> Plan(RunState run, string actorId, IReadOnlyDictionary<string, Guid> assignments)
    {
        Result<LoadoutChangePlan> Fail(string reason) => Result<LoadoutChangePlan>.Failure(reason);
        if (assignments == null || string.IsNullOrWhiteSpace(actorId)) return Fail("Character and complete equipment draft are required");
        var active = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (active.IsFailure) return Fail(active.Error);
        var policy = LoadoutContentAccess.Policy(run, active.Value);
        if (policy.IsFailure) return Fail(policy.Error);
        if (run.Loadouts == null || !run.Loadouts.Actors.TryGetValue(actorId, out var actor)) return Fail("Loadout character not found");
        var valid = run.Loadouts.Validate(run.RunId, policy.Value);
        if (valid.IsFailure) return Fail(valid.Error);
        if (!LoadoutContentAccess.CanEquip(run, policy.Value)) return Fail("Equipment cannot be changed in the current policy window");
        if (assignments.Count > policy.Value.Slots.Length || assignments.Values.Distinct().Count() != assignments.Count) return Fail("Invalid slots or duplicate inscription instance");
        var inventory = run.Loadouts.Inventories[run.Loadouts.InventoryId(run.RunId, actorId)];
        var definitions = new HashSet<string>(StringComparer.Ordinal);
        var used = 0d;
        foreach (var (slotId, instanceId) in assignments.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var slot = policy.Value.Slots.FirstOrDefault(value => value.SlotId == slotId);
            var owned = inventory.Items.FirstOrDefault(item => item.InstanceId == instanceId);
            if (slot == null || owned == null || !policy.Value.InscriptionIds.Contains(owned.DefinitionId)) return Fail("Slot or owned inscription not found");
            if (run.Loadouts.Actors.Values.Where(other => other.ActorId != actorId).Any(other => other.Assignments.Values.Contains(instanceId)))
                return Fail("Inscription is already equipped by another character");
            var acquired = runtimes.Resolve(owned.AcquisitionRevision, run.ConfigName);
            if (acquired.IsFailure) return Fail(acquired.Error);
            if (acquired.Value.Manifest.Revision != owned.AcquisitionRevision || !string.Equals(acquired.Value.Manifest.ConfigName, run.ConfigName, StringComparison.OrdinalIgnoreCase))
                return Fail("Inscription acquisition setting or revision mismatch");
            var item = acquired.Value.GetDefinition<InscriptionDefinition>("inscriptions", owned.DefinitionId);
            if (item.IsFailure) return Fail(item.Error);
            valid = item.Value.Validate(acquired.Value);
            if (valid.IsFailure) return Fail(valid.Error);
            if (!slot.RequiredTags.All(item.Value.Tags.Contains)) return Fail("Inscription tags are not compatible with this slot");
            if (!definitions.Add(owned.DefinitionId) && policy.Value.AllowDuplicateDefinitions != true) return Fail("Duplicate inscription definitions are not allowed");
            used += item.Value.EquipmentCost;
        }
        if (used > policy.Value.Capacity) return Fail("Inscription equipment capacity exceeded");
        var after = assignments.ToImmutableSortedDictionary(StringComparer.Ordinal);
        var next = run.Loadouts with { Actors = run.Loadouts.Actors.SetItem(actorId, actor with { Assignments = after }) };
        valid = next.Validate(run.RunId, policy.Value);
        if (valid.IsFailure) return Fail(valid.Error);
        return Result<LoadoutChangePlan>.Success(new(run with { Loadouts = next, Determinism = run.Determinism.AdvanceStep() }, actorId,
            actor.Assignments, after, (float)used, policy.Value.Capacity));
    }
}
