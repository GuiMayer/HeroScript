using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Run.Content;
using Core.Run.Loadouts;

namespace Core.Run.DeckLoadouts;

/// <summary>Pure build transitions. No IDs, RNG, journal writes or card circulation are performed here.</summary>
public static class DeckLoadoutPlanner
{
    public static Result<RunState> Initialize(RunState run, ContentRuntime runtime, string actorId, string policyId)
    {
        var scope = Scope(run, runtime, actorId);
        if (scope.IsFailure) return Result<RunState>.Failure(scope.Error);
        var policy = Policy(runtime, policyId);
        if (policy.IsFailure) return Result<RunState>.Failure(policy.Error);
        if (run.DeckLoadout?.Actors.TryGetValue(actorId, out var existing) == true)
        {
            if (existing.ActorId != actorId) return Result<RunState>.Failure("Composition actor identity mismatch");
            if (existing.PolicyId != policyId) return Result<RunState>.Failure("Composition policy changed; explicit reconciliation is required");
            var valid = ValidateSelection(run, runtime, policy.Value, existing.Groups, existing.Modules, actorId);
            return valid.IsFailure ? Result<RunState>.Failure(valid.Error) : Result<RunState>.Success(run);
        }
        var groups = ImmutableSortedDictionary.CreateBuilder<string, ImmutableArray<Guid>>(StringComparer.Ordinal);
        var modules = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var owned = Owned(run, actorId);
        var used = new HashSet<Guid>();
        foreach (var group in policy.Value.Groups)
        {
            var wanted = group.StartingCardIds;
            if (!group.ModuleIds.IsEmpty)
            {
                var module = runtime.GetDefinition<DeckModuleDefinition>("deck-modules", group.StartingModuleId!);
                if (module.IsFailure) return Result<RunState>.Failure(module.Error);
                modules[group.GroupId] = module.Value.ModuleId; wanted = module.Value.StartingCardIds;
            }
            var selected = ImmutableArray.CreateBuilder<Guid>();
            foreach (var definitionId in wanted)
            {
                var instance = owned.FirstOrDefault(card => card.DefinitionId == definitionId && !used.Contains(card.CardInstanceId));
                if (instance == null) return Result<RunState>.Failure($"Missing owned starting card in group {group.GroupId}: {definitionId}");
                used.Add(instance.CardInstanceId); selected.Add(instance.CardInstanceId);
            }
            groups[group.GroupId] = Normalize(run, selected);
        }
        var actor = new ActorDeckLoadoutState { ActorId = actorId, PolicyId = policyId, Groups = groups.ToImmutable(), Modules = modules.ToImmutable() };
        return Result<RunState>.Success(run with { DeckLoadout = (run.DeckLoadout ?? new()) with
        { Actors = (run.DeckLoadout?.Actors ?? ImmutableSortedDictionary.Create<string, ActorDeckLoadoutState>(StringComparer.Ordinal)).SetItem(actorId, actor) } });
    }

