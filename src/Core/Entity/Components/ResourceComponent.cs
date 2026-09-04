using Core.Resources;

namespace Core.Entity.Components;

/// <summary>
/// Componente que gerencia recursos da entidade (HP, energia, mana, etc.)
/// Encapsula o conjunto imutável e genérico de recursos do dono.
/// </summary>
public class ResourceComponent : ComponentBase
{
    /// <summary>
    /// Estado de recursos da entidade
    /// </summary>
    public ResourceSet ResourceState { get; private set; }
    
    public ResourceComponent(ResourceSet resourceState)
    {
        ResourceState = resourceState ?? throw new ArgumentNullException(nameof(resourceState));
    }
    
    /// <summary>
    /// Obtém um recurso específico
    /// </summary>
    public ResourcePool? GetResource(string resourceId)
    {
        return ResourceState.Get(resourceId);
    }
    
    public ResourceComponent WithState(ResourceSet resourceState)
    {
        ArgumentNullException.ThrowIfNull(resourceState);
        if (!string.Equals(ResourceState.OwnerId, resourceState.OwnerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Resource state owner cannot change");
        return new ResourceComponent(resourceState)
        {
            ComponentId = this.ComponentId,
            IsEnabled = this.IsEnabled
        };
    }
    
    /// <summary>
    /// Verifica se nenhuma política configurada de recurso derrotou a entidade.
    /// Nome e categoria do recurso não possuem semântica implícita.
    /// </summary>
    public bool IsAlive() => !ResourceThresholdEvaluator.IsOwnerDefeated(
        ResourceState.Resources.Values);
}
