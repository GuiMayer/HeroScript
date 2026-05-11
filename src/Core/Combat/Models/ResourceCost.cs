namespace Core.Combat.Models;

/// <summary>
/// Custo de recurso para uma ação.
/// </summary>
public record ResourceCost
{
    /// <summary>
    /// ID do recurso (ex: "energy", "mana", "health").
    /// </summary>
    public string ResourceId { get; init; } = string.Empty;
    
    /// <summary>
    /// Quantidade fixa do recurso.
    /// </summary>
    public float Amount { get; init; }
    
    /// <summary>
    /// Fórmula dinâmica para calcular o custo (opcional).
    /// Contexto disponível: actor_level, target_level, etc.
    /// </summary>
    public string? Formula { get; init; }
    
    /// <summary>
    /// Se true, a ação pode ser executada mesmo sem recurso suficiente,
    /// mas o recurso pode ficar negativo (se permitido pela definição).
    /// </summary>
    public bool AllowOverdraft { get; init; }
}
