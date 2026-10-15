using System.Collections.Immutable;
using Core.Common;

namespace Core.Run.Loadouts;

public sealed record InscriptionInstance
{
    public Guid InstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string AcquisitionRevision { get; init; } = string.Empty;
    public string AcquisitionSource { get; init; } = string.Empty;
    public int AcquisitionSequence { get; init; }
}

public sealed record InscriptionInventoryState
{
    public string InventoryId { get; init; } = string.Empty;
    public ImmutableArray<InscriptionInstance> Items { get; init; } = [];
}

public sealed record ActorLoadoutState
{
    public string ActorId { get; init; } = string.Empty;
    public ImmutableSortedDictionary<string, Guid> Assignments { get; init; } = ImmutableSortedDictionary<string, Guid>.Empty.WithComparers(StringComparer.Ordinal);
}

/// <summary>Single immutable authority for ownership and equipment. Does not contain cards or effective stats.</summary>
public sealed record InscriptionLoadoutState
{
    public string PolicyId { get; init; } = string.Empty;
    public InscriptionInventoryScope InventoryScope { get; init; }
    public ImmutableSortedDictionary<string, InscriptionInventoryState> Inventories { get; init; } = ImmutableSortedDictionary<string, InscriptionInventoryState>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableSortedDictionary<string, ActorLoadoutState> Actors { get; init; } = ImmutableSortedDictionary<string, ActorLoadoutState>.Empty.WithComparers(StringComparer.Ordinal);
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableSortedDictionary<Guid, InscriptionOfferState>? Offers { get; init; }

    public string InventoryId(Guid runId, string actorId) => InventoryScope == InscriptionInventoryScope.Run ? "run:" + runId.ToString("D") : "actor:" + actorId;

    public Result Validate(Guid runId, LoadoutPolicyDefinition policy)
    {
        var valid = policy.Validate();
        if (valid.IsFailure) return valid;
        if (PolicyId != policy.LoadoutPolicyId || InventoryScope != policy.InventoryScope || Actors.Count is < 1 or > 64 ||
            Actors.Any(pair => pair.Value == null || string.IsNullOrWhiteSpace(pair.Key) || pair.Key != pair.Value.ActorId) ||
            Inventories.Count != (InventoryScope == InscriptionInventoryScope.Run ? 1 : Actors.Count))
            return Result.Failure("Invalid loadout policy, inventory scope or persistent character registry");
        var ownedIds = new HashSet<Guid>();
        var equippedIds = new HashSet<Guid>();
        if (Offers is { } offers && (offers.Count > 512 || offers.Any(pair => pair.Value == null || pair.Key == Guid.Empty || pair.Key != pair.Value.OfferId ||
            string.IsNullOrWhiteSpace(pair.Value.NodeId) || string.IsNullOrWhiteSpace(pair.Value.StationId) || string.IsNullOrWhiteSpace(pair.Value.ContentRevision) ||
            string.IsNullOrWhiteSpace(pair.Value.Fingerprint) || !Actors.ContainsKey(pair.Value.ActorId) || pair.Value.Options.IsDefault ||
            pair.Value.Options.Length > 512 || pair.Value.Options.Any(string.IsNullOrWhiteSpace) || pair.Value.Options.Distinct().Count() != pair.Value.Options.Length ||
            pair.Value.PurchasedIds.IsDefault || pair.Value.PurchasedIds.Distinct().Count() != pair.Value.PurchasedIds.Length ||
            pair.Value.PurchasedIds.Any(id => !pair.Value.Options.Contains(id)) || pair.Value.MaximumPurchases is < 1 or > 16 ||
            pair.Value.PurchasedIds.Length > pair.Value.MaximumPurchases || pair.Value.Costs.IsDefault ||
            pair.Value.Costs.Any(cost => cost == null || string.IsNullOrWhiteSpace(cost.ResourceId) || !float.IsFinite(cost.Amount) || cost.Amount < 0))))
            return Result.Failure("Invalid captured inscription offer");
        foreach (var (id, inventory) in Inventories)
        {
            if (inventory == null || id != inventory.InventoryId || inventory.Items.IsDefault || inventory.Items.Length > policy.MaximumInventoryEntries ||
                !Actors.Keys.Any(actor => InventoryId(runId, actor) == id)) return Result.Failure("Invalid inscription inventory owner or size");
            foreach (var item in inventory.Items)
                if (item == null || item.InstanceId == Guid.Empty || !ownedIds.Add(item.InstanceId) || string.IsNullOrWhiteSpace(item.DefinitionId) ||
                    string.IsNullOrWhiteSpace(item.AcquisitionRevision) || string.IsNullOrWhiteSpace(item.AcquisitionSource) || item.AcquisitionSequence < 0)
                    return Result.Failure("Invalid or duplicate inscription instance provenance");
        }
        foreach (var (actor, loadout) in Actors)
        {
            if (!Inventories.TryGetValue(InventoryId(runId, actor), out var inventory)) return Result.Failure("Character inventory not found");
            if (loadout.Assignments.Count > policy.Slots.Length) return Result.Failure("Too many equipped inscriptions");
            foreach (var (slot, instanceId) in loadout.Assignments)
                if (!policy.Slots.Any(value => value.SlotId == slot) || !equippedIds.Add(instanceId) || !inventory.Items.Any(item => item.InstanceId == instanceId))
                    return Result.Failure("Invalid slot, foreign ownership or instance equipped by multiple characters");
        }
        return Result.Success();
    }
}
