using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Activation;
using Core.Common;
using Core.Content;
using Core.Run.DeckLoadouts;

namespace Core.Run.Supports;

public sealed record ActorSupportLoadout(string ActorId, string? SupportId, string? RoutineId);
public sealed record SupportLoadoutState
{
    public string PolicyId { get; init; } = string.Empty;
    public ImmutableSortedDictionary<string, ImmutableArray<string>> Inventories { get; init; } = ImmutableSortedDictionary<string, ImmutableArray<string>>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableSortedDictionary<string, ActorSupportLoadout> Actors { get; init; } = ImmutableSortedDictionary<string, ActorSupportLoadout>.Empty.WithComparers(StringComparer.Ordinal);
}
public sealed record EquipSupportCommand(string ActorId, string? SupportId, string? RoutineId);
public sealed record CapturedSupportLoadout(string ActorId, string SupportId, string RoutineId, string ParticipantId, string ContentRevision);

/// <summary>Build associations only. Combat, decision and effect authorities remain unchanged.</summary>
public static class SupportLoadoutPlanner
{
    public static Result<SupportLoadoutPolicyDefinition> Policy(RunState run, ContentRuntime runtime)
    {
        if (DeckLoadoutPlanner.Scope(run, runtime, run.PlayerEntityId).IsFailure || run.ResolvedMode?.Definition.SupportLoadoutPolicyId is not { } id)
            return Result<SupportLoadoutPolicyDefinition>.Failure("Support setting, revision or mode mismatch");
        var policy = runtime.GetDefinition<SupportLoadoutPolicyDefinition>("support-loadout-policies", id);
        if (policy.IsFailure) return policy;
        var valid = policy.Value.Validate(runtime);
        return valid.IsFailure ? Result<SupportLoadoutPolicyDefinition>.Failure(valid.Error) : policy;
    }

    public static Result<RunState> Initialize(RunState run, ContentRuntime runtime)
    {
        var policy = Policy(run, runtime);
        if (policy.IsFailure) return Result<RunState>.Failure(policy.Error);
        var state = run.SupportLoadout ?? new() { PolicyId = policy.Value.PolicyId };
        if (state.PolicyId != policy.Value.PolicyId) return Result<RunState>.Failure("Support policy changed; explicit reconciliation is required");
        if (state.Actors.ContainsKey(run.PlayerEntityId))
        {
            var valid = ValidateSelection(run, runtime, policy.Value, state.Actors[run.PlayerEntityId]);
            return valid.IsFailure ? Result<RunState>.Failure(valid.Error) : Result<RunState>.Success(run);
        }
        var owner = InventoryOwner(policy.Value, run.PlayerEntityId);
        if (!state.Inventories.ContainsKey(owner)) state = state with
            { Inventories = state.Inventories.Add(owner, policy.Value.StartingOwnedSupportIds.Order(StringComparer.Ordinal).ToImmutableArray()) };
        string? routine = null;
        if (policy.Value.StartingSupportId is { } id) routine = runtime.GetDefinition<SupportDefinition>("supports", id).Value.DefaultRoutineId;
        state = state with { Actors = state.Actors.Add(run.PlayerEntityId, new(run.PlayerEntityId, policy.Value.StartingSupportId, routine)) };
        return Result<RunState>.Success(run with { SupportLoadout = state });
    }

    public static Result CanChange(RunState run, SupportLoadoutPolicyDefinition policy) =>
        DeckLoadoutPlanner.CanChange(run, new() { EditWindows = policy.EditWindows });

    public static Result<RunState> Plan(RunState run, ContentRuntime runtime, EquipSupportCommand command)
    {
        var policy = Policy(run, runtime);
        if (policy.IsFailure) return Result<RunState>.Failure(policy.Error);
        if (command == null || run.SupportLoadout == null || run.SupportLoadout.PolicyId != policy.Value.PolicyId || command.ActorId != run.PlayerEntityId ||
            !run.SupportLoadout.Actors.ContainsKey(command.ActorId)) return Result<RunState>.Failure("Support equipment requires the current initialized character");
        var window = CanChange(run, policy.Value);
        if (window.IsFailure) return Result<RunState>.Failure(window.Error);
        var selected = new ActorSupportLoadout(command.ActorId, command.SupportId, command.RoutineId);
        var valid = ValidateSelection(run, runtime, policy.Value, selected);
        return valid.IsFailure ? Result<RunState>.Failure(valid.Error) : Result<RunState>.Success(run with
        { SupportLoadout = run.SupportLoadout with { Actors = run.SupportLoadout.Actors.SetItem(command.ActorId, selected) } });
    }

