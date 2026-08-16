using Core.Combat.Models;
using Core.Damage;

namespace Core.Effects;

public static class EffectTargetResolver
{
    public static IReadOnlyList<string> Resolve(
        EffectTarget targetType,
        string sourceId,
        string primaryTargetId,
        IEffectContext context,
        IRandomProvider randomProvider)
    {
        if (context.CombatState == null)
        {
            return targetType switch
            {
                EffectTarget.SELF => new[] { sourceId },
                EffectTarget.TARGET => new[] { primaryTargetId },
                _ => Array.Empty<string>()
            };
        }

        return targetType switch
        {
            EffectTarget.SELF => new[] { sourceId },
            EffectTarget.TARGET => new[] { primaryTargetId },
            EffectTarget.ALL_ENEMIES => context.CombatState.Enemies
                .Select(entity => entity.EntityId).ToArray(),
            EffectTarget.ALL_ALLIES => new[] { context.CombatState.Hero.EntityId },
            EffectTarget.RANDOM_ENEMY => RandomEnemy(context.CombatState, randomProvider),
            EffectTarget.LOWEST_HP_ENEMY => ByHealth(context.CombatState, descending: false),
            EffectTarget.HIGHEST_HP_ENEMY => ByHealth(context.CombatState, descending: true),
            _ => new[] { primaryTargetId }
        };
    }

    private static IReadOnlyList<string> RandomEnemy(
        CombatState state,
        IRandomProvider randomProvider)
    {
        var alive = state.Enemies.Where(entity => entity.IsAlive).ToArray();
        return alive.Length == 0
            ? Array.Empty<string>()
            : new[] { alive[randomProvider.Next(0, alive.Length)].EntityId };
    }

    private static IReadOnlyList<string> ByHealth(CombatState state, bool descending)
    {
        var alive = state.Enemies.Where(entity => entity.IsAlive);
        var selected = descending
            ? alive.OrderByDescending(Health).ThenBy(entity => entity.EntityId, StringComparer.Ordinal).FirstOrDefault()
            : alive.OrderBy(Health).ThenBy(entity => entity.EntityId, StringComparer.Ordinal).FirstOrDefault();
        return selected == null ? Array.Empty<string>() : new[] { selected.EntityId };
    }

    private static float Health(CombatEntity entity) =>
        entity.GetResource("health")?.Current ?? 0f;
}
