using Core.Entity.Controllers;

namespace Core.Entity;

/// <summary>
/// Container genérico de entidade component-driven.
/// Entidades são compostas por componentes que definem seu comportamento.
/// Imutável - cada mudança cria nova instância.
/// </summary>
public record Entity
{
    /// <summary>
    /// ID único da entidade
    /// </summary>
    public string EntityId { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo da entidade (PLAYER, COMPANION, ENEMY, NPC)
    /// </summary>
    public EntityType Type { get; init; }
    
    /// <summary>
    /// ID da definição que criou esta entidade (referência ao JSON)
    /// </summary>
    public string DefinitionId { get; init; } = string.Empty;
    
    /// <summary>
    /// Nome para exibição
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;
    
    /// <summary>
    /// Componentes anexados à entidade
    /// </summary>
    public IReadOnlyDictionary<Type, IComponent> Components { get; init; } = 
        new Dictionary<Type, IComponent>();
    
    /// <summary>
    /// Controller que controla as ações da entidade
    /// </summary>
    public IEntityController? Controller { get; init; }
    
    /// <summary>
    /// Obtém um componente específico por tipo
    /// </summary>
    public T? GetComponent<T>() where T : class, IComponent
    {
        if (Components.TryGetValue(typeof(T), out var component))
        {
            return component as T;
        }
        return null;
    }
    
    /// <summary>
    /// Verifica se a entidade possui um componente específico
    /// </summary>
    public bool HasComponent<T>() where T : IComponent
    {
        return Components.ContainsKey(typeof(T));
    }
    
    /// <summary>
    /// Adiciona ou substitui um componente
    /// </summary>
    public Entity AddComponent<T>(T component) where T : IComponent
    {
        if (component == null)
            throw new ArgumentNullException(nameof(component));
        
        var newComponents = new Dictionary<Type, IComponent>(Components)
        {
            [typeof(T)] = component
        };
        
        // Inicializar componente com a nova entidade
        var newEntity = this with { Components = newComponents };
        component.OnAttached(newEntity);
        
        return newEntity;
    }
    
    /// <summary>
    /// Remove um componente
    /// </summary>
    public Entity RemoveComponent<T>() where T : IComponent
    {
        if (!Components.ContainsKey(typeof(T)))
            return this;
        
        var component = Components[typeof(T)];
        var newComponents = new Dictionary<Type, IComponent>(Components);
        newComponents.Remove(typeof(T));
        
        var newEntity = this with { Components = newComponents };
        component.OnDetached(this);
        
        return newEntity;
    }
    
    /// <summary>
    /// Atualiza um componente existente
    /// </summary>
    public Entity UpdateComponent<T>(T component) where T : IComponent
    {
        if (component == null)
            throw new ArgumentNullException(nameof(component));
        
        if (!Components.ContainsKey(typeof(T)))
            throw new InvalidOperationException($"Component {typeof(T).Name} not found");
        
        return AddComponent(component);
    }
}
