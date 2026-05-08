namespace Core.Events;

/// <summary>
/// Severidade de eventos do sistema.
/// </summary>
public enum EventSeverity
{
    /// <summary>
    /// Informação de debug detalhada
    /// </summary>
    DEBUG,
    
    /// <summary>
    /// Informação normal
    /// </summary>
    INFO,
    
    /// <summary>
    /// Aviso (algo inesperado mas não crítico)
    /// </summary>
    WARN,
    
    /// <summary>
    /// Anomalia (Reality Bend, comportamento especial)
    /// </summary>
    ANOMALY
}
