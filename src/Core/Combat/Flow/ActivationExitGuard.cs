using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Common;
using Core.Run;

namespace Core.Combat.Flow;

public interface IActivationExitGuard { Result Validate(RunState run, CombatState combat); }

/// <summary>Adapts existing command conditions to direct lifecycle exits, without adding rule authority.</summary>
public sealed class ActivationExitGuard(ILegalActionResolver legality) : IActivationExitGuard
{
    public Result Validate(RunState run, CombatState combat) => legality.ValidateConfiguredConditions(run, combat,
        new() { ActorId = combat.ActivationState?.ActiveActorId ?? "", ActionType = ActionType.END_TURN }, CombatCommandOrigin.PendingResolution);
}
