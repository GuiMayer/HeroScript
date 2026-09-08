using Core.Combat.Models;
using Core.Common;

namespace Core.Combat;

public interface ICombatFactory
{
    Result<CombatState> Create(IReadOnlyList<CombatParticipantReference> participants, CombatStartOptions options);
    Result<CombatState> Create(IReadOnlyList<CombatActorState> participants, CombatStartOptions options);
}
