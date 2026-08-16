using System.Collections.Immutable;
using Core.Combat.Activation;
using Core.Combat.TurnPhase;
using Core.Determinism;

namespace Core.Combat.Models;

/// <summary>
/// Estado imutável de um combate.
/// Cada ação cria um novo CombatState.
/// </summary>
public record CombatState
{
    private ImmutableList<CombatEntity> _enemies = [];
    private ImmutableList<CombatAction> _actionHistory = [];
    private ImmutableList<string>? _turnOrder;

    public Guid CombatId { get; init; } = Guid.Empty;
    public Guid? RunId { get; init; }
    public string? RunNodeId { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UnixEpoch;
    public DeterministicContext Determinism { get; init; } =
        DeterministicContext.Create(0, "legacy-combat");
    public int CurrentTurn { get; init; } = 1;
    public CombatStatus Status { get; init; } = CombatStatus.ACTIVE;
    
    // Entidades (agora com recursos genéricos)
    public CombatEntity Hero { get; init; } = null!;
    public IReadOnlyList<CombatEntity> Enemies
    {
        get => _enemies;
        init => _enemies = value?.ToImmutableList() ?? [];
    }
    
    // Histórico
    public IReadOnlyList<CombatAction> ActionHistory
    {
        get => _actionHistory;
        init => _actionHistory = value?.ToImmutableList() ?? [];
    }
    
    // Ordem de turnos (opcional - se null, usa ordem padrão)
    public IReadOnlyList<string>? TurnOrder
    {
        get => _turnOrder;
        init => _turnOrder = value?.ToImmutableList();
    }

    /// <summary>
    /// Estado serializável de estratégias de turno (por exemplo, medidores ATB).
    /// Nunca fica armazenado dentro de uma calculadora singleton.
    /// </summary>
    public ImmutableDictionary<string, float> TurnOrderValues { get; init; } =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    
    // Sistema de fases (opcional - se null, usa sistema de turno simples sem fases)
    // Quando não-null, habilita sistema de fases TCG-style com prioridade e validação de ações por fase
    public PhaseState? PhaseState { get; init; }

    // Estado de ativação por entidade (opcional - Fase 3 run loop)
    public ActivationState? ActivationState { get; init; }
    
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
    /// Obtém todas as entidades participantes do combate.
    /// </summary>
    public IEnumerable<CombatEntity> GetAllEntities()
    {
        yield return Hero;
        foreach (var enemy in Enemies)
            yield return enemy;
    }

    /// <summary>
    /// Retorna novo estado substituindo a entidade pelo ID.
    /// </summary>
    public CombatState ReplaceEntity(CombatEntity entity)
    {
        if (Hero.EntityId == entity.EntityId)
            return this with { Hero = entity };

        return this with
        {
            Enemies = _enemies.Select(e => e.EntityId == entity.EntityId ? entity : e).ToImmutableList()
        };
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
    
}
