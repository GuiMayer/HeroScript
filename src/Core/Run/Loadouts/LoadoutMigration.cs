using System.Collections.Immutable;
using Core.Common;
using Core.Content;

namespace Core.Run.Loadouts;

/// <summary>Preflight of the complete graph; never silently removes or unequips items.</summary>
public static class LoadoutMigration
{
    public static Result<RunState> Prepare(RunState run, ContentRuntime target)
    {
        Result<RunState> Fail(string error) => Result<RunState>.Failure(error);
        var definition = run.ResolvedMode?.Definition;
        if (run.Loadouts == null)
        {
            if (definition?.LoadoutPolicyId != null || run.ModeId is { } inactiveMode &&
                target.GetDefinition<GameModeDefinition>("modes", inactiveMode) is { IsSuccess: true } enabled && enabled.Value.LoadoutPolicyId != null)
                return Fail("Enabling inscriptions on an existing run requires a new run");
            return Result<RunState>.Success(run);
        }
        if (!string.Equals(run.ConfigName, target.Manifest.ConfigName, StringComparison.OrdinalIgnoreCase)) return Fail("Loadout migration cannot cross settings");
        if (run.ModeId is { } modeId)
        {
            var mode = target.GetDefinition<GameModeDefinition>("modes", modeId);
            if (mode.IsFailure) return Fail(mode.Error);
            definition = mode.Value;
        }
        if (definition?.LoadoutPolicyId != run.Loadouts.PolicyId) return Fail("Changing or removing the loadout policy requires a new run");
        var policy = target.GetDefinition<LoadoutPolicyDefinition>("loadout-policies", run.Loadouts.PolicyId);
        if (policy.IsFailure) return Fail(policy.Error);
        var valid = policy.Value.ValidateBindings(target, definition);
        if (valid.IsFailure) return Fail(valid.Error);
        if (policy.Value.InventoryScope != run.Loadouts.InventoryScope) return Fail("Inventory scope migration requires explicit redistribution; start a new run");
        var changing = target.Manifest.Revision != run.Determinism.ContentRevision;
        if (changing && (run.ActiveEncounterId != null || policy.Value.RevisionMigration != LoadoutRevisionMigration.RebindCompatible))
            return Fail("Loadout policy rejects revision change or requires outside-combat activation");
        if (changing && run.Loadouts.Offers?.Values.Any(offer => !offer.Closed && offer.NodeId == run.CurrentNodeId && !run.Map.ResolvedNodeIds.Contains(offer.NodeId)) == true)
            return Fail("Close the current inscription offer before activating another revision");
        var inventories = run.Loadouts.Inventories;
        foreach (var (inventoryId, inventory) in inventories)
        {
            var items = inventory.Items;
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                if (!policy.Value.InscriptionIds.Contains(item.DefinitionId)) return Fail($"Owned inscription is removed from policy: {item.DefinitionId}");
                var content = target.GetDefinition<InscriptionDefinition>("inscriptions", item.DefinitionId);
                if (content.IsFailure) return Fail(content.Error);
                valid = content.Value.Validate(target);
                if (valid.IsFailure) return Fail(valid.Error);
                if (changing) items = items.SetItem(index, item with { DefinitionRevision = target.Manifest.Revision });
                else if (item.EffectiveRevision != target.Manifest.Revision) return Fail("Inscription effective revision differs from active content");
            }
            if (items != inventory.Items) inventories = inventories.SetItem(inventoryId, inventory with { Items = items });
        }
        var candidate = run with { Loadouts = run.Loadouts with { Inventories = inventories },
            ResolvedMode = run.ResolvedMode! with { Definition = definition }, Determinism = run.Determinism.WithContentRevision(target.Manifest.Revision) };
        valid = candidate.Loadouts.Validate(run.RunId, policy.Value);
        if (valid.IsFailure) return Fail(valid.Error);
        var planner = new LoadoutChangePlanner(new TargetResolver(target));
        foreach (var actor in candidate.Loadouts.Actors.Values)
        {
            var legal = planner.ValidateExisting(candidate, actor.ActorId, actor.Assignments);
            if (legal.IsFailure) return Fail($"Loadout migration for {actor.ActorId}: {legal.Error}");
        }
        // Revision, mode and logical time are committed by the enclosing activation command.
        return Result<RunState>.Success(run with { Loadouts = candidate.Loadouts });
    }
    private sealed class TargetResolver(ContentRuntime target) : IContentRuntimeResolver
    {
        public Result<ContentRuntime> Resolve(string revision, string? configName = null) => revision == target.Manifest.Revision &&
            (configName == null || string.Equals(configName, target.Manifest.ConfigName, StringComparison.OrdinalIgnoreCase))
            ? Result<ContentRuntime>.Success(target) : Result<ContentRuntime>.Failure("Migration tried to read outside target revision");
        public void Invalidate(string? revision = null) { }
    }
}

