using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;

namespace Core.Combat;

public static class CombatTransitions
{
    public static CombatState Create(
        IEnumerable<CombatActorState> actors,
        DeterministicContext context,
        string idScope = "combat")
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(context);
        var ordered = actors.ToImmutableArray();
        var roster = ordered
            .ToImmutableSortedDictionary(actor => actor.InstanceId, actor => actor, StringComparer.Ordinal);
        var combatId = context.AllocateId(idScope);
        return new CombatState
        {
            CombatId = combatId.Value,
            StartedAt = combatId.Context.LogicalTimestamp.UtcDateTime,
            Determinism = combatId.Context,
            Actors = roster,
            ActorOrder = ordered.Select(actor => actor.InstanceId).ToImmutableArray(),
            Sides = roster.Values.Select(actor => actor.SideId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(sideId => sideId, StringComparer.Ordinal)
                .Select(sideId => new CombatSide { SideId = sideId })
                .ToImmutableArray(),
            CurrentTurn = 1,
            Status = CombatStatus.ACTIVE
        };
    }

    public static (CombatState State, CombatAction Action) AppendAction(CombatState state, CombatAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        var actionId = state.Determinism.AllocateId("combat-action");
        var materialized = action with
        {
            ActionId = actionId.Value,
            Timestamp = actionId.Context.LogicalTimestamp.UtcDateTime
        };
        return (state with
        {
            ActionHistory = state.ActionHistory.Append(materialized).ToImmutableList(),
            Determinism = actionId.Context.AdvanceStep()
        }, materialized);
    }
}
