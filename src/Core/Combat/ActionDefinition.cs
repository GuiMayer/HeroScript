namespace Core.Combat;

/// <summary>
/// Definição de uma ação configurável.
/// Carregada de JSON, permite criar ações customizadas.
/// </summary>
public record ActionDefinition
{
    /// <summary>
    /// ID único da ação (ex: "basic_attack", "fireball", "heal").
    /// </summary>
    public string ActionId { get; init; } = string.Empty;
    
    /// <summary>
    /// Nome de exibição da ação.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;
    
    /// <summary>
    /// Descrição da ação.
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo da ação.
    /// </summary>
    public ActionType ActionType { get; init; }
    
    /// <summary>
    /// Custos de recursos para executar a ação.
    /// </summary>
    public ActionCosts Costs { get; init; } = new();
    
    /// <summary>
    /// Dano base da ação (se aplicável).
    /// </summary>
    public float? BaseDamage { get; init; }
    
    /// <summary>
    /// Fórmula de dano dinâmica (opcional).
    /// Contexto disponível: actor_attack, target_defense, etc.
    /// </summary>
    public string? DamageFormula { get; init; }
    
    /// <summary>
    /// Cura base da ação (se aplicável).
    /// </summary>
    public float? BaseHealing { get; init; }
    
    /// <summary>
    /// Fórmula de cura dinâmica (opcional).
    /// </summary>
    public string? HealingFormula { get; init; }
    
    /// <summary>
    /// Se a ação requer um alvo.
    /// </summary>
    public bool RequiresTarget { get; init; } = true;
    
    /// <summary>
    /// Se a ação pode ter múltiplos alvos.
    /// </summary>
    public bool MultiTarget { get; init; }
    
    /// <summary>
    /// Cooldown em turnos (0 = sem cooldown).
    /// </summary>
    public int Cooldown { get; init; }
    
    /// <summary>
    /// Tags para categorização.
    /// </summary>
    public List<string> Tags { get; init; } = new();
}
