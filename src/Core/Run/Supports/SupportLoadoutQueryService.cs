using System.Collections.Immutable;
using Core.Common;
using Core.Content;

namespace Core.Run.Supports;

public sealed record SupportCatalogEntry(SupportDefinition Definition, ImmutableArray<SupportRoutineDefinition> Routines);
public sealed record SupportCombatView(string ParticipantId, int RemainingOwnerActivations, bool Eligible, bool OwnerAvailable);
public sealed record SupportLoadoutSnapshot(Guid RunId, string SettingId, int RunSequence, ulong ExpectedStep,
    string ContentRevision, string ActorId, bool Enabled, bool CanChange, string? UnavailableReason,
    SupportLoadoutPolicyDefinition? Policy, ActorSupportLoadout? Selection, ImmutableArray<SupportCatalogEntry> Owned,
    SupportCombatView? Combat);
public sealed record SupportLoadoutPreviewRequest(int ExpectedSequence, ulong ExpectedStep, EquipSupportCommand Equipment);
public sealed record SupportLoadoutPreview(SupportLoadoutSnapshot Before, SupportLoadoutSnapshot After);
public interface ISupportLoadoutQueryService
{
    Result<SupportLoadoutSnapshot> Read(Guid runId);
    Result<SupportLoadoutSnapshot> Read(RunState run);
    Result<SupportLoadoutPreview> Preview(Guid runId, SupportLoadoutPreviewRequest request);
}
public sealed class SupportLoadoutQueryService(IRunQueryService runs, IContentRuntimeResolver runtimes) : ISupportLoadoutQueryService
{
    public Result<SupportLoadoutSnapshot> Read(Guid runId)
    {
        var run = runs.GetRun(runId);
        return run.IsFailure ? Result<SupportLoadoutSnapshot>.Failure(run.Error) : Read(run.Value);
    }
    public Result<SupportLoadoutSnapshot> Read(RunState run)
    {
        SupportLoadoutSnapshot Snapshot(bool enabled, bool editable, string? reason, SupportLoadoutPolicyDefinition? policy,
            ActorSupportLoadout? selected, ImmutableArray<SupportCatalogEntry> owned, SupportCombatView? combat) => new(run.RunId,
                run.SettingId, run.Sequence, run.Determinism.Step, run.Determinism.ContentRevision, run.PlayerEntityId,
                enabled, editable, reason, policy, selected, owned, combat);
        if (run.ResolvedMode?.Definition.SupportLoadoutPolicyId == null)
            return Result<SupportLoadoutSnapshot>.Success(Snapshot(false, false, "Supports are not enabled for this character or setting.", null, null, [], null));
        var runtime = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure) return Result<SupportLoadoutSnapshot>.Failure(runtime.Error);
        var policy = SupportLoadoutPlanner.Policy(run, runtime.Value);
        if (policy.IsFailure) return Result<SupportLoadoutSnapshot>.Failure(policy.Error);
        var captured = SupportLoadoutPlanner.Capture(run, runtime.Value);
        if (captured.IsFailure) return Result<SupportLoadoutSnapshot>.Failure(captured.Error);
        var selected = run.SupportLoadout!.Actors[run.PlayerEntityId];
        var owner = SupportLoadoutPlanner.InventoryOwner(policy.Value, run.PlayerEntityId);
        var owned = ImmutableArray.CreateBuilder<SupportCatalogEntry>();
        foreach (var id in run.SupportLoadout.Inventories.GetValueOrDefault(owner, []).Order(StringComparer.Ordinal))
        {
            if (!policy.Value.SupportIds.Contains(id)) continue;
            var definition = runtime.Value.GetDefinition<SupportDefinition>("supports", id);
            if (definition.IsFailure) return Result<SupportLoadoutSnapshot>.Failure(definition.Error);
            owned.Add(new(definition.Value, definition.Value.RoutineIds.Order(StringComparer.Ordinal)
                .Select(routine => runtime.Value.GetDefinition<SupportRoutineDefinition>("support-routines", routine).Value).ToImmutableArray()));
        }
        SupportCombatView? combatView = null;
        if (captured.Value is { } build && run.GetActiveEncounter()?.Combat is { } combat && combat.GetActor(build.ParticipantId) is { } participant)
        {
            var cadence = participant.ControllerBinding.Cadence;
            if (cadence == null) return Result<SupportLoadoutSnapshot>.Failure("Captured support cadence is missing");
            var progress = combat.ActivationState?.CadenceProgress;
            var elapsed = (progress?.Completed.GetValueOrDefault(build.ActorId) ?? 0) - (progress?.ConsumedAnchor.GetValueOrDefault(build.ParticipantId) ?? 0);
            combatView = new(build.ParticipantId, System.Math.Max(0, cadence.Every - elapsed),
                combat.IsActive && Core.Combat.Activation.ActorActivationCadenceReducer.Eligible(combat, build.ParticipantId), combat.GetActor(build.ActorId)?.IsAlive == true);
        }
        var window = SupportLoadoutPlanner.CanChange(run, policy.Value);
        return Result<SupportLoadoutSnapshot>.Success(Snapshot(true, window.IsSuccess, window.IsFailure ? window.Error : null,
            policy.Value, selected, owned.ToImmutable(), combatView));
    }
    public Result<SupportLoadoutPreview> Preview(Guid runId, SupportLoadoutPreviewRequest request)
    {
        var run = runs.GetRun(runId);
        if (run.IsFailure) return Result<SupportLoadoutPreview>.Failure(run.Error);
        if (request == null || request.ExpectedSequence != run.Value.Sequence || request.ExpectedStep != run.Value.Determinism.Step)
            return Result<SupportLoadoutPreview>.Failure(RunCommandErrors.VersionConflictPrefix + " stale support equipment");
        var runtime = runtimes.Resolve(run.Value.Determinism.ContentRevision, run.Value.ConfigName);
        if (runtime.IsFailure) return Result<SupportLoadoutPreview>.Failure(runtime.Error);
        var plan = SupportLoadoutPlanner.Plan(run.Value, runtime.Value, request.Equipment);
        if (plan.IsFailure) return Result<SupportLoadoutPreview>.Failure(plan.Error);
        var before = Read(run.Value);
        if (before.IsFailure) return Result<SupportLoadoutPreview>.Failure(before.Error);
        var after = Read(plan.Value);
        return after.IsFailure ? Result<SupportLoadoutPreview>.Failure(after.Error) : Result<SupportLoadoutPreview>.Success(new(before.Value, after.Value));
    }
}
