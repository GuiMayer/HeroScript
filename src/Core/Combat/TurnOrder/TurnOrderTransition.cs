using Core.Combat.Models;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Resultado puro de uma estratégia de turnos: o estado atualizado permanece
/// dentro do agregado e a ordem é apenas uma projeção desse estado.
/// </summary>
public sealed record TurnOrderTransition(
    CombatState State,
    IReadOnlyList<string> Order);
