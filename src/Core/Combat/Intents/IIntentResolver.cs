using Core.Combat.Models;
using Core.Combat.Flow;
using Core.Common;
using Core.Run;

namespace Core.Combat.Intents;

public interface IIntentResolver
{
    Result<CombatIntent> ResolveIntent(
        RunState run,
        CombatState combat,
        string actorId,
        IReadOnlyList<string> decisionIds);

    Result<IReadOnlyList<CombatIntent>> ResolveEnemyIntents(
        RunState run,
        CombatState combat,
        IReadOnlyList<string> decisionIds,
        IntentPolicyDefinition policy);
}
