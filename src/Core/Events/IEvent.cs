namespace Core.Events;

/// <summary>
/// Interface base para todos os eventos do sistema.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Identificador único do evento.
    /// </summary>
    Guid EventId { get; }
    
    /// <summary>
    /// Timestamp de quando o evento foi criado.
    /// </summary>
    DateTime Timestamp { get; }
    
    /// <summary>
    /// Tipo do evento (nome da classe).
    /// </summary>
    string EventType { get; }
}
