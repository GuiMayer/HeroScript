using Core.Common;

namespace Core.Combat.Activation;

public interface ICombatActivationCoordinator
{
    Result<CombatActivationResult> StartActivationCycle(Guid combatId, Guid runId, string? rulesId = null);
    Result<CombatActivationResult> GetActivationState(Guid combatId);
    Result<CombatActivationResult> EndCurrentActivation(Guid combatId, Guid runId, string actorId);
    Result<CombatActivationResult> AdvanceToNextActivation(Guid combatId, Guid runId);
    Result<CombatActivationResult> ProcessCurrentAiActivation(Guid combatId, Guid runId, IReadOnlyList<string>? gambitIds = null);
}
