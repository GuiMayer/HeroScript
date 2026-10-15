using System.Collections.Immutable;
using Core.Caching;
using Core.Common;
using Core.Content;
using Core.Determinism;

namespace Core.Run.Loadouts;

public sealed record ApplyLoadoutCommand(string ActorId, ImmutableSortedDictionary<string, Guid> Assignments);
public sealed record LoadoutPreviewRequest(string ActorId, ImmutableSortedDictionary<string, Guid> Assignments, int ExpectedSequence, ulong ExpectedStep);
public sealed record LoadoutItemView(InscriptionInstance Instance, InscriptionDefinition Definition, string? EquippedActorId, string? SlotId);
public sealed record LoadoutSnapshot(Guid RunId, int RunSequence, ulong ExpectedStep, string ContentRevision, string ActorId,
    string InventoryId, LoadoutPolicyDefinition Policy, bool CanEquip, float UsedCapacity,
    ImmutableArray<string> ActorIds, ImmutableArray<LoadoutItemView> Items, ImmutableSortedDictionary<string, Guid> Assignments);
public sealed record LoadoutPreview(LoadoutSnapshot Before, LoadoutSnapshot After);

public interface ILoadoutQueryService
{
    Result<LoadoutSnapshot> Read(Guid runId, string actorId);
    Result<LoadoutSnapshot> Read(RunState run, string actorId);
    Result<LoadoutPreview> Preview(Guid runId, LoadoutPreviewRequest request);
}

/// <summary>Read-only versioned projection. The same planner validates preview and commands.</summary>
public sealed class LoadoutQueryService(IRunQueryService runs, IContentRuntimeResolver runtimes, LoadoutChangePlanner planner) : ILoadoutQueryService, ICacheService
{
    private readonly LruCache<string, LoadoutSnapshot> _cache = new(32);
    public string CacheName => "RunInscriptionLoadouts";
    public CacheLayer Layer => CacheLayer.Derived;
    public void Invalidate(string? key = null) { if (key == null) _cache.Clear(); else _cache.Remove(key); }
    public CacheServiceStats GetStats() => new LruCacheService<string, LoadoutSnapshot>(_cache, CacheName).GetStats();

    public Result<LoadoutSnapshot> Read(Guid runId, string actorId)
    {
        var run = runs.GetRun(runId);
        return run.IsFailure ? Result<LoadoutSnapshot>.Failure(run.Error) : Read(run.Value, actorId);
    }

    public Result<LoadoutSnapshot> Read(RunState run, string actorId)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("loadout_projection");
        Result<LoadoutSnapshot> Fail(string error) => Result<LoadoutSnapshot>.Failure(error);
        if (string.IsNullOrWhiteSpace(actorId)) return Fail("Character identity is required");
        var key = CanonicalJson.ComputeHash(run) + ":" + actorId;
        if (_cache.TryGetValue(key, out var cached)) return Result<LoadoutSnapshot>.Success(cached!);
        var runtime = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure) return Fail(runtime.Error);
        var policy = LoadoutContentAccess.Policy(run, runtime.Value);
        if (policy.IsFailure) return Fail(policy.Error);
        if (run.Loadouts == null || !run.Loadouts.Actors.TryGetValue(actorId, out var actor)) return Fail("Loadout character not found");
        var valid = run.Loadouts.Validate(run.RunId, policy.Value);
        if (valid.IsFailure) return Fail(valid.Error);
        var inventoryId = run.Loadouts.InventoryId(run.RunId, actorId);
        var inventory = run.Loadouts.Inventories[inventoryId];
        var items = ImmutableArray.CreateBuilder<LoadoutItemView>();
        var cost = 0d;
        foreach (var instance in inventory.Items.OrderBy(value => value.InstanceId))
        {
            var acquired = runtimes.Resolve(instance.AcquisitionRevision, run.ConfigName);
            if (acquired.IsFailure) return Fail(acquired.Error);
            if (acquired.Value.Manifest.Revision != instance.AcquisitionRevision || !string.Equals(acquired.Value.Manifest.ConfigName, run.ConfigName, StringComparison.OrdinalIgnoreCase))
                return Fail("Inscription acquisition setting or revision mismatch");
            var definition = acquired.Value.GetDefinition<InscriptionDefinition>("inscriptions", instance.DefinitionId);
            if (definition.IsFailure) return Fail(definition.Error);
            var owner = run.Loadouts.Actors.Values.FirstOrDefault(value => value.Assignments.Values.Contains(instance.InstanceId));
            var slot = owner?.Assignments.First(value => value.Value == instance.InstanceId).Key;
            if (owner?.ActorId == actorId) cost += definition.Value.EquipmentCost;
            items.Add(new(instance, definition.Value, owner?.ActorId, slot));
        }
        if (!double.IsFinite(cost) || cost > float.MaxValue) return Fail("Invalid inscription equipment cost");
        var snapshot = new LoadoutSnapshot(run.RunId, run.Sequence, run.Determinism.Step, run.Determinism.ContentRevision,
            actorId, inventoryId, policy.Value, LoadoutContentAccess.CanEquip(run, policy.Value), (float)cost,
            run.Loadouts.Actors.Keys.ToImmutableArray(), items.ToImmutable(), actor.Assignments);
        _cache.Set(key, snapshot);
        return Result<LoadoutSnapshot>.Success(snapshot);
    }

    public Result<LoadoutPreview> Preview(Guid runId, LoadoutPreviewRequest request)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("loadout_preview");
        var run = runs.GetRun(runId);
        if (run.IsFailure) return Result<LoadoutPreview>.Failure(run.Error);
        if (request.ExpectedSequence != run.Value.Sequence || request.ExpectedStep != run.Value.Determinism.Step)
            return Result<LoadoutPreview>.Failure(RunCommandErrors.VersionConflict(request.ExpectedSequence, request.ExpectedStep, run.Value));
        var planned = planner.Plan(run.Value, request.ActorId, request.Assignments);
        if (planned.IsFailure) return Result<LoadoutPreview>.Failure(planned.Error);
        var before = Read(run.Value, request.ActorId);
        var after = Read(planned.Value.Candidate, request.ActorId);
        return before.IsFailure || after.IsFailure ? Result<LoadoutPreview>.Failure(before.IsFailure ? before.Error : after.Error)
            : Result<LoadoutPreview>.Success(new(before.Value, after.Value with { ExpectedStep = before.Value.ExpectedStep }));
    }
}
