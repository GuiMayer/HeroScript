using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Logging;
using TurnPhaseEnum = Core.Combat.TurnPhase.TurnPhase;

namespace Core.Combat;

/// <summary>
/// Métodos de extensão para integrar o sistema de fases ao CombatSystem.
/// Permite retrocompatibilidade: o combate funciona com ou sem sistema de fases ativo.
/// </summary>
public static class CombatSystemPhaseExtensions
{
    /// <summary>
    /// Inicializa o sistema de fases para um combate.
    /// Deve ser chamado após StartCombat se o sistema de fases for desejado.
    /// </summary>
    /// <param name="state">Estado do combate</param>
    /// <param name="phaseSystem">Sistema de fases configurado</param>
    /// <param name="playerIds">IDs dos jogadores em ordem de prioridade inicial</param>
    /// <returns>Estado atualizado com sistema de fases inicializado</returns>
    public static Result<CombatState> InitializePhaseSystem(
        this CombatState state,
        PhaseSystem phaseSystem,
        List<string> playerIds)
    {
        if (phaseSystem == null)
        {
            return Result<CombatState>.Failure("Phase system cannot be null");
        }
        
        if (playerIds == null || playerIds.Count == 0)
        {
            return Result<CombatState>.Failure("At least one player ID is required");
        }
        
        // Criar estado inicial de fase
        var firstPhase = phaseSystem.Sequence.Phases.FirstOrDefault();
        if (firstPhase == TurnPhaseEnum.NONE)
        {
            return Result<CombatState>.Failure("Phase sequence must have at least one valid phase");
        }
        
        var phaseState = new PhaseState
        {
            CurrentPhase = firstPhase,
            PhaseSequence = phaseSystem.Sequence,
            PriorityOrder = playerIds,
            CurrentPriorityIndex = 0,
            ActionStack = new ActionStack(),
            PhaseStartTime = DateTime.UtcNow
        };
        
        var updatedState = state with { PhaseState = phaseState };
        
        return Result<CombatState>.Success(updatedState);
    }
    
    /// <summary>
    /// Verifica se uma ação é permitida na fase atual.
    /// Se não houver sistema de fases ativo, sempre retorna true (retrocompatibilidade).
    /// </summary>
    /// <param name="state">Estado do combate</param>
    /// <param name="actionType">Tipo de ação a validar</param>
    /// <returns>True se a ação é permitida</returns>
    public static bool IsActionAllowedInCurrentPhase(this CombatState state, ActionType actionType)
    {
        // Se não há sistema de fases, permitir todas as ações (retrocompatibilidade)
        if (state.PhaseState == null)
        {
            return true;
        }
        
        var currentPhase = state.PhaseState.CurrentPhase;
        
        // Se não há detalhes da fase, permitir (fallback)
        if (!state.PhaseState.PhaseSequence.PhaseDetails.TryGetValue(currentPhase, out var phaseDefinition))
        {
            return true;
        }
        
        // Verificar se a ação está na lista de ações permitidas
        return phaseDefinition.AllowedActions.Contains(actionType);
    }
    
    /// <summary>
    /// Obtém o jogador que tem prioridade atual.
    /// Se não houver sistema de fases, retorna null.
    /// </summary>
    /// <param name="state">Estado do combate</param>
    /// <returns>ID do jogador com prioridade, ou null</returns>
    public static string? GetCurrentPriorityPlayer(this CombatState state)
    {
        if (state.PhaseState == null || state.PhaseState.PriorityOrder.Count == 0)
        {
            return null;
        }
        
        var index = state.PhaseState.CurrentPriorityIndex;
        if (index < 0 || index >= state.PhaseState.PriorityOrder.Count)
        {
            return null;
        }
        
        return state.PhaseState.PriorityOrder[index];
    }
    
    /// <summary>
    /// Verifica se o jogador especificado tem prioridade atual.
    /// Se não houver sistema de fases, sempre retorna true (retrocompatibilidade).
    /// </summary>
    /// <param name="state">Estado do combate</param>
    /// <param name="playerId">ID do jogador a verificar</param>
    /// <returns>True se o jogador tem prioridade</returns>
    public static bool HasPriority(this CombatState state, string playerId)
    {
        // Se não há sistema de fases, todos têm prioridade (retrocompatibilidade)
        if (state.PhaseState == null)
        {
            return true;
        }
        
        var currentPlayer = state.GetCurrentPriorityPlayer();
        return currentPlayer == playerId;
    }
    
    /// <summary>
    /// Verifica se a fase atual permite prioridade interativa.
    /// </summary>
    /// <param name="state">Estado do combate</param>
    /// <returns>True se a fase permite prioridade</returns>
    public static bool CurrentPhaseAllowsPriority(this CombatState state)
    {
        if (state.PhaseState == null)
        {
            return false;
        }
        
        var currentPhase = state.PhaseState.CurrentPhase;
        
        if (!state.PhaseState.PhaseSequence.PhaseDetails.TryGetValue(currentPhase, out var phaseDefinition))
        {
            return false;
        }
        
        return phaseDefinition.AllowPriority;
    }
    
    /// <summary>
    /// Verifica se a fase atual deve transicionar automaticamente.
    /// </summary>
    /// <param name="state">Estado do combate</param>
    /// <returns>True se a fase deve transicionar automaticamente</returns>
    public static bool CurrentPhaseShouldAutoTransition(this CombatState state)
    {
        if (state.PhaseState == null)
        {
            return false;
        }
        
        var currentPhase = state.PhaseState.CurrentPhase;
        
        if (!state.PhaseState.PhaseSequence.PhaseDetails.TryGetValue(currentPhase, out var phaseDefinition))
        {
            return false;
        }
        
        return phaseDefinition.AutoTransition;
    }
    
    /// <summary>
    /// Obtém informações sobre a fase atual para exibição.
    /// </summary>
    /// <param name="state">Estado do combate</param>
    /// <returns>Informações da fase, ou null se não houver sistema de fases</returns>
    public static PhaseInfo? GetCurrentPhaseInfo(this CombatState state)
    {
        if (state.PhaseState == null)
        {
            return null;
        }
        
        var currentPhase = state.PhaseState.CurrentPhase;
        
        if (!state.PhaseState.PhaseSequence.PhaseDetails.TryGetValue(currentPhase, out var phaseDefinition))
        {
            return null;
        }
        
        return new PhaseInfo
        {
            Phase = currentPhase,
            Name = phaseDefinition.Name,
            Description = phaseDefinition.Description,
            AllowedActions = phaseDefinition.AllowedActions,
            AllowsPriority = phaseDefinition.AllowPriority,
            AutoTransition = phaseDefinition.AutoTransition,
            CurrentPriorityPlayer = state.GetCurrentPriorityPlayer(),
            StackSize = state.PhaseState.ActionStack.Size
        };
    }
}

/// <summary>
/// Informações sobre a fase atual para exibição.
/// </summary>
public record PhaseInfo
{
    public Core.Combat.TurnPhase.TurnPhase Phase { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<ActionType> AllowedActions { get; init; } = new();
    public bool AllowsPriority { get; init; }
    public bool AutoTransition { get; init; }
    public string? CurrentPriorityPlayer { get; init; }
    public int StackSize { get; init; }
}
