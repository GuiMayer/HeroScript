using Core.Combat.Models;
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
    /// Inicia um combate com entradas reproduzíveis explícitas.
    /// </summary>
    Result<CombatState> StartCombat(
        string heroId,
        List<string> enemyIds,
        int initialEnergy,
        CombatStartOptions options);
    
    /// <summary>
    /// Inicia novo combate usando entidades do novo sistema Entity.
    /// </summary>
    /// <param name="hero">Entidade do herói</param>
    /// <param name="enemies">Lista de entidades inimigas</param>
    /// <returns>Result com o estado inicial do combate</returns>
    Result<CombatState> StartCombatWithEntities(Entity.Entity hero, List<Entity.Entity> enemies);

    /// <summary>
    /// Inicia um combate de entidades com entradas reproduzíveis explícitas.
    /// </summary>
    Result<CombatState> StartCombatWithEntities(
        Entity.Entity hero,
        List<Entity.Entity> enemies,
        CombatStartOptions options);
    
    /// <summary>
    /// Executa ação em combate.
    /// </summary>
    /// <param name="combatId">ID do combate</param>
    /// <param name="command">Comando actor-agnostic contendo ator, ação, alvo e custos</param>
    /// <returns>Result com o novo estado do combate</returns>
    Result<CombatState> ExecuteAction(Guid combatId, CombatActionCommand command);
    
    /// <summary>
    /// Obtém estado atual de combate.
    /// </summary>
    /// <param name="combatId">ID do combate</param>
    /// <returns>Result com o estado do combate</returns>
    Result<CombatState> GetCombatState(Guid combatId);

    /// <summary>
    /// Atualiza estado de combate ativo de forma controlada.
    /// Usado por coordenadores externos para anexar estados actor-agnostic como ativação.
    /// </summary>
    Result<CombatState> UpdateCombatState(Guid combatId, Func<CombatState, CombatState> update);
    
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
