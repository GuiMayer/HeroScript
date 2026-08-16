using System.Collections.Immutable;
using Core.Resources;

namespace Core.Combat.Models;

/// <summary>
/// Estado de recursos de uma entidade.
/// Imutável - cada mudança cria nova instância.
/// </summary>
public record EntityResourceState
{
    private ImmutableDictionary<string, ResourcePool> _resources =
        ImmutableDictionary<string, ResourcePool>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ID da entidade dona destes recursos.
    /// </summary>
    public string EntityId { get; init; } = string.Empty;
    
    /// <summary>
    /// Recursos da entidade (health, energy, mana, etc.)
    /// </summary>
    public IReadOnlyDictionary<string, ResourcePool> Resources
    {
        get => _resources;
        init => _resources = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, ResourcePool>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Obtém um recurso específico.
    /// </summary>
    public ResourcePool? GetResource(string resourceId)
    {
        return Resources.TryGetValue(resourceId, out var pool) ? pool : null;
    }
    
    /// <summary>
    /// Verifica se a entidade possui um recurso.
    /// </summary>
    public bool HasResource(string resourceId) => Resources.ContainsKey(resourceId);
    
    /// <summary>
    /// Atualiza um recurso específico.
    /// </summary>
    public EntityResourceState UpdateResource(string resourceId, ResourcePool newPool)
    {
        return this with { Resources = _resources.SetItem(resourceId, newPool) };
    }
    
    /// <summary>
    /// Atualiza múltiplos recursos de uma vez.
    /// </summary>
    public EntityResourceState UpdateResources(Dictionary<string, ResourcePool> updates)
    {
        var merged = _resources;
        foreach (var (key, value) in updates)
            merged = merged.SetItem(key, value);
        return this with { Resources = merged };
    }
    
    /// <summary>
    /// Obtém o primeiro recurso vital (categoria VITAL).
    /// Usado para determinar se a entidade está viva.
    /// </summary>
    public ResourcePool? GetVitalResource()
    {
        foreach (var pool in Resources.Values)
        {
            if (pool.Definition != null && 
                pool.Definition.Category == ResourceCategory.VITAL)
            {
                return pool;
            }
        }
        return null;
    }
}
