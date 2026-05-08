namespace Core.Combat;

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
    public EntityResourceState ResourceState { get; init; } = null!;
    
    /// <summary>
    /// Verifica se a entidade está viva.
    /// Baseado no primeiro recurso vital (categoria VITAL).
    /// </summary>
    public bool IsAlive
    {
        get
        {
            var vital = ResourceState.GetVitalResource();
            if (vital == null) return false;
            return vital.Current > 0;
        }
    }
    
    /// <summary>
    /// Obtém um recurso específico.
    /// </summary>
    public Resources.ResourcePool? GetResource(string resourceId) 
        => ResourceState.GetResource(resourceId);
    
    /// <summary>
    /// Atualiza um recurso específico.
    /// </summary>
    public CombatEntity UpdateResource(string resourceId, Resources.ResourcePool newPool)
    {
        return this with 
        { 
            ResourceState = ResourceState.UpdateResource(resourceId, newPool) 
        };
    }
    
    /// <summary>
    /// Atualiza múltiplos recursos de uma vez.
    /// </summary>
    public CombatEntity UpdateResources(Dictionary<string, Resources.ResourcePool> updates)
    {
        return this with 
        { 
            ResourceState = ResourceState.UpdateResources(updates) 
        };
    }
    
    /// <summary>
    /// Aplica dano à entidade (conveniência para recurso "health").
    /// </summary>
    /// <param name="damage">Quantidade de dano</param>
    /// <returns>Nova instância com HP reduzido</returns>
    public CombatEntity TakeDamage(float damage)
    {
        var health = GetResource("health");
        if (health == null) return this;
        
        // Calcula novo valor (pode ficar negativo ou abaixo do mínimo)
        var newValue = health.Current - damage;
        
        // Usa Set() que respeita os limites da definição
        var newHealth = health.Set(newValue);
        return UpdateResource("health", newHealth);
    }
    
    /// <summary>
    /// Cura a entidade (conveniência para recurso "health").
    /// </summary>
    /// <param name="amount">Quantidade de cura</param>
    /// <returns>Nova instância com HP aumentado</returns>
    public CombatEntity Heal(float amount)
    {
        var health = GetResource("health");
        if (health == null) return this;
        
        var newHealth = health.Gain(amount);
        return UpdateResource("health", newHealth);
    }
    
    // Propriedades de conveniência para compatibilidade (delegam para recurso "health")
    public int CurrentHp => (int)(GetResource("health")?.Current ?? 0);
    public int MaxHp => (int)(GetResource("health")?.Maximum ?? 100);
}
