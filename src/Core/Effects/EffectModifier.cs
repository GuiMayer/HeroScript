using System.Collections.Immutable;

namespace Core.Effects;

/// <summary>
/// Modificador que altera um efeito durante a run.
/// Usado por relíquias, poderes, status, e outros sistemas para modificar effects.
/// Imutável - cada modificador é uma transformação aplicada ao effect.
/// </summary>
public record EffectModifier
{
    private ImmutableList<string>? _addTags;
    private ImmutableList<string>? _removeTags;
    private ImmutableList<string>? _requiredTags;
    private ImmutableList<string>? _excludedTags;
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ID único do modificador
    /// </summary>
    public string ModifierId { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo de modificação
    /// </summary>
    public EffectModifierType Type { get; init; }
    
    // ===== MODIFICAÇÃO DE VALORES =====
    
    /// <summary>
    /// Multiplicador de valor (ex: 2.0 = dobra o valor)
    /// </summary>
    public float? ValueMultiplier { get; init; }
    
    /// <summary>
    /// Adição de valor (ex: +5 dano)
    /// </summary>
    public float? ValueAddition { get; init; }
    
    // ===== MODIFICAÇÃO DE TIPO =====
    
    /// <summary>
    /// Override do tipo de efeito (ex: DAMAGE → HEAL)
    /// </summary>
    public EffectType? OverrideType { get; init; }
    
    // ===== MODIFICAÇÃO DE ALVO =====
    
    /// <summary>
    /// Override do alvo (ex: TARGET → ALL_ENEMIES)
    /// </summary>
    public EffectTarget? OverrideTarget { get; init; }
    
    // ===== MODIFICAÇÃO DE TAGS =====
    
    /// <summary>
    /// Tags a adicionar ao efeito
    /// </summary>
    public IReadOnlyList<string>? AddTags
    {
        get => _addTags;
        init => _addTags = value?.ToImmutableList();
    }
    
    /// <summary>
    /// Tags a remover do efeito
    /// </summary>
    public IReadOnlyList<string>? RemoveTags
    {
        get => _removeTags;
        init => _removeTags = value?.ToImmutableList();
    }
    
    // ===== MODIFICAÇÃO DE CHANCE =====
    
    /// <summary>
    /// Multiplicador de chance (ex: 2.0 = dobra a chance)
    /// </summary>
    public float? ChanceMultiplier { get; init; }
    
    // ===== MODIFICAÇÃO DE REPETIÇÃO =====
    
    /// <summary>
    /// Adição de repetições (ex: +2 = executa 2 vezes a mais)
    /// </summary>
    public int? RepeatAddition { get; init; }
    
    // ===== ORIGEM DO MODIFICADOR =====
    
    /// <summary>
    /// ID da fonte do modificador (relíquia, poder, status, etc.)
    /// </summary>
    public string SourceId { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo da fonte (RELIC, POWER, STATUS, etc.)
    /// </summary>
    public string SourceType { get; init; } = string.Empty;
    
    // ===== CONDIÇÕES =====
    
    /// <summary>
    /// Condição para aplicar o modificador (expressão booleana via MathEngine)
    /// Ex: "attack_count % 10 == 0" (Pen Nib de Slay the Spire)
    /// </summary>
    public string? Condition { get; init; }
    
    /// <summary>
    /// Tags requeridas no efeito para aplicar o modificador
    /// Ex: ["attack"] - só modifica effects com tag "attack"
    /// </summary>
    public IReadOnlyList<string>? RequiredTags
    {
        get => _requiredTags;
        init => _requiredTags = value?.ToImmutableList();
    }
    
    /// <summary>
    /// Tags excluídas (modificador não se aplica se effect tem essas tags)
    /// </summary>
    public IReadOnlyList<string>? ExcludedTags
    {
        get => _excludedTags;
        init => _excludedTags = value?.ToImmutableList();
    }
    
    // ===== METADATA =====
    
    /// <summary>
    /// Metadata adicional
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Se o modificador é permanente (não expira)
    /// </summary>
    public bool IsPermanent { get; init; }
    
    /// <summary>
    /// Duração do modificador em turnos (se não permanente)
    /// </summary>
    public int? Duration { get; init; }
}

/// <summary>
/// Tipos de modificação de efeitos
/// </summary>
public enum EffectModifierType
{
    /// <summary>
    /// Multiplica valor do efeito
    /// </summary>
    MULTIPLY_VALUE,
    
    /// <summary>
    /// Adiciona valor ao efeito
    /// </summary>
    ADD_VALUE,
    
    /// <summary>
    /// Muda tipo do efeito (dano → cura)
    /// </summary>
    CHANGE_TYPE,
    
    /// <summary>
    /// Muda alvo do efeito
    /// </summary>
    CHANGE_TARGET,
    
    /// <summary>
    /// Adiciona tags ao efeito
    /// </summary>
    ADD_TAGS,
    
    /// <summary>
    /// Remove tags do efeito
    /// </summary>
    REMOVE_TAGS,
    
    /// <summary>
    /// Multiplica chance de execução
    /// </summary>
    MULTIPLY_CHANCE,
    
    /// <summary>
    /// Adiciona repetições
    /// </summary>
    ADD_REPEAT,
    
    /// <summary>
    /// Adiciona efeito encadeado
    /// </summary>
    CHAIN_EFFECT
}
