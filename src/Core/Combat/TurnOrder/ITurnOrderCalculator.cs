using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Interface para calculadoras de ordem de turnos
/// </summary>
public interface ITurnOrderCalculator
{
    /// <summary>
    /// Calcula a ordem de turnos para o próximo turno
    /// </summary>
    /// <param name="state">Estado atual do combate</param>
    /// <returns>Lista ordenada de IDs de entidades que devem agir</returns>
    Result<List<string>> CalculateTurnOrder(CombatState state);
    
    /// <summary>
    /// Inicializa a calculadora no início do combate
    /// </summary>
    /// <param name="state">Estado inicial do combate</param>
    Result Initialize(CombatState state);
    
    /// <summary>
    /// Atualiza o estado interno após uma ação
    /// </summary>
    /// <param name="state">Estado atual do combate</param>
    /// <param name="actorId">ID da entidade que agiu</param>
    Result UpdateAfterAction(CombatState state, string actorId);
    
    /// <summary>
    /// Estratégia utilizada por esta calculadora
    /// </summary>
    TurnStrategy Strategy { get; }
}
