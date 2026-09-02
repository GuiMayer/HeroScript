using System.Collections.Immutable;
namespace Core.Effects;

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
    private ImmutableList<EffectDefinition>? _conditionalEffects;

    /// <summary>
    /// ID único do efeito (gerado automaticamente se não fornecido)
    /// </summary>
    public string EffectId { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo do efeito
    /// </summary>
    public EffectType Type { get; init; }
    
    /// <summary>
    /// Alvo do efeito
    /// </summary>
    public EffectTarget Target { get; init; } = EffectTarget.TARGET;
    
    /// <summary>
    /// Timing de execução
    /// </summary>
    public EffectTiming Timing { get; init; } = EffectTiming.IMMEDIATE;
    
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
    
    /// <summary>
    /// Se o valor é percentual (ex: 50% = 0.5)
    /// </summary>
    public bool IsPercentage { get; init; }
    
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
    
    // ===== MODIFICADORES (para MODIFY_*) =====
    
    /// <summary>
    /// Chave do modificador no damage pipeline
    /// Ex: "increased_damage_total", "crit_chance"
    /// </summary>
    public string? ModifierKey { get; init; }
    
    /// <summary>
    /// Valor do modificador (flat)
    /// </summary>
    public float? ModifierValue { get; init; }
    
    /// <summary>
    /// Fórmula do modificador (dinâmica)
    /// </summary>
    public string? ModifierFormula { get; init; }
    
    // ===== CONDIÇÕES =====
    
    /// <summary>
    /// Condição para executar o efeito (expressão booleana via MathEngine)
    /// Ex: "target_hp < target_max_hp * 0.5"
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
    
    /// <summary>
    /// Efeitos condicionais (executam se condição for verdadeira)
    /// </summary>
    public IReadOnlyList<EffectDefinition>? ConditionalEffects
    {
        get => _conditionalEffects;
        init => _conditionalEffects = value?.ToImmutableList();
    }
}
