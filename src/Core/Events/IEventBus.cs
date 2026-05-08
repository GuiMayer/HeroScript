namespace Core.Events;

/// <summary>
/// Interface para o EventBus - sistema pub/sub para comunicação desacoplada.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publica um evento para todos os subscribers registrados.
    /// </summary>
    void Publish<TEvent>(TEvent @event) where TEvent : IEvent;
    
    /// <summary>
    /// Registra um handler para receber eventos de um tipo específico.
    /// Retorna IDisposable para permitir unsubscribe.
    /// </summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent;
    
    /// <summary>
    /// Retorna histórico completo de eventos publicados.
    /// </summary>
    IReadOnlyList<IEvent> GetEventHistory();
    
    /// <summary>
    /// Retorna histórico filtrado por categoria.
    /// </summary>
    IReadOnlyList<IEvent> GetEventHistory(EventCategory category);
    
    /// <summary>
    /// Retorna histórico filtrado por severidade.
    /// </summary>
    IReadOnlyList<IEvent> GetEventHistory(EventSeverity severity);
    
    /// <summary>
    /// Limpa histórico de eventos (apenas para dev/testing).
    /// </summary>
    void ClearHistory();
}
