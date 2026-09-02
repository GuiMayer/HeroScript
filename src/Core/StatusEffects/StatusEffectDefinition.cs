using System.Collections.Immutable;
using Core.Combat.Flow;

namespace Core.StatusEffects;

/// <summary>
/// Definição de um status effect configurável via JSON.
/// Status effects são efeitos temporários ou permanentes que modificam o comportamento de entidades.
/// Totalmente data-driven, permitindo criar status effects customizados sem código.
/// </summary>
public record StatusEffectDefinition
{
    private ImmutableList<string> _tags = [];
    private ImmutableDictionary<string, object> _customData =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>
    /// ID único do status effect
    /// </summary>
    public string StatusId { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo do status effect
    /// </summary>
    public StatusEffectType Type { get; init; }
    
    /// <summary>
    /// Nome para exibição
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;
    
    /// <summary>
    /// Descrição do efeito
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    // ===== COMPORTAMENTO =====
    
    /// <summary>
    /// Comportamento do status effect (como é processado)
    /// </summary>
    public StatusEffectBehavior Behavior { get; init; }
    
    /// <summary>
    /// Duração padrão em turnos (-1 = permanente)
    /// </summary>
    public int DefaultDuration { get; init; } = -1;
    
    /// <summary>
    /// Número padrão de stacks ao aplicar
    /// </summary>
    public int DefaultStacks { get; init; } = 1;
    
    /// <summary>
    /// Número máximo de stacks permitidos
    /// </summary>
    public int MaxStacks { get; init; } = 99;
    
    // ===== VALORES =====
    
    /// <summary>
    /// Valor base do efeito (dano, cura, modificador)
    /// Usado quando FormulaValue não está definido
    /// </summary>
    public float BaseValue { get; init; }
    
    /// <summary>
    /// Fórmula dinâmica para calcular valor (usa MathEngine)
    /// Contexto disponível: stacks, target_hp, target_max_hp, source_*, etc.
    /// Ex: "stacks * 3" para Burning que causa 3 de dano por stack
    /// Ex: "stacks * 0.25" para Strength que aumenta dano em 25% por stack
    /// </summary>
    public string? FormulaValue { get; init; }
    
    /// <summary>
    /// Se true, o valor escala com número de stacks
    /// Se false, o valor é fixo independente de stacks
    /// </summary>
    public bool ScalesWithStacks { get; init; } = true;

    /// <summary>
    /// Recurso alterado por comportamentos que aumentam ou reduzem um pool.
    /// Obrigatório para DAMAGE_OVER_TIME, HEAL_OVER_TIME e REACTIVE.
    /// </summary>
    public string? TargetResource { get; init; }
    
    // ===== INTEGRAÇÃO COM PIPELINE =====
    
    /// <summary>
    /// Chave do modificador no damage pipeline
    /// Ex: "increased_damage_total" para Strength
    /// Ex: "increased_damage_taken" para Vulnerable
    /// Ex: "crit_chance" para Dexterity
    /// Quando definido, o status effect injeta modificadores no pipeline de dano
    /// </summary>
    public string? ModifierKey { get; init; }
    
    /// <summary>
    /// Fórmula para calcular o valor do modificador (usa MathEngine)
    /// Contexto disponível: stacks, target_*, source_*, etc.
    /// Ex: "stacks * 0.25" para Strength (25% por stack)
    /// Ex: "stacks * 0.5" para Vulnerable (50% por stack)
    /// </summary>
    public string? ModifierFormula { get; init; }
    
    // ===== TIMING =====
    
    /// <summary>
    /// Quando o status effect é processado
    /// </summary>
    public StatusEffectTiming Timing { get; init; }

    /// <summary>
    /// Exact canonical combat boundary that actively executes this status.
    /// Reactive and passive statuses leave this as Unspecified.
    /// </summary>
    public StatusTriggerBoundary TriggerBoundary { get; init; }

    /// <summary>
    /// Exact canonical combat boundary that decrements finite duration.
    /// Permanent statuses leave this as Unspecified.
    /// </summary>
    public StatusTriggerBoundary DurationTickBoundary { get; init; }

    /// <summary>
    /// Higher values resolve first; instance id is the deterministic tie-break.
    /// </summary>
    public int Priority { get; init; }
    
    // ===== VISUAL =====
    
    /// <summary>
    /// Caminho do ícone para UI
    /// </summary>
    public string IconPath { get; init; } = string.Empty;
    
    /// <summary>
    /// Cor do status effect (hex)
    /// </summary>
    public string Color { get; init; } = "#FFFFFF";
    
    // ===== TAGS =====
    
    /// <summary>
    /// Tags para categorização e sinergias
    /// Ex: ["fire", "dot", "debuff"]
    /// </summary>
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableList() ?? [];
    }
    
    // ===== FLEXIBILIDADE =====
    
    /// <summary>
    /// Dados customizados para comportamentos únicos
    /// Permite estender funcionalidade sem modificar código
    /// Ex: { "damage_cap": 1 } para Intangible
    /// Ex: { "trigger_on": "status_applied" } para Evolve
    /// </summary>
    public IReadOnlyDictionary<string, object> CustomData
    {
        get => _customData;
        init => _customData = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
