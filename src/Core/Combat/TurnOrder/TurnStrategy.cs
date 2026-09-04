namespace Core.Combat.TurnOrder;

/// <summary>
/// Estratégias disponíveis para cálculo de ordem de turnos
/// </summary>
public enum TurnStrategy
{
    UNSPECIFIED,

    /// <summary>
    /// Ordem fixa: Hero sempre age primeiro, depois inimigos na ordem de criação
    /// </summary>
    FIXED,
    
    /// <summary>
    /// Baseado em velocidade: Entidades com maior velocidade agem primeiro
    /// Recalcula a cada turno
    /// </summary>
    SPEED_BASED,
    
    /// <summary>
    /// Sistema de iniciativa: Rola dados no início do combate
    /// Ordem permanece fixa durante todo o combate
    /// </summary>
    INITIATIVE,
    
    /// <summary>
    /// Active Time Battle: Cada entidade tem uma barra de tempo que preenche
    /// Quando cheia, a entidade pode agir
    /// </summary>
    ATB,
    
    /// <summary>
    /// Ordem condicional: Usa regras customizadas baseadas em condições
    /// Permite lógica complexa de ordenação
    /// </summary>
    CONDITIONAL
}
