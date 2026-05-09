namespace Core.Damage;

/// <summary>
/// Contexto imutável de cálculo de dano que flui através do pipeline.
/// Cada bucket processa o contexto e retorna uma nova versão transformada.
/// </summary>
public record DamageContext
{
    /// <summary>
    /// Dano base original (não muda durante o pipeline)
    /// </summary>
    public float BaseDamage { get; init; }
    
    /// <summary>
    /// Dano atual (modificado por cada bucket)
    /// </summary>
    public float CurrentDamage { get; init; }
    
    /// <summary>
    /// Tags da ação (ex: "physical", "spell", "can_crit")
    /// </summary>
    public HashSet<string> Tags { get; init; } = new();
    
    /// <summary>
    /// Modificadores numéricos (ex: "crit_chance" = 150.0)
    /// </summary>
    public Dictionary<string, float> Modifiers { get; init; } = new();
    
    /// <summary>
    /// Metadata adicional (ex: "crit_tier" = 2, "attacker_id" = "hero-1")
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    /// <summary>
    /// Lista de multiplicadores "more" aplicados sequencialmente.
    /// Cada multiplicador é aplicado separadamente (não somados).
    /// </summary>
    public List<float> MoreMultipliers { get; init; } = new();
    
    /// <summary>
    /// Cria novo contexto com dano atualizado
    /// </summary>
    public DamageContext WithDamage(float newDamage)
    {
        return this with { CurrentDamage = newDamage };
    }
    
    /// <summary>
    /// Cria novo contexto com modifier adicionado/atualizado
    /// </summary>
    public DamageContext WithModifier(string key, float value)
    {
        var newModifiers = new Dictionary<string, float>(Modifiers)
        {
            [key] = value
        };
        return this with { Modifiers = newModifiers };
    }
    
    /// <summary>
    /// Cria novo contexto com tag adicionada
    /// </summary>
    public DamageContext WithTag(string tag)
    {
        var newTags = new HashSet<string>(Tags) { tag };
        return this with { Tags = newTags };
    }
    
    /// <summary>
    /// Cria novo contexto com tag removida
    /// </summary>
    public DamageContext RemoveTag(string tag)
    {
        var newTags = new HashSet<string>(Tags);
        newTags.Remove(tag);
        return this with { Tags = newTags };
    }
    
    /// <summary>
    /// Cria novo contexto com metadata adicionada/atualizada
    /// </summary>
    public DamageContext WithMetadata(string key, object value)
    {
        var newMetadata = new Dictionary<string, object>(Metadata)
        {
            [key] = value
        };
        return this with { Metadata = newMetadata };
    }
    
    /// <summary>
    /// Cria novo contexto com multiplicador "more" adicionado à lista
    /// </summary>
    public DamageContext WithMoreMultiplier(float multiplier)
    {
        var newMultipliers = new List<float>(MoreMultipliers) { multiplier };
        return this with { MoreMultipliers = newMultipliers };
    }
}
