namespace API.Models;

/// <summary>
/// Request para calcular dano de uma ação
/// </summary>
public class CalculateDamageRequest
{
    /// <summary>
    /// ID da ação (ex: "fireball", "basic_attack")
    /// </summary>
    public string ActionId { get; set; } = string.Empty;
    
    /// <summary>
    /// Dano base da ação
    /// </summary>
    public float BaseDamage { get; set; }
    
    /// <summary>
    /// Tags da ação (ex: ["physical", "melee", "can_crit"])
    /// </summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>
    /// Stats do atacante
    /// </summary>
    public EntityStatsDto Attacker { get; set; } = new();
    
    /// <summary>
    /// Stats do alvo
    /// </summary>
    public EntityStatsDto Target { get; set; } = new();
}

/// <summary>
/// Stats de uma entidade para cálculo de dano
/// </summary>
public class EntityStatsDto
{
    /// <summary>
    /// ID da entidade
    /// </summary>
    public string EntityId { get; set; } = string.Empty;
    
    /// <summary>
    /// Armadura (para mitigação)
    /// </summary>
    public float Armor { get; set; }
    
    /// <summary>
    /// Chance de crítico (0-100+)
    /// </summary>
    public float CritChance { get; set; }
    
    /// <summary>
    /// Multiplicador de crítico (padrão 2.0)
    /// </summary>
    public float CritMultiplier { get; set; } = 2.0f;
    
    /// <summary>
    /// Modifiers adicionais (ex: "increased_damage_total": 50)
    /// </summary>
    public Dictionary<string, float> Modifiers { get; set; } = new();
}
