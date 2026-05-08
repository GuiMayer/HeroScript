namespace Core.Combat;

/// <summary>
/// Representa uma entidade em combate (herói ou inimigo).
/// Imutável - cada mudança cria nova instância.
/// </summary>
public record CombatEntity
{
    public string EntityId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int CurrentHp { get; init; }
    public int MaxHp { get; init; }
    public bool IsAlive => CurrentHp > 0;
    public bool IsHero { get; init; }
    
    /// <summary>
    /// Aplica dano à entidade.
    /// </summary>
    /// <param name="damage">Quantidade de dano</param>
    /// <returns>Nova instância com HP reduzido</returns>
    public CombatEntity TakeDamage(int damage)
    {
        return this with { CurrentHp = System.Math.Max(0, CurrentHp - damage) };
    }
    
    /// <summary>
    /// Cura a entidade.
    /// </summary>
    /// <param name="amount">Quantidade de cura</param>
    /// <returns>Nova instância com HP aumentado</returns>
    public CombatEntity Heal(int amount)
    {
        return this with { CurrentHp = System.Math.Min(MaxHp, CurrentHp + amount) };
    }
}
