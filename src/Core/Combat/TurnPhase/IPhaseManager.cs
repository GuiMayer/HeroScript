using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Gerenciador de fases de turno.
/// Controla transições entre fases, valida ações permitidas, e gerencia o fluxo de um turno TCG-style.
/// </summary>
public interface IPhaseManager
{
    /// <summary>
    /// Inicia uma nova fase no combate.
    /// </summary>
    /// <param name="phase">Fase a ser iniciada</param>
    /// <param name="state">Estado atual do combate</param>
    /// <returns>Novo PhaseState para a fase iniciada</returns>
    Result<PhaseState> StartPhase(TurnPhase phase, CombatState state);
    
    /// <summary>
    /// Transiciona para a próxima fase na sequência.
    /// </summary>
    /// <param name="currentPhase">Estado atual da fase</param>
    /// <param name="sequence">Definição da sequência de fases</param>
    /// <returns>Novo PhaseState para a próxima fase</returns>
    Result<PhaseState> TransitionToNextPhase(PhaseState currentPhase, PhaseSequenceDefinition sequence);
    
    /// <summary>
    /// Transiciona para uma fase específica (se permitido pela sequência).
    /// </summary>
    /// <param name="targetPhase">Fase de destino</param>
    /// <param name="currentPhase">Estado atual da fase</param>
    /// <param name="sequence">Definição da sequência de fases</param>
    /// <returns>Novo PhaseState para a fase de destino</returns>
    Result<PhaseState> TransitionToPhase(TurnPhase targetPhase, PhaseState currentPhase, PhaseSequenceDefinition sequence);
    
    /// <summary>
    /// Verifica se uma ação pode ser executada na fase atual.
    /// </summary>
    /// <param name="action">Tipo de ação a ser executada</param>
    /// <param name="phaseState">Estado atual da fase</param>
    /// <param name="sequence">Definição da sequência de fases</param>
    /// <returns>True se a ação é permitida</returns>
    bool CanExecuteAction(ActionType action, PhaseState phaseState, PhaseSequenceDefinition sequence);
    
    /// <summary>
    /// Obtém lista de ações permitidas na fase atual.
    /// </summary>
    /// <param name="phaseState">Estado atual da fase</param>
    /// <param name="sequence">Definição da sequência de fases</param>
    /// <returns>Lista de tipos de ação permitidos</returns>
    List<ActionType> GetAllowedActions(PhaseState phaseState, PhaseSequenceDefinition sequence);
    
    /// <summary>
    /// Valida se uma transição de fase é permitida.
    /// </summary>
    /// <param name="from">Fase de origem</param>
    /// <param name="to">Fase de destino</param>
    /// <param name="sequence">Definição da sequência de fases</param>
    /// <returns>Result indicando se a transição é válida</returns>
    Result ValidatePhaseTransition(TurnPhase from, TurnPhase to, PhaseSequenceDefinition sequence);
}
