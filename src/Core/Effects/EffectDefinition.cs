using System.Collections.Immutable;
namespace Core.Effects;

/// <summary>
/// Reusable component that binds source-agnostic effects to a named gameplay
/// boundary. Owners such as statuses and relics decide which boundaries exist.
/// </summary>
public sealed record EffectTriggerDefinition
{
    private ImmutableArray<EffectDefinition> _effects = [];

    public string TriggerId { get; init; } = string.Empty;
    public string Boundary { get; init; } = string.Empty;
    public int Priority { get; init; }
    public IReadOnlyList<EffectDefinition> Effects
    {
        get => _effects;
        init => _effects = value?.ToImmutableArray() ?? [];
    }
}

/// <summary>
/// Definição de um efeito configurável via JSON.
/// Effect é a unidade fundamental de todas as ações em combate.
/// Carregada de JSON, permite criar efeitos customizados.
/// </summary>
public record EffectDefinition
{
    private ImmutableList<string>? _requiredTags;
    private ImmutableList<string>? _excludedTags;
    private ImmutableList<string> _tags = [];
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private ImmutableList<EffectDefinition>? _chainedEffects;

    /// <summary>
    /// ID único do efeito (gerado automaticamente se não fornecido)
    /// </summary>
    public string EffectId { get; init; } = string.Empty;

    /// <summary>Optional unique formula-safe alias for typed outputs within an action.</summary>
    public string? OutputId { get; init; }
    public EffectExecutionScope ExecutionScope { get; init; }
    public string? ExecutionGroupId { get; init; }
    public string? ChanceGroupId { get; init; }
    public EffectChildTiming ChildTiming { get; init; }
    public ImmutableArray<EffectRandomInputDefinition> RandomInputs { get; init; } = [];
    public ImmutableArray<EffectNumericParameterDefinition> Parameters { get; init; } = [];
    public ImmutableArray<StackPayloadBinding> PayloadBindings { get; init; } = [];
    public string? CondensationRecipeId { get; init; }
    public EffectContinuationDefinition? Continuation { get; init; }
    public Core.Entity.AttributeMutationDefinition? AttributeMutation { get; init; }
    
    /// <summary>
    /// Tipo do efeito
    /// </summary>
    public EffectType Type { get; init; }
    
    /// <summary>
    /// Alvo do efeito
    /// </summary>
    public EffectTarget Target { get; init; } = EffectTarget.TARGET;

    /// <summary>Policy for targets invalidated during this action; never permits invalid input.</summary>
    public EffectTargetLossDefinition TargetLoss { get; init; } = new();

    /// <summary>
    /// Resource used by resource-ranked automatic targeting. It is independent
    /// from TargetResource, which identifies the resource changed by the effect.
    /// </summary>
    public string? SelectionResourceId { get; init; }
    
    // ===== VALORES =====
    
    /// <summary>
    /// Valor flat (fixo)
    /// </summary>
    public float? FlatValue { get; init; }
    
    /// <summary>
    /// Fórmula dinâmica (usa MathEngine)
    /// Contexto disponível: source_*, target_*, stacks, etc.
    /// </summary>
    public string? FormulaValue { get; init; }
    
    // ===== RECURSOS (para DAMAGE, HEAL, MODIFY_RESOURCE) =====
    
    /// <summary>
    /// Recurso alvo. Obrigatório para qualquer efeito que altere um recurso;
    /// não existe recurso implícito associado ao tipo do efeito.
    /// </summary>
    public string? TargetResource { get; init; }

    /// <summary>
    /// Explicit operation for MODIFY_RESOURCE. DAMAGE and HEAL are authoring
    /// aliases for SUBTRACT and ADD; none of them selects a resource by name.
    /// </summary>
    public ResourceEffectOperation Operation { get; init; } = ResourceEffectOperation.ADD;

    /// <summary>
    /// Numeric field changed by a resource effect. Current remains the default;
    /// minimum and maximum use the same mutation reducer and journal semantics.
    /// </summary>
    public Resources.ResourceValueField ResourceField { get; init; } = Resources.ResourceValueField.Current;

    /// <summary>
    /// Calculation channel used to resolve the numeric value. The game mode
    /// selects the compatible pipeline; effect type does not select one.
    /// </summary>
    public string CalculationChannel { get; init; } = "effect_amount";

    /// <summary>
    /// Optional explicit pipeline. When omitted, exactly one compatible
    /// pipeline must be enabled by the game mode.
    /// </summary>
    public string? CalculationPipelineId { get; init; }
    
    // ===== STATUS (para APPLY_STATUS, REMOVE_STATUS) =====
    
    /// <summary>
    /// ID do status a aplicar/remover
    /// </summary>
    public string? StatusId { get; init; }
    
    /// <summary>
    /// Número de stacks do status
    /// </summary>
    public int? StatusStacks { get; init; }
    
    /// <summary>
    /// Duração do status (override do valor base)
    /// </summary>
    public int? StatusDuration { get; init; }
    public StatusEffects.StatusDispelDefinition Dispel { get; init; } = new();
    public string? ModifierId { get; init; }
    public int? ModifierStacks { get; init; }
    public int? ModifierDuration { get; init; }
    public Combat.Models.GameplayOwner? ModifierOwner { get; init; }
    public string? CardDefinitionId { get; init; }
    public string? CardZoneFlowId { get; init; }
    public int CardCount { get; init; } = 1;
    public ImmutableArray<Guid> CardInstanceIds { get; init; } = [];
    
    // ===== CONDIÇÕES =====
    
    /// <summary>
    /// Condição para executar o efeito (expressão booleana via MathEngine)
    /// Ex: "target.resources.health.current &lt; target.resources.health.maximum * 0.5"
    /// </summary>
    public string? Condition { get; init; }
    
    /// <summary>
    /// Tags requeridas para executar (AND)
    /// Ex: ["fire", "spell"] - só executa se ação tem ambas as tags
    /// </summary>
    public IReadOnlyList<string>? RequiredTags
    {
        get => _requiredTags;
        init => _requiredTags = value?.ToImmutableList();
    }
    
    /// <summary>
    /// Tags excluídas (NOT)
    /// Ex: ["physical"] - não executa se ação tem tag "physical"
    /// </summary>
    public IReadOnlyList<string>? ExcludedTags
    {
        get => _excludedTags;
        init => _excludedTags = value?.ToImmutableList();
    }
    
    // ===== PROBABILIDADE =====
    
    /// <summary>
    /// Chance de executar (0.0 a 1.0)
    /// 1.0 = sempre, 0.5 = 50%, 0.0 = nunca
    /// </summary>
    public float Chance { get; init; } = 1.0f;
    public EffectChanceScope ChanceScope { get; init; } = EffectChanceScope.PerEffect;
    
    // ===== REPETIÇÃO =====
    
    /// <summary>
    /// Número de vezes que o efeito é executado
    /// </summary>
    public int Repeat { get; init; } = 1;
    
    // ===== METADATA =====
    
    /// <summary>
    /// Tags para categorização e filtros
    /// Ex: ["physical", "attack", "fire"]
    /// </summary>
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Metadata adicional (livre)
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }
    
    // ===== EFEITOS ENCADEADOS =====
    
    /// <summary>
    /// Efeitos disparados após este (sempre executam)
    /// </summary>
    public IReadOnlyList<EffectDefinition>? ChainedEffects
    {
        get => _chainedEffects;
        init => _chainedEffects = value?.ToImmutableList();
    }
    
}
