namespace Core.Combat;

/// <summary>
/// Estado imutável de um combate.
/// Cada ação cria um novo CombatState.
/// </summary>
public record CombatState
{
    public Guid CombatId { get; init; } = Guid.NewGuid();
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public int CurrentTurn { get; init; } = 1;
    public CombatStatus Status { get; init; } = CombatStatus.ACTIVE;
    
    // Entidades
    public CombatEntity Hero { get; init; } = null!;
    public IReadOnlyList<CombatEntity> Enemies { get; init; } = Array.Empty<CombatEntity>();
    
    // Energia
    public EnergyPool Energy { get; init; } = null!;
    
    // Histórico
    public IReadOnlyList<CombatAction> ActionHistory { get; init; } = Array.Empty<CombatAction>();
    
    // Helpers
    public bool IsActive => Status == CombatStatus.ACTIVE;
    public bool AllEnemiesDead => Enemies.All(e => !e.IsAlive);
    public bool HeroIsDead => !Hero.IsAlive;
    
    /// <summary>
    /// Obtém entidade por ID.
    /// </summary>
    /// <param name="entityId">ID da entidade</param>
    /// <returns>Entidade encontrada ou null</returns>
    public CombatEntity? GetEntity(string entityId)
    {
        if (Hero.EntityId == entityId) return Hero;
        return Enemies.FirstOrDefault(e => e.EntityId == entityId);
    }
}
