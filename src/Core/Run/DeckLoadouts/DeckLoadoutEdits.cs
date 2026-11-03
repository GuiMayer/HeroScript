using System.Collections.Immutable;
using Core.Common;
using Core.Content;

namespace Core.Run.DeckLoadouts;

public sealed record DeckLoadoutEdit(string ActorId, string GroupId, Guid? ReplacedCardId = null,
    Guid? ReplacementCardId = null, string? ModuleId = null);

/// <summary>Complete gestures become one validated build transition, never an incomplete intermediate deck.</summary>
public static class DeckLoadoutEdits
{
    public static Result<DeckLoadoutChangePlan> Plan(RunState run, ContentRuntime runtime, DeckLoadoutEdit edit)
    {
        Result<DeckLoadoutChangePlan> Fail(string error) => Result<DeckLoadoutChangePlan>.Failure(error);
        if (edit == null) return Fail("Composition edit is required");
        var scope = DeckLoadoutPlanner.Scope(run, runtime, edit.ActorId);
        if (scope.IsFailure) return Fail(scope.Error);
        if (run.ResolvedMode?.Definition.DeckCompositionPolicyId is not { } policyId ||
            run.DeckLoadout?.Actors.TryGetValue(edit.ActorId, out var actor) != true || actor == null || actor.PolicyId != policyId)
            return Fail("Deck composition is unavailable for this mode and character");
        var policy = DeckLoadoutPlanner.Policy(runtime, policyId);
        if (policy.IsFailure) return Fail(policy.Error);
        var group = policy.Value.Groups.FirstOrDefault(g => g.GroupId == edit.GroupId);
        if (group == null || !actor.Groups.TryGetValue(edit.GroupId, out var before)) return Fail("Composition group not found");
        var groups = actor.Groups; var modules = actor.Modules;
        if (edit.ModuleId != null)
        {
            if (edit.ReplacedCardId != null || edit.ReplacementCardId != null || !group.ModuleIds.Contains(edit.ModuleId))
                return Fail("Choose either a card replacement or an eligible module");
            if (modules.GetValueOrDefault(edit.GroupId) == edit.ModuleId)
                return DeckLoadoutPlanner.Plan(run, runtime, edit.ActorId, new(groups, modules));
            var module = runtime.GetDefinition<DeckModuleDefinition>("deck-modules", edit.ModuleId);
            if (module.IsFailure) return Fail(module.Error);
            var used = actor.Groups.Where(pair => pair.Key != edit.GroupId).SelectMany(pair => pair.Value).ToHashSet();
            var selected = ImmutableArray.CreateBuilder<Guid>();
            var owned = DeckLoadoutPlanner.Owned(run, edit.ActorId);
            foreach (var definitionId in module.Value.StartingCardIds)
            {
                var card = owned.FirstOrDefault(c => c.DefinitionId == definitionId && !used.Contains(c.CardInstanceId));
                if (card == null) return Fail($"Missing owned module card: {definitionId}");
                selected.Add(card.CardInstanceId); used.Add(card.CardInstanceId);
            }
            groups = groups.SetItem(edit.GroupId, selected.ToImmutable()); modules = modules.SetItem(edit.GroupId, edit.ModuleId);
        }
        else
        {
            if (edit.ReplacedCardId is not { } outgoing || edit.ReplacementCardId is not { } incoming ||
                outgoing == incoming || !before.Contains(outgoing) || actor.Groups.Values.Any(ids => ids.Contains(incoming)))
                return Fail("Choose one selected card and one reserve card");
            groups = groups.SetItem(edit.GroupId, before.Replace(outgoing, incoming));
        }
        return DeckLoadoutPlanner.Plan(run, runtime, edit.ActorId, new(groups, modules));
    }
}