    public static Result<DeckLoadoutChangePlan> Plan(RunState run, ContentRuntime runtime, string actorId, DeckLoadoutSelection selection)
    {
        Result<DeckLoadoutChangePlan> Fail(string error) => Result<DeckLoadoutChangePlan>.Failure(error);
        var scope = Scope(run, runtime, actorId);
        if (scope.IsFailure) return Fail(scope.Error);
        var loadout = run.DeckLoadout;
        if (loadout == null || !loadout.Actors.TryGetValue(actorId, out var before)) return Fail("Deck composition is not initialized for this actor");
        if (before.ActorId != actorId) return Fail("Composition actor identity mismatch");
        var policy = Policy(runtime, before.PolicyId);
        if (policy.IsFailure) return Fail(policy.Error);
        var window = CanChange(run, policy.Value);
        if (window.IsFailure) return Fail(window.Error);
        if (selection == null) return Fail("Composition selection is required");
        var valid = ValidateSelection(run, runtime, policy.Value, selection.Groups, selection.Modules, actorId);
        if (valid.IsFailure) return Fail(valid.Error);
        var after = before with
        {
            Groups = selection.Groups.ToImmutableSortedDictionary(pair => pair.Key, pair => Normalize(run, pair.Value), StringComparer.Ordinal),
            Modules = selection.Modules.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
        var candidate = run with { DeckLoadout = loadout with { Actors = loadout.Actors.SetItem(actorId, after) } };
        return Result<DeckLoadoutChangePlan>.Success(new(candidate, before, after));
    }

    public static Result<CapturedDeckLoadout> Capture(RunState run, ContentRuntime runtime, string actorId)
    {
        Result<CapturedDeckLoadout> Fail(string error) => Result<CapturedDeckLoadout>.Failure(error);
        var scope = Scope(run, runtime, actorId);
        if (scope.IsFailure) return Fail(scope.Error);
        if (run.DeckLoadout?.Actors.TryGetValue(actorId, out var actor) != true) return Fail("Deck composition is not initialized for this actor");
        if (actor.ActorId != actorId) return Fail("Composition actor identity mismatch");
        var policy = Policy(runtime, actor.PolicyId);
        if (policy.IsFailure) return Fail(policy.Error);
        var valid = ValidateSelection(run, runtime, policy.Value, actor.Groups, actor.Modules, actorId);
        if (valid.IsFailure) return Fail(valid.Error);
        return Result<CapturedDeckLoadout>.Success(new(actorId, actor.PolicyId, runtime.Manifest.Revision, run.Sequence, run.Determinism.Step,
            actor.Groups, actor.Modules, policy.Value.Groups.SelectMany(group => actor.Groups[group.GroupId]).ToImmutableArray()));
    }

    public static Result CanChange(RunState run, DeckCompositionPolicyDefinition policy)
    {
        if (run.Lifecycle != RunLifecycleState.Active || run.ActiveEncounterId != null)
            return Result.Failure("Composition changes require an active campaign outside a pending encounter");
        var node = run.Map.Nodes.FirstOrDefault(node => node.NodeId == run.CurrentNodeId);
        var preparation = node?.Activity.Type == RunActivityType.Preparation && !run.Map.ResolvedNodeIds.Contains(node.NodeId) &&
            !run.CompletedActivityNodeIds.Contains(node.NodeId);
        return policy.EditWindows.Contains(LoadoutEquipmentWindow.OutsideCombat) || preparation && policy.EditWindows.Contains(LoadoutEquipmentWindow.Preparation)
            ? Result.Success() : Result.Failure("Composition changes are unavailable in this activity");
    }

    public static ImmutableArray<CardInstanceState> Owned(RunState run, string actorId) => run.Deck.Topology.Instances.Values
        .Where(card => PersistentCardOwnership.Owns(run, actorId, card)).OrderBy(card => card.CreationOrdinal).ThenBy(card => card.CardInstanceId).ToImmutableArray();

    public static Result<DeckCompositionPolicyDefinition> Policy(ContentRuntime runtime, string policyId)
    {
        var policy = runtime.GetDefinition<DeckCompositionPolicyDefinition>("deck-composition-policies", policyId);
        if (policy.IsFailure) return policy;
        var valid = policy.Value.Validate(runtime);
        return valid.IsFailure ? Result<DeckCompositionPolicyDefinition>.Failure(valid.Error) : policy;
    }

    public static Result Scope(RunState run, ContentRuntime runtime, string actorId) =>
        runtime.Manifest.Revision == run.Determinism.ContentRevision && string.Equals(runtime.Manifest.ConfigName, run.ConfigName, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(actorId) && actorId == run.PlayerEntityId
        ? Result.Success() : Result.Failure("Composition setting, revision or current actor mismatch");

    public static RunState ReconcileDestroyedCards(RunState run)
    {
        if (run.DeckLoadout == null) return run;
        var state = run.DeckLoadout;
        foreach (var (id, actor) in state.Actors)
        {
            if (!actor.Groups.Values.SelectMany(ids => ids).Any(card => run.Deck.GetCard(card) == null)) continue;
            var groups = actor.Groups.ToImmutableSortedDictionary(pair => pair.Key,
                pair => pair.Value.Where(card => run.Deck.GetCard(card) != null).ToImmutableArray(), StringComparer.Ordinal);
            state = state with { Actors = state.Actors.SetItem(id, actor with { Groups = groups }) };
        }
        return ReferenceEquals(state, run.DeckLoadout) ? run : run with { DeckLoadout = state };
    }

    private static Result ValidateSelection(RunState run, ContentRuntime runtime, DeckCompositionPolicyDefinition policy,
        ImmutableSortedDictionary<string, ImmutableArray<Guid>> groups, ImmutableSortedDictionary<string, string> modules, string actorId)
    {
        if (groups == null || modules == null || !groups.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(policy.Groups.Select(g => g.GroupId)) ||
            !modules.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(policy.Groups.Where(g => !g.ModuleIds.IsEmpty).Select(g => g.GroupId)))
            return Result.Failure("Composition groups/modules do not match the configured policy");
        var used = new HashSet<Guid>();
        foreach (var group in policy.Groups)
        {
            var ids = groups[group.GroupId];
            if (ids.IsDefault || ids.Length < group.Minimum || ids.Length > group.Maximum)
                return Result.Failure($"Group {group.GroupId} requires {group.Minimum}..{group.Maximum} cards");
            var allowed = group.CardIds;
            if (!group.ModuleIds.IsEmpty)
            {
                var id = modules[group.GroupId];
                if (!group.ModuleIds.Contains(id)) return Result.Failure($"Module is not eligible for group {group.GroupId}");
                var module = runtime.GetDefinition<DeckModuleDefinition>("deck-modules", id);
                if (module.IsFailure) return Result.Failure(module.Error);
                allowed = module.Value.CardIds;
            }
            var definitions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var card = run.Deck.GetCard(id);
                if (id == Guid.Empty || card == null || !PersistentCardOwnership.Owns(run, actorId, card))
                    return Result.Failure($"Card is not a persistent owned instance: {id}");
                if (!used.Add(id)) return Result.Failure("The same card instance cannot occupy multiple composition positions");
                if (!allowed.Contains(card.DefinitionId)) return Result.Failure($"Card is not eligible for group {group.GroupId}: {card.DefinitionId}");
                if (!definitions.Add(card.DefinitionId) && !group.AllowDuplicateDefinitions)
                    return Result.Failure($"Duplicate definitions are not allowed in group {group.GroupId}");
            }
        }
        return Result.Success();
    }

    private static ImmutableArray<Guid> Normalize(RunState run, IEnumerable<Guid> ids) => ids
        .OrderBy(id => run.Deck.GetCard(id)!.CreationOrdinal).ThenBy(id => id).ToImmutableArray();
}
