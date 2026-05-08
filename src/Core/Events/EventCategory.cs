namespace Core.Events;

/// <summary>
/// Categorias de eventos do sistema.
/// </summary>
public enum EventCategory
{
    /// <summary>
    /// Eventos de combate (ataque, dano, morte)
    /// </summary>
    COMBAT,
    
    /// <summary>
    /// Eventos do pipeline de dano (bucket processing)
    /// </summary>
    PIPELINE,
    
    /// <summary>
    /// Eventos de sistema (config load, resource reload)
    /// </summary>
    META,
    
    /// <summary>
    /// Eventos de configuração
    /// </summary>
    CONFIG,
    
    /// <summary>
    /// Eventos especiais do jogo (Reality Bend)
    /// </summary>
    REALITY_BEND
}
