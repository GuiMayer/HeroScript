using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.Intents;

public interface IIntentResolver
{
    Result<CombatIntent> ResolveIntent(CombatState combat, string actorId, Guid? runId = null, IReadOnlyList<string>? gambitIds = null);
    Result<IReadOnlyList<CombatIntent>> ResolveEnemyIntents(CombatState combat, Guid? runId = null, IReadOnlyList<string>? gambitIds = null);
}
