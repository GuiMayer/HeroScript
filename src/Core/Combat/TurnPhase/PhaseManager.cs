using Core.Combat.Models;
using Core.Common;
using Core.Events;
using Core.Logging;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Implementação do gerenciador de fases de turno.
/// Controla transições, valida ações, e gerencia o fluxo de fases TCG-style.
/// </summary>
public class PhaseManager : IPhaseManager
{
    private readonly IPrioritySystem _prioritySystem;
    private readonly ILogger _logger;
    private readonly IEventBus? _eventBus;
    
    public PhaseManager(IPrioritySystem prioritySystem, ILogger logger, IEventBus? eventBus = null)
    {
        _prioritySystem = prioritySystem ?? throw new ArgumentNullException(nameof(prioritySystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus;
    }
    
    public Result<PhaseState> StartPhase(TurnPhase phase, CombatState state)
    {
        if (phase == TurnPhase.NONE)
        {
            return Result<PhaseState>.Failure("Cannot start NONE phase");
        }
        
        // Obter ordem de jogadores para inicializar prioridade
        var playerOrder = _prioritySystem.GetPlayerOrder(state);
        if (playerOrder.Count == 0)
        {
            return Result<PhaseState>.Failure("No players found in combat state");
        }
        
        // Criar dicionário de prioridade com todos os jogadores
        var priorityDict = playerOrder.ToDictionary(id => id, _ => false);
        
        var phaseState = new PhaseState
        {
            CurrentPhase = phase,
            PhaseIndex = 0,
            ActivePlayerId = playerOrder[0], // Primeiro jogador tem prioridade
            CanTransition = false,
            PlayerPassedPriority = priorityDict,
            PhaseStartTime = DateTime.UtcNow
        };
        
        _logger.LogDebug($"Started phase {phase} with active player {playerOrder[0]}");
        
        return Result<PhaseState>.Success(phaseState);
    }
    
    public Result<PhaseState> TransitionToNextPhase(PhaseState currentPhase, PhaseSequenceDefinition sequence)
    {
        if (sequence.Phases.Count == 0)
        {
            return Result<PhaseState>.Failure("Phase sequence is empty");
        }
        
        // Encontrar índice da próxima fase
        var nextIndex = currentPhase.PhaseIndex + 1;
        
        // Se chegou ao fim da sequência, retornar erro (caller deve iniciar novo turno)
        if (nextIndex >= sequence.Phases.Count)
        {
            return Result<PhaseState>.Failure("Reached end of phase sequence");
        }
        
        var nextPhase = sequence.Phases[nextIndex];
        
        // Validar transição
        var validationResult = ValidatePhaseTransition(currentPhase.CurrentPhase, nextPhase, sequence);
        if (validationResult.IsFailure)
        {
            return Result<PhaseState>.Failure(validationResult.Error);
        }
        
        // Criar novo estado de fase
        var newPhaseState = currentPhase with
        {
            CurrentPhase = nextPhase,
            PhaseIndex = nextIndex,
            CanTransition = false,
            PhaseStartTime = DateTime.UtcNow
        };
        
        // Resetar prioridade para a nova fase
        var resetResult = _prioritySystem.ResetPriority(newPhaseState, currentPhase.ActivePlayerId);
        if (resetResult.IsFailure)
        {
            return Result<PhaseState>.Failure($"Failed to reset priority: {resetResult.Error}");
        }
        
        _logger.LogDebug($"Transitioned from {currentPhase.CurrentPhase} to {nextPhase}");
        
        return Result<PhaseState>.Success(resetResult.Value);
    }
    
    public Result<PhaseState> TransitionToPhase(TurnPhase targetPhase, PhaseState currentPhase, PhaseSequenceDefinition sequence)
    {
        if (targetPhase == TurnPhase.NONE)
        {
            return Result<PhaseState>.Failure("Cannot transition to NONE phase");
        }
        
        // Encontrar índice da fase alvo
        var targetIndex = sequence.Phases.IndexOf(targetPhase);
        if (targetIndex == -1)
        {
            return Result<PhaseState>.Failure($"Phase {targetPhase} not found in sequence");
        }
        
        // Validar transição
        var validationResult = ValidatePhaseTransition(currentPhase.CurrentPhase, targetPhase, sequence);
        if (validationResult.IsFailure)
        {
            return Result<PhaseState>.Failure(validationResult.Error);
        }
        
        // Criar novo estado de fase
        var newPhaseState = currentPhase with
        {
            CurrentPhase = targetPhase,
            PhaseIndex = targetIndex,
            CanTransition = false,
            PhaseStartTime = DateTime.UtcNow
        };
        
        // Resetar prioridade para a nova fase
        var resetResult = _prioritySystem.ResetPriority(newPhaseState, currentPhase.ActivePlayerId);
        if (resetResult.IsFailure)
        {
            return Result<PhaseState>.Failure($"Failed to reset priority: {resetResult.Error}");
        }
        
        _logger.LogDebug($"Transitioned from {currentPhase.CurrentPhase} to {targetPhase}");
        
        return Result<PhaseState>.Success(resetResult.Value);
    }
    
    public bool CanExecuteAction(ActionType action, PhaseState phaseState, PhaseSequenceDefinition sequence)
    {
        // Se fase é NONE, permite todas as ações (retrocompatibilidade)
        if (phaseState.CurrentPhase == TurnPhase.NONE)
        {
            return true;
        }
        
        // Obter definição da fase atual
        if (!sequence.PhaseDetails.TryGetValue(phaseState.CurrentPhase, out var phaseDefinition))
        {
            _logger.LogWarning($"Phase definition not found for {phaseState.CurrentPhase}");
            return false;
        }
        
        // Verificar se ação está na lista de permitidas
        return phaseDefinition.AllowedActions.Contains(action);
    }
    
    public List<ActionType> GetAllowedActions(PhaseState phaseState, PhaseSequenceDefinition sequence)
    {
        // Se fase é NONE, retorna lista vazia (sem restrições)
        if (phaseState.CurrentPhase == TurnPhase.NONE)
        {
            return new List<ActionType>();
        }
        
        // Obter definição da fase atual
        if (!sequence.PhaseDetails.TryGetValue(phaseState.CurrentPhase, out var phaseDefinition))
        {
            _logger.LogWarning($"Phase definition not found for {phaseState.CurrentPhase}");
            return new List<ActionType>();
        }
        
        return phaseDefinition.AllowedActions;
    }
    
    public Result ValidatePhaseTransition(TurnPhase from, TurnPhase to, PhaseSequenceDefinition sequence)
    {
        // Transição de NONE é sempre permitida (inicialização)
        if (from == TurnPhase.NONE)
        {
            return Result.Success();
        }
        
        // Obter definição da fase de origem
        if (!sequence.PhaseDetails.TryGetValue(from, out var fromDefinition))
        {
            return Result.Failure($"Phase definition not found for {from}");
        }
        
        // Se há lista de próximas fases válidas, verificar
        if (fromDefinition.ValidNextPhases.Count > 0)
        {
            if (!fromDefinition.ValidNextPhases.Contains(to))
            {
                return Result.Failure($"Cannot transition from {from} to {to} - not in valid next phases");
            }
        }
        else
        {
            // Se não há lista específica, verificar se é a próxima na sequência
            var fromIndex = sequence.Phases.IndexOf(from);
            var toIndex = sequence.Phases.IndexOf(to);
            
            if (fromIndex == -1 || toIndex == -1)
            {
                return Result.Failure($"Phase not found in sequence");
            }
            
            // Permitir apenas transição para próxima fase, a menos que skipping seja permitido
            if (toIndex != fromIndex + 1 && !sequence.AllowPhaseSkipping)
            {
                return Result.Failure($"Cannot skip phases - transition from {from} to {to} not allowed");
            }
            
            // Se skipping é permitido, verificar se não está voltando
            if (sequence.AllowPhaseSkipping && toIndex <= fromIndex)
            {
                return Result.Failure($"Cannot go backwards in phase sequence");
            }
        }
        
        return Result.Success();
    }
}
