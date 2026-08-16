using System.Collections.Immutable;
using Core.Effects;

namespace Core.Combat.Models;

/// <summary>
/// Definição de uma ação configurável.
/// Carregada de JSON, permite criar ações customizadas.
/// Effect é a unidade fundamental - todas as ações são compostas por Effects.
/// </summary>
public record ActionDefinition
{
    private ImmutableList<EffectDefinition> _effects = [];
    private ImmutableList<string> _tags = [];

    /// <summary>
    /// ID único da ação (ex: "basic_attack", "fireball", "heal").
    /// </summary>
    public string ActionId { get; init; } = string.Empty;
    
    /// <summary>
    /// Nome de exibição da ação.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;
    
    /// <summary>
    /// Descrição da ação.
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo da ação.
    /// </summary>
    public ActionType ActionType { get; init; }
    
    /// <summary>
    /// Custos de recursos para executar a ação.
    /// Custos também são representados como Effects (MODIFY_RESOURCE com valor negativo).
    /// </summary>
    public ActionCosts Costs { get; init; } = new();
    
    /// <summary>
    /// Efeitos da ação (dano, cura, status, etc.).
    /// Effect é a unidade fundamental de todas as ações em combate.
    /// </summary>
    public IReadOnlyList<EffectDefinition> Effects
    {
        get => _effects;
        init => _effects = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Se a ação requer um alvo.
    /// </summary>
    public bool RequiresTarget { get; init; } = true;
    
    /// <summary>
    /// Se a ação pode ter múltiplos alvos.
    /// </summary>
    public bool MultiTarget { get; init; }
    
    /// <summary>
    /// Cooldown em turnos (0 = sem cooldown).
    /// </summary>
    public int Cooldown { get; init; }
    
    /// <summary>
    /// Tags para categorização.
    /// </summary>
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableList() ?? [];
    }
}
