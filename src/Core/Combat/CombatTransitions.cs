using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;

namespace Core.Combat;

/// <summary>
/// Transições puras do agregado de combate. IDs, tempo e avanço de passo são
/// derivados exclusivamente do contexto armazenado no snapshot de entrada.
/// </summary>
public static class CombatTransitions
{
    public static CombatState Create(
        CombatEntity hero,
        IEnumerable<CombatEntity> enemies,
        DeterministicContext context,
        string idScope = "combat")
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(enemies);
        ArgumentNullException.ThrowIfNull(context);

        var combatId = context.AllocateId(idScope);
        var materializedHero = string.IsNullOrWhiteSpace(hero.SideId) ? hero with { SideId = "player" } : hero;
        var materializedEnemies = enemies.Select(entity => string.IsNullOrWhiteSpace(entity.SideId)
            ? entity with { SideId = "opposition" } : entity).ToImmutableArray();
        var sides = materializedEnemies.Select(entity => entity.SideId)
            .Append(materializedHero.SideId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sideId => sideId, StringComparer.Ordinal)
            .Select(sideId => new CombatSide
            {
                SideId = sideId,
                Controller = string.Equals(sideId, materializedHero.SideId, StringComparison.Ordinal)
                    ? ControllerKind.Player
                    : ControllerKind.AI
            })
            .ToImmutableArray();
        return new CombatState
        {
            CombatId = combatId.Value,
            StartedAt = combatId.Context.LogicalTimestamp.UtcDateTime,
            Determinism = combatId.Context,
            Hero = materializedHero,
            Enemies = materializedEnemies,
            Sides = sides,
            CurrentTurn = 1,
            Status = CombatStatus.ACTIVE
        };
    }

    public static (CombatState State, CombatAction Action) AppendAction(
        CombatState state,
        CombatAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);

        var actionId = state.Determinism.AllocateId("combat-action");
        var materialized = action with
        {
            ActionId = actionId.Value,
            Timestamp = actionId.Context.LogicalTimestamp.UtcDateTime
        };
        var context = actionId.Context.AdvanceStep();
        var next = state with
        {
            ActionHistory = state.ActionHistory.Append(materialized).ToImmutableList(),
            Determinism = context
        };
        return (next, materialized);
    }
}
