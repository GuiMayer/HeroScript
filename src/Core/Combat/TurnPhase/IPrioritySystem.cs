using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Sistema de prioridade para controle de ações em fases interativas.
/// Gerencia quem pode agir e quando, permitindo respostas e interações entre jogadores.
/// Preparado para multiplayer, mas funciona com single-player (hero vs enemies).
/// </summary>
public interface IPrioritySystem
{
    /// <summary>
    /// Obtém o jogador que atualmente tem prioridade para agir.
    /// </summary>
    /// <param name="state">Estado atual do combate</param>
    /// <returns>ID do jogador com prioridade, ou erro se não puder determinar</returns>
    Result<string> GetPriorityPlayer(CombatState state);
    
    /// <summary>
    /// Registra que um jogador passou a prioridade.
    /// Atualiza o PhaseState para refletir a mudança.
    /// </summary>
    /// <param name="phaseState">Estado atual da fase</param>
    /// <param name="playerId">ID do jogador que está passando prioridade</param>
    /// <returns>Novo PhaseState com prioridade atualizada</returns>
    Result<PhaseState> PassPriority(PhaseState phaseState, string playerId);
    
    /// <summary>
    /// Verifica se todos os jogadores passaram prioridade na fase atual.
    /// Quando true, a fase pode avançar ou a stack pode resolver.
    /// </summary>
    /// <param name="phaseState">Estado atual da fase</param>
    /// <returns>True se todos passaram prioridade</returns>
    bool AllPlayersPassedPriority(PhaseState phaseState);
    
    /// <summary>
    /// Reseta o estado de prioridade para uma nova fase ou após resolução de ação.
    /// Define o jogador ativo como tendo prioridade primeiro.
    /// </summary>
    /// <param name="phaseState">Estado atual da fase</param>
    /// <param name="activePlayerId">ID do jogador que deve receber prioridade</param>
    /// <returns>Novo PhaseState com prioridade resetada</returns>
    Result<PhaseState> ResetPriority(PhaseState phaseState, string activePlayerId);
    
    /// <summary>
    /// Obtém a ordem de jogadores para passagem de prioridade.
    /// Em single-player: [hero, enemy1, enemy2, ...]
    /// Em multiplayer: ordem baseada em TurnOrder ou configuração
    /// </summary>
    /// <param name="state">Estado atual do combate</param>
    /// <returns>Lista ordenada de IDs de jogadores</returns>
    List<string> GetPlayerOrder(CombatState state);
}
