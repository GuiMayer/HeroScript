namespace Core.Combat.Models;

/// <summary>
/// Representa uma entidade em combate (herói ou inimigo).
/// Imutável - cada mudança cria nova instância.
/// </summary>
public record CombatEntity
{
    public string EntityId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsHero { get; init; }
    
    /// <summary>
    /// Estado de recursos da entidade (health, energy, mana, etc.)
    /// </summary>
    public Resources.ResourceSet ResourceState { get; init; } = null!;
    
    /// <summary>
    /// Verifica se nenhuma política de recurso declarou o dono derrotado.
    /// </summary>
    public bool IsAlive
    {
        get
        {
            return !Resources.ResourceThresholdEvaluator.IsOwnerDefeated(
                ResourceState.Resources.Values);
        }
    }
    
    /// <summary>
    /// Obtém um recurso específico.
    /// </summary>
    public Resources.ResourcePool? GetResource(string resourceId) 
        => ResourceState.Get(resourceId);
    
    /// <summary>
    /// Atualiza um recurso específico.
    /// </summary>
    public CombatEntity UpdateResource(string resourceId, Resources.ResourcePool newPool)
    {
        return this with 
        { 
            ResourceState = ResourceState.WithResource(resourceId, newPool)
        };
    }
    
    /// <summary>
    /// Atualiza múltiplos recursos de uma vez.
    /// </summary>
    public CombatEntity UpdateResources(Dictionary<string, Resources.ResourcePool> updates)
    {
        return this with 
        { 
            ResourceState = ResourceState.WithResources(updates)
        };
    }
    
    /// <summary>
    /// Reduz um recurso explicitamente selecionado. O resultado da redução
    /// (inclusive derrota) pertence às políticas da definição do recurso.
    /// </summary>
    public CombatEntity ReduceResource(string resourceId, float amount)
    {
        var resource = GetResource(resourceId);
        if (resource == null) return this;
        return UpdateResource(resourceId, resource.Set(resource.Current - amount));
    }
    
    /// <summary>
    /// Aumenta um recurso explicitamente selecionado.
    /// </summary>
    public CombatEntity IncreaseResource(string resourceId, float amount)
    {
        var resource = GetResource(resourceId);
        if (resource == null) return this;
        return UpdateResource(resourceId, resource.Gain(amount));
    }
    
    /// <summary>
    /// Obtém armadura da entidade para cálculo de mitigação.
    /// </summary>
    public float GetArmor()
    {
        var armor = GetResource("armor");
        return armor?.Current ?? 0f;
    }
    
    /// <summary>
    /// Obtém chance de crítico da entidade (pode ultrapassar 100% para multi-tier).
    /// </summary>
    public float GetCritChance()
    {
        var critChance = GetResource("crit_chance");
        return critChance?.Current ?? 0f;
    }
    
    /// <summary>
    /// Obtém multiplicador de crítico da entidade (padrão 2.0, Felídeo 3.0).
    /// </summary>
    public float GetCritMultiplier()
    {
        var critMult = GetResource("crit_multiplier");
        return critMult?.Current ?? 2.0f;
    }
    
}
