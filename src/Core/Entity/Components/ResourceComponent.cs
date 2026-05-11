using Core.Combat.Models;
using Core.Resources;

namespace Core.Entity.Components;

/// <summary>
/// Componente que gerencia recursos da entidade (HP, energia, mana, etc.)
/// Encapsula EntityResourceState existente para compatibilidade.
/// </summary>
public class ResourceComponent : ComponentBase
{
    /// <summary>
    /// Estado de recursos da entidade
    /// </summary>
    public EntityResourceState ResourceState { get; private set; }
    
    public ResourceComponent(EntityResourceState resourceState)
    {
        ResourceState = resourceState ?? throw new ArgumentNullException(nameof(resourceState));
    }
    
    /// <summary>
    /// Obtém um recurso específico
    /// </summary>
    public ResourcePool? GetResource(string resourceId)
    {
        return ResourceState.GetResource(resourceId);
    }
    
    /// <summary>
    /// Atualiza um recurso específico
    /// </summary>
    public ResourceComponent UpdateResource(string resourceId, ResourcePool newPool)
    {
        var newState = ResourceState.UpdateResource(resourceId, newPool);
        return new ResourceComponent(newState)
        {
            ComponentId = this.ComponentId,
            IsEnabled = this.IsEnabled
        };
    }
    
    /// <summary>
    /// Atualiza múltiplos recursos de uma vez
    /// </summary>
    public ResourceComponent UpdateResources(Dictionary<string, ResourcePool> updates)
    {
        var newState = ResourceState.UpdateResources(updates);
        return new ResourceComponent(newState)
        {
            ComponentId = this.ComponentId,
            IsEnabled = this.IsEnabled
        };
    }
    
    /// <summary>
    /// Obtém o recurso vital (primeiro recurso VITAL)
    /// </summary>
    public ResourcePool? GetVitalResource()
    {
        return ResourceState.GetVitalResource();
    }
    
    /// <summary>
    /// Verifica se a entidade está viva (recurso vital > 0)
    /// </summary>
    public bool IsAlive()
    {
        var vital = GetVitalResource();
        return vital != null && vital.Current > 0;
    }
}
