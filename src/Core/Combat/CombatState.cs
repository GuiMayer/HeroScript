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
    
    // Entidades (agora com recursos genéricos)
    public CombatEntity Hero { get; init; } = null!;
    public IReadOnlyList<CombatEntity> Enemies { get; init; } = Array.Empty<CombatEntity>();
    
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
    
    /// <summary>
    /// Obtém recurso do herói.
    /// </summary>
    public Resources.ResourcePool? GetHeroResource(string resourceId) 
        => Hero.GetResource(resourceId);
    
    /// <summary>
    /// Obtém recurso de um inimigo.
    /// </summary>
    public Resources.ResourcePool? GetEnemyResource(string enemyId, string resourceId)
    {
        var enemy = Enemies.FirstOrDefault(e => e.EntityId == enemyId);
        return enemy?.GetResource(resourceId);
    }
    
    // Propriedade de conveniência para compatibilidade (delega para recurso "energy" do herói)
    public EnergyPool Energy => new EnergyPool
    {
        Current = (int)(GetHeroResource("energy")?.Current ?? 0),
        Maximum = (int)(GetHeroResource("energy")?.Maximum ?? 10)
    };
}
