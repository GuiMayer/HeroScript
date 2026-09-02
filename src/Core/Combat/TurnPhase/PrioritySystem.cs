using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Implementação do sistema de prioridade para controle de ações em fases interativas.
/// Gerencia a ordem de passagem de prioridade entre jogadores.
/// </summary>
public class PrioritySystem : IPrioritySystem
{
    private readonly ILogger _logger;
    
    public PrioritySystem(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public Result<string> GetPriorityPlayer(CombatState state)
    {
        if (state.PhaseState == null)
        {
            return Result<string>.Failure("Combat does not have phase system enabled");
        }
        
        if (string.IsNullOrEmpty(state.PhaseState.ActivePlayerId))
        {
            return Result<string>.Failure("No active player set in phase state");
        }
        
        return Result<string>.Success(state.PhaseState.ActivePlayerId);
    }
    
    public Result<PhaseState> PassPriority(PhaseState phaseState, string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            return Result<PhaseState>.Failure("Player ID cannot be empty");
        }
        
        // Atualizar dicionário de prioridade passada
        var updatedPassed = new Dictionary<string, bool>(phaseState.PlayerPassedPriority)
        {
            [playerId] = true
        };
        
        _logger.LogDebug($"Player {playerId} passed priority in phase {phaseState.CurrentPhaseId}");
        
        return Result<PhaseState>.Success(phaseState with
        {
            PlayerPassedPriority = updatedPassed
        });
    }
    
    public bool AllPlayersPassedPriority(PhaseState phaseState)
    {
        // Se não há jogadores registrados, considera que todos passaram
        if (phaseState.PlayerPassedPriority.Count == 0)
        {
            return true;
        }
        
        // Verifica se todos os jogadores passaram prioridade
        return phaseState.PlayerPassedPriority.Values.All(passed => passed);
    }
    
    public Result<PhaseState> ResetPriority(PhaseState phaseState, string activePlayerId)
    {
        if (string.IsNullOrEmpty(activePlayerId))
        {
            return Result<PhaseState>.Failure("Active player ID cannot be empty");
        }
        
        // Criar novo dicionário com todos os jogadores marcados como não tendo passado prioridade
        var resetPassed = phaseState.PlayerPassedPriority.Keys
            .ToDictionary(playerId => playerId, _ => false);
        
        _logger.LogDebug($"Priority reset for phase {phaseState.CurrentPhaseId}, active player: {activePlayerId}");
        
        return Result<PhaseState>.Success(phaseState with
        {
            ActivePlayerId = activePlayerId,
            PlayerPassedPriority = resetPassed,
            CanTransition = false
        });
    }
    
    public List<string> GetPlayerOrder(CombatState state)
    {
        var playerOrder = new List<string>();
        
        // Adicionar hero primeiro
        playerOrder.Add(state.Hero.EntityId);
        
        // Adicionar inimigos
        // Em single-player, inimigos geralmente não passam prioridade interativamente
        // mas a arquitetura suporta para futuro multiplayer
        playerOrder.AddRange(state.Enemies.Select(e => e.EntityId));
        
        // Se houver TurnOrder definido, usar essa ordem
        if (state.TurnOrder != null && state.TurnOrder.Count > 0)
        {
            // Reordenar baseado em TurnOrder
            var orderedPlayers = state.TurnOrder
                .Where(id => playerOrder.Contains(id))
                .ToList();
            
            // Adicionar jogadores que não estão em TurnOrder (caso existam)
            orderedPlayers.AddRange(playerOrder.Where(id => !orderedPlayers.Contains(id)));
            
            return orderedPlayers;
        }
        
        return playerOrder;
    }
}
