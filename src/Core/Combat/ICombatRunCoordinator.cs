using Core.Combat.Models;
using Core.Common;

namespace Core.Combat;

public interface ICombatRunCoordinator
{
    Result<CombatRunActionResult> ExecuteAction(Guid combatId, CombatActionCommand command);
}