    public static Result<CapturedSupportLoadout?> Capture(RunState run, ContentRuntime runtime)
    {
        var policy = Policy(run, runtime);
        if (policy.IsFailure) return Result<CapturedSupportLoadout?>.Failure(policy.Error);
        if (run.SupportLoadout == null || run.SupportLoadout.PolicyId != policy.Value.PolicyId ||
            !run.SupportLoadout.Actors.TryGetValue(run.PlayerEntityId, out var selected))
            return Result<CapturedSupportLoadout?>.Failure("Support loadout is not initialized");
        var valid = ValidateSelection(run, runtime, policy.Value, selected);
        return valid.IsFailure ? Result<CapturedSupportLoadout?>.Failure(valid.Error) : Result<CapturedSupportLoadout?>.Success(selected.SupportId == null ? null :
            new(selected.ActorId, selected.SupportId, selected.RoutineId!, ParticipantId(selected.ActorId), runtime.Manifest.Revision));
    }

    public static Result<IReadOnlyList<CombatParticipantReference>> Participants(RunState run, ContentRuntime runtime, IReadOnlyList<CombatParticipantReference> participants)
    {
        var captured = Capture(run, runtime);
        if (captured.IsFailure) return Result<IReadOnlyList<CombatParticipantReference>>.Failure(captured.Error);
        if (captured.Value is not { } build) return Result<IReadOnlyList<CombatParticipantReference>>.Success(participants);
        var owner = participants.FirstOrDefault(actor => actor.InstanceId == build.ActorId);
        if (owner == null || participants.Count(actor => actor.InstanceId == build.ActorId) != 1 || participants.Any(actor => actor.InstanceId == build.ParticipantId))
            return Result<IReadOnlyList<CombatParticipantReference>>.Failure("Support owner is missing or participant identity collides");
        var support = runtime.GetDefinition<SupportDefinition>("supports", build.SupportId).Value;
        var routine = runtime.GetDefinition<SupportRoutineDefinition>("support-routines", build.RoutineId).Value;
        var participant = new CombatParticipantReference(build.ParticipantId, support.EntityDefinitionId, owner.SideId,
            new() { Kind = ControllerKind.AI, PolicyId = "gambit", DecisionIds = routine.GambitIds,
                Cadence = new ActorActivationCadence(owner.InstanceId, support.OwnerActivationInterval) }) { CountsForOutcome = support.CountsForOutcome };
        return Result<IReadOnlyList<CombatParticipantReference>>.Success(participants.Append(participant).ToArray());
    }

    public static string ParticipantId(string ownerId) => "$support::" + ownerId;
    public static string InventoryOwner(SupportLoadoutPolicyDefinition policy, string actorId) => policy.InventoryScope == SupportInventoryScope.Run ? "$run" : actorId;

    public static Result ValidateEncounter(RunState run, ContentRuntime runtime, CombatState combat)
    {
        var captured = Capture(run, runtime);
        if (captured.IsFailure) return Result.Failure(captured.Error);
        if (captured.Value is not { } build) return Result.Success();
        var actor = combat.GetActor(build.ParticipantId);
        var support = runtime.GetDefinition<SupportDefinition>("supports", build.SupportId).Value;
        var routine = runtime.GetDefinition<SupportRoutineDefinition>("support-routines", build.RoutineId).Value;
        return actor != null && actor.DefinitionId == support.EntityDefinitionId && actor.ContentRevision == runtime.Manifest.Revision &&
            actor.SideId == combat.GetActor(build.ActorId)?.SideId && actor.CountsForOutcome == support.CountsForOutcome &&
            actor.ControllerBinding.Kind == ControllerKind.AI && actor.ControllerBinding.PolicyId == "gambit" &&
            actor.ControllerBinding.DecisionIds is { } ids && ids.SequenceEqual(routine.GambitIds) &&
            actor.ControllerBinding.Cadence == new ActorActivationCadence(build.ActorId, support.OwnerActivationInterval)
            ? Result.Success() : Result.Failure("Encounter participant does not match captured support equipment");
    }

    private static Result ValidateSelection(RunState run, ContentRuntime runtime, SupportLoadoutPolicyDefinition policy, ActorSupportLoadout selected)
    {
        if (selected.ActorId != run.PlayerEntityId) return Result.Failure("Support owner differs from current character");
        if (selected.SupportId == null) return selected.RoutineId == null ? Result.Success() : Result.Failure("Unequipped support cannot select a routine");
        if (!policy.SupportIds.Contains(selected.SupportId) || run.SupportLoadout?.Inventories.TryGetValue(InventoryOwner(policy, selected.ActorId), out var owned) != true ||
            !owned.Contains(selected.SupportId)) return Result.Failure("Support is not owned and eligible in this inventory");
        var support = runtime.GetDefinition<SupportDefinition>("supports", selected.SupportId);
        if (support.IsFailure) return Result.Failure(support.Error);
        var valid = support.Value.Validate(runtime);
        return valid.IsFailure ? valid : support.Value.RoutineIds.Contains(selected.RoutineId!)
            ? Result.Success() : Result.Failure("Routine is not authorized for this support");
    }
}
