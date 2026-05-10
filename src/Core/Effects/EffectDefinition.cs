namespace Core.Effects;

/// <summary>
/// Definição de um efeito configurável via JSON.
/// Effect é a unidade fundamental de todas as ações em combate.
/// Carregada de JSON, permite criar efeitos customizados.
/// </summary>
public record EffectDefinition
{
    /// <summary>
    /// ID único do efeito (gerado automaticamente se não fornecido)
    /// </summary>
    public string EffectId { get; init; } = Guid.NewGuid().ToString();
    
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
    /// Recurso alvo (health, energy, mana, etc.)
    /// </summary>
    public string? TargetResource { get; init; } = "health";
    
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
    public List<string>? RequiredTags { get; init; }
    
    /// <summary>
    /// Tags excluídas (NOT)
    /// Ex: ["physical"] - não executa se ação tem tag "physical"
    /// </summary>
    public List<string>? ExcludedTags { get; init; }
    
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
    public List<string> Tags { get; init; } = new();
    
    /// <summary>
    /// Metadata adicional (livre)
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    // ===== EFEITOS ENCADEADOS =====
    
    /// <summary>
    /// Efeitos disparados após este (sempre executam)
    /// </summary>
    public List<EffectDefinition>? ChainedEffects { get; init; }
    
    /// <summary>
    /// Efeitos condicionais (executam se condição for verdadeira)
    /// </summary>
    public List<EffectDefinition>? ConditionalEffects { get; init; }
}