public sealed record TransferInscriptionCommand(string FromActorId, string ToActorId, Guid InstanceId);

public static class LoadoutOwnershipTransitions
{
    public static Result<RunState> Transfer(RunState run, ContentRuntime runtime, TransferInscriptionCommand command)
    {
        var policy = LoadoutContentAccess.Policy(run, runtime);
        if (policy.IsFailure) return Result<RunState>.Failure(policy.Error);
        var state = run.Loadouts;
        if (state == null || !policy.Value.AllowTransfers || state.InventoryScope != InscriptionInventoryScope.Actor ||
            !LoadoutContentAccess.CanEquip(run, policy.Value) || string.IsNullOrWhiteSpace(command.FromActorId) ||
            string.IsNullOrWhiteSpace(command.ToActorId) || command.FromActorId == command.ToActorId ||
            !state.Actors.ContainsKey(command.FromActorId) || !state.Actors.ContainsKey(command.ToActorId))
            return Result<RunState>.Failure("Inscription transfer is not authorized for this ownership/window");
        var sourceInventory = state.Inventories[state.InventoryId(run.RunId, command.FromActorId)];
        var to = state.Inventories[state.InventoryId(run.RunId, command.ToActorId)];
        var item = sourceInventory.Items.FirstOrDefault(item => item.InstanceId == command.InstanceId);
        if (item == null || state.Actors.Values.Any(actor => actor.Assignments.Values.Contains(item.InstanceId)))
            return Result<RunState>.Failure("Only owned, unequipped inscriptions may be transferred");
        var next = state with { Inventories = state.Inventories.SetItem(sourceInventory.InventoryId, sourceInventory with { Items = sourceInventory.Items.Remove(item) })
            .SetItem(to.InventoryId, to with { Items = to.Items.Add(item) }) };
        var valid = next.Validate(run.RunId, policy.Value);
        return valid.IsFailure ? Result<RunState>.Failure(valid.Error) : Result<RunState>.Success(run with
            { Loadouts = next, Determinism = run.Determinism.AdvanceStep() });
    }
    /// <summary>Trusted roster lifecycle only. Historical identities are retained when exit would orphan data.</summary>
    public static Result<RunState> RemoveActor(RunState run, ContentRuntime runtime, string actorId)
    {
        var policy = LoadoutContentAccess.Policy(run, runtime);
        if (policy.IsFailure) return Result<RunState>.Failure(policy.Error);
        var state = run.Loadouts;
        if (state == null || string.IsNullOrWhiteSpace(actorId) || !state.Actors.TryGetValue(actorId, out var actor) || state.Actors.Count <= 1 ||
            actorId == run.PlayerEntityId ||
            actor.Assignments.Count != 0 || !LoadoutContentAccess.CanEquip(run, policy.Value) ||
            state.Offers?.Values.Any(offer => offer.ActorId == actorId) == true ||
            state.InventoryScope == InscriptionInventoryScope.Actor && state.Inventories[state.InventoryId(run.RunId, actorId)].Items.Length != 0)
            return Result<RunState>.Failure("Character exit would orphan equipment, inventory or offer history; transfer items first or retain the identity");
        var next = state with { Actors = state.Actors.Remove(actorId), Inventories = state.InventoryScope == InscriptionInventoryScope.Actor
            ? state.Inventories.Remove(state.InventoryId(run.RunId, actorId)) : state.Inventories };
        var valid = next.Validate(run.RunId, policy.Value);
        return valid.IsFailure ? Result<RunState>.Failure(valid.Error) : Result<RunState>.Success(run with
            { Loadouts = next, Determinism = run.Determinism.AdvanceStep() });
    }
}
