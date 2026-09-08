using System.Collections.Immutable;

namespace Core.Entity.Definitions;

/// <summary>
/// Definição de recursos para uma entidade
/// </summary>
public record ResourcesDefinition
{
    private ImmutableDictionary<string, ResourcePoolDefinition> _resources =
        ImmutableDictionary<string, ResourcePoolDefinition>.Empty.WithComparers(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ResourcePoolDefinition> Resources
    {
        get => _resources;
        init => _resources = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, ResourcePoolDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

/// <summary>
/// Definição de um pool de recurso
/// </summary>
public record ResourcePoolDefinition
{
    public float Current { get; init; }
    public float Max { get; init; }
}

/// <summary>
/// Definição de stats para uma entidade
/// </summary>
public record StatsDefinition
{
    private ImmutableDictionary<string, float> _customStats =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);

    public float Strength { get; init; } = 10;
    public float Dexterity { get; init; } = 10;
    public float Intelligence { get; init; } = 10;
    public float Constitution { get; init; } = 10;
    public float Wisdom { get; init; } = 10;
    public float Charisma { get; init; } = 10;
    public IReadOnlyDictionary<string, float> CustomStats
    {
        get => _customStats;
        init => _customStats = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

/// <summary>
/// Definição de inventário para uma entidade
/// </summary>
public record InventoryDefinition
{
    private ImmutableArray<string> _startingItems = ImmutableArray<string>.Empty;

    public int MaxCapacity { get; init; } = -1;
    public IReadOnlyList<string> StartingItems
    {
        get => _startingItems;
        init => _startingItems = value?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }
}

/// <summary>
/// Definição de AI para inimigos
/// </summary>
public record AIDefinition
{
    private ImmutableArray<string> _actions = ImmutableArray<string>.Empty;

    public string BehaviorTree { get; init; } = string.Empty;
    public string DecisionResourceId { get; init; } = string.Empty;
    public IReadOnlyList<string> Actions
    {
        get => _actions;
        init => _actions = value?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }
    public float LowResourceThreshold { get; init; } = 0.5f;
    public float FleeResourceThreshold { get; init; } = 0.3f;
}

/// <summary>
/// Definição de Gambits para companions
/// </summary>
public record GambitDefinition
{
    private ImmutableArray<string> _gambitIds = ImmutableArray<string>.Empty;

    public IReadOnlyList<string> GambitIds
    {
        get => _gambitIds;
        init => _gambitIds = value?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }
}

/// <summary>
/// Definição completa de uma entidade configurável via JSON
/// </summary>
public record EntityDefinition
{
    private ImmutableDictionary<string, object> _customData =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>
    /// ID único da definição
    /// </summary>
    public string DefinitionId { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo da entidade
    /// </summary>
    public EntityType Type { get; init; }
    
    /// <summary>
    /// Nome para exibição
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;
    
    /// <summary>
    /// Descrição da entidade
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Definição de recursos (HP, energia, etc.)
    /// </summary>
    public ResourcesDefinition? Resources { get; init; }
    
    /// <summary>
    /// Definição de stats (STR, DEX, etc.)
    /// </summary>
    public StatsDefinition? Stats { get; init; }
    
    /// <summary>
    /// Definição de inventário
    /// </summary>
    public InventoryDefinition? Inventory { get; init; }
    
    /// <summary>
    /// Definição de AI (para inimigos)
    /// </summary>
    public AIDefinition? AI { get; init; }
    
    /// <summary>
    /// Definição de Gambits (para companions)
    /// </summary>
    public GambitDefinition? Gambits { get; init; }
    
    /// <summary>
    /// Caminho do ícone
    /// </summary>
    public string IconPath { get; init; } = string.Empty;
    
    /// <summary>
    /// Caminho do sprite
    /// </summary>
    public string SpritePath { get; init; } = string.Empty;
    
    /// <summary>
    /// Dados customizados adicionais
    /// </summary>
    public IReadOnlyDictionary<string, object> CustomData
    {
        get => _customData;
        init => _customData = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
    
}
