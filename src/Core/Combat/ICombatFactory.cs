using Core.Combat.Models;
using Core.Common;

namespace Core.Combat;

/// <summary>
/// Pure materialization boundary for an initial combat snapshot. It owns no
/// session state and executes no gameplay rules after creation.
/// </summary>
public interface ICombatFactory
{
    Result<CombatState> Create(
        CombatParticipantReference hero,
        IReadOnlyList<CombatParticipantReference> enemies,
        CombatStartOptions options);

    Result<CombatState> Create(
        Entity.Entity hero,
        IReadOnlyList<Entity.Entity> enemies,
        CombatStartOptions options);

    Result<CombatState> Create(
        CombatEntity hero,
        IReadOnlyList<CombatEntity> enemies,
        CombatStartOptions options);
}
