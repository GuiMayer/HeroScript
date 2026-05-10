namespace Core.Entity;

/// <summary>
/// Classe base abstrata para componentes.
/// Fornece implementação padrão de IComponent.
/// </summary>
public abstract class ComponentBase : IComponent
{
    public string ComponentId { get; protected set; } = string.Empty;
    public bool IsEnabled { get; protected set; } = true;
    
    protected Entity? Owner { get; private set; }
    
    public virtual void Initialize(Entity owner)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ComponentId = $"{owner.EntityId}_{GetType().Name}";
    }
    
    public virtual void OnAttached(Entity owner)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }
    
    public virtual void OnDetached(Entity owner)
    {
        Owner = null;
    }
    
    /// <summary>
    /// Habilita o componente
    /// </summary>
    public virtual void Enable()
    {
        IsEnabled = true;
    }
    
    /// <summary>
    /// Desabilita o componente
    /// </summary>
    public virtual void Disable()
    {
        IsEnabled = false;
    }
}
