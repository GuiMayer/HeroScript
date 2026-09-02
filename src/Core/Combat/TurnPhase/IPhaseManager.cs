using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.TurnPhase;

public interface IPhaseManager
{
    Result<PhaseState> StartPhase(string phaseId, CombatState state, PhaseSequenceDefinition sequence);
    Result<PhaseState> TransitionToNextPhase(PhaseState currentPhase, PhaseSequenceDefinition sequence);
    Result<PhaseState> TransitionToPhase(
        string targetPhaseId,
        PhaseState currentPhase,
        PhaseSequenceDefinition sequence);
    bool CanExecuteAction(ActionType action, PhaseState phaseState, PhaseSequenceDefinition sequence);
    List<ActionType> GetAllowedActions(PhaseState phaseState, PhaseSequenceDefinition sequence);
    Result ValidatePhaseTransition(string fromPhaseId, string toPhaseId, PhaseSequenceDefinition sequence);
}
