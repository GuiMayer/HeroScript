using Core.Common;

namespace Core.Combat;

/// <summary>
/// Interface para sistema de combate.
/// Gerencia estado de combates ativos e execução de ações.
/// </summary>
public interface ICombatSystem
{
    /// <summary>
    /// Inicia novo combate.
    /// </summary>
    /// <param name="heroId">ID do herói</param>
    /// <param name="enemyIds">Lista de IDs dos inimigos</param>
    /// <param name="initialEnergy">Energia inicial (padrão: 3)</param>
    /// <returns>Result com o estado inicial do combate</returns>
    Result<CombatState> StartCombat(string heroId, List<string> enemyIds, int initialEnergy = 3);
    
    /// <summary>
    /// Executa ação em combate.
    /// </summary>
    /// <param name="combatId">ID do combate</param>
    /// <param name="actionType">Tipo de ação</param>
    /// <param name="powerId">ID do poder (opcional, necessário para POWER)</param>
    /// <param name="targetId">ID do alvo (opcional, necessário para BASIC_ATTACK e POWER)</param>
    /// <returns>Result com o novo estado do combate</returns>
    Result<CombatState> ExecuteAction(Guid combatId, ActionType actionType, string? powerId = null, string? targetId = null);
    
    /// <summary>
    /// Obtém estado atual de combate.
    /// </summary>
    /// <param name="combatId">ID do combate</param>
    /// <returns>Result com o estado do combate</returns>
    Result<CombatState> GetCombatState(Guid combatId);
    
    /// <summary>
    /// Finaliza combate.
    /// </summary>
    /// <param name="combatId">ID do combate</param>
    /// <returns>Result com o resultado final do combate</returns>
    Result<CombatResult> EndCombat(Guid combatId);
    
    /// <summary>
    /// Obtém histórico de ações de um combate.
    /// </summary>
    /// <param name="combatId">ID do combate</param>
    /// <returns>Result com a lista de ações</returns>
    Result<IReadOnlyList<CombatAction>> GetActionHistory(Guid combatId);
    
    /// <summary>
    /// Verifica se combate existe e está ativo.
    /// </summary>
    /// <param name="combatId">ID do combate</param>
    /// <returns>True se o combate existe</returns>
    bool CombatExists(Guid combatId);
    
    /// <summary>
    /// Limpa combates inativos (dev/testing).
    /// </summary>
    void ClearInactiveCombats();
}
