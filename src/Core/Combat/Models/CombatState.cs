using System.Collections.Immutable;
using Core.Combat.Activation;
using Core.Combat.TurnPhase;
using Core.Determinism;
using Core.StatusEffects;

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
    public CombatRelationshipPolicy Relationships { get; init; } = new();
    public ImmutableHashSet<string> CompletedLifecycleBoundaries { get; init; } = ImmutableHashSet<string>.Empty;
    public ImmutableArray<CombatSide> Sides { get; init; } = [];

    // The two participant slots supply sides when a scenario does not override them.
    // IsHero is display metadata and is deliberately not consulted here.
    public string GetSideId(CombatEntity entity) => !string.IsNullOrWhiteSpace(entity.SideId)
        ? entity.SideId : entity.EntityId == Hero.EntityId ? "player" : "opposition";

    public SideRelationship Relationship(CombatEntity from, CombatEntity to) =>
        Relationships.Resolve(GetSideId(from), GetSideId(to));

    public ControllerKind ControllerOf(CombatEntity entity) =>
        Sides.FirstOrDefault(side => string.Equals(side.SideId, GetSideId(entity), StringComparison.Ordinal))?.Controller
        ?? (entity.EntityId == Hero.EntityId ? ControllerKind.Player : ControllerKind.AI);
    
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
    
    // Fase materializada pelo único fluxo canônico. Ausente apenas antes da
    // inicialização do encontro; não existe fallback permissivo sem fases.
    public PhaseState? PhaseState { get; init; }
    public CombatBoardState Board { get; init; } = new();

    // Única autoridade para ator ativo e progresso da ativação.
    public ActivationState? ActivationState { get; init; }

    /// <summary>
    /// Status ativos pertencem exclusivamente ao snapshot do combate.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>> StatusEffects { get; init; } =
        ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>>.Empty.WithComparers(StringComparer.Ordinal);
    
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
