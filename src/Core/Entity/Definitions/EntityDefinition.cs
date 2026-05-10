namespace Core.Entity.Definitions;

/// <summary>
/// Definição de recursos para uma entidade
/// </summary>
public record ResourcesDefinition
{
    public Dictionary<string, ResourcePoolDefinition> Resources { get; init; } = new();
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
    public float Strength { get; init; } = 10;
    public float Dexterity { get; init; } = 10;
    public float Intelligence { get; init; } = 10;
    public float Constitution { get; init; } = 10;
    public float Wisdom { get; init; } = 10;
    public float Charisma { get; init; } = 10;
    public Dictionary<string, float> CustomStats { get; init; } = new();
}

/// <summary>
/// Definição de inventário para uma entidade
/// </summary>
public record InventoryDefinition
{
    public int MaxCapacity { get; init; } = -1;
    public List<string> StartingItems { get; init; } = new();
}

/// <summary>
/// Definição de AI para inimigos
/// </summary>
public record AIDefinition
{
    public string BehaviorTree { get; init; } = "balanced";
    public List<string> Actions { get; init; } = new();
    public float LowHealthThreshold { get; init; } = 0.5f;
    public float FleeHealthThreshold { get; init; } = 0.3f;
}

/// <summary>
/// Definição de Gambits para companions
/// </summary>
public record GambitDefinition
{
    public List<string> GambitIds { get; init; } = new();
}

/// <summary>
/// Definição completa de uma entidade configurável via JSON
/// </summary>
public record EntityDefinition
{
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
    public Dictionary<string, object> CustomData { get; init; } = new();
    
    /// <summary>
    /// ID da definição base (para herança delta)
    /// </summary>
    public string? BaseDefinitionId { get; init; }
}
