namespace Core.Entity;

/// <summary>
/// Interface base para todos os componentes de entidade.
/// Componentes são blocos de funcionalidade que podem ser anexados a entidades.
/// </summary>
public interface IComponent
{
    /// <summary>
    /// ID único do componente
    /// </summary>
    string ComponentId { get; }
    
    /// <summary>
    /// Se o componente está ativo
    /// </summary>
    bool IsEnabled { get; }
    
    /// <summary>
    /// Inicializa o componente com a entidade proprietária
    /// </summary>
    void Initialize(Entity owner);
    
    /// <summary>
    /// Chamado quando o componente é anexado a uma entidade
    /// </summary>
    void OnAttached(Entity owner);
    
    /// <summary>
    /// Chamado quando o componente é removido de uma entidade
    /// </summary>
    void OnDetached(Entity owner);
}
