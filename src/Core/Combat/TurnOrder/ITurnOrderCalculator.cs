using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Interface para calculadoras de ordem de turnos
/// </summary>
public interface ITurnOrderCalculator
{
    /// <summary>
    /// Inicializa o estado serializável da estratégia. Implementações novas não
    /// devem armazenar dados mutáveis na instância da calculadora.
    /// </summary>
    Result<CombatState> InitializeState(CombatState state)
    {
        var result = Initialize(state);
        return result.IsSuccess
            ? Result<CombatState>.Success(state)
            : Result<CombatState>.Failure(result.Error);
    }

    /// <summary>
    /// Calcula a ordem e devolve qualquer alteração de estado necessária.
    /// </summary>
    Result<TurnOrderTransition> Calculate(CombatState state)
    {
        var result = CalculateTurnOrder(state);
        return result.IsSuccess
            ? Result<TurnOrderTransition>.Success(new TurnOrderTransition(state, result.Value))
            : Result<TurnOrderTransition>.Failure(result.Error);
    }

    /// <summary>
    /// Atualiza o estado da estratégia após uma ação.
    /// </summary>
    Result<CombatState> UpdateStateAfterAction(CombatState state, string actorId)
    {
        var result = UpdateAfterAction(state, actorId);
        return result.IsSuccess
            ? Result<CombatState>.Success(state)
            : Result<CombatState>.Failure(result.Error);
    }

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
    /// API legada. Novas implementações devem usar UpdateStateAfterAction e
    /// manter o estado dentro de CombatState.
    /// </summary>
    /// <param name="state">Estado atual do combate</param>
    /// <param name="actorId">ID da entidade que agiu</param>
    Result UpdateAfterAction(CombatState state, string actorId);
    
    /// <summary>
    /// Estratégia utilizada por esta calculadora
    /// </summary>
    TurnStrategy Strategy { get; }
}
