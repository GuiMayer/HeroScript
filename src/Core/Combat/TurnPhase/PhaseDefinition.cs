using System.Collections.Immutable;
using Core.Combat.Models;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Define as características e regras de uma fase específica do turno.
/// </summary>
public record PhaseDefinition
{
    private ImmutableList<ActionType> _allowedActions = [];
    private ImmutableList<TurnPhase> _validNextPhases = [];

    /// <summary>
    /// Nome para exibição (ex: "Main Phase 1", "Battle Phase")
    /// </summary>
    public string Name { get; init; } = "";
    
    /// <summary>
    /// Descrição da fase e suas regras
    /// </summary>
    public string Description { get; init; } = "";
    
    /// <summary>
    /// Ações permitidas durante esta fase
    /// </summary>
    public IReadOnlyList<ActionType> AllowedActions
    {
        get => _allowedActions;
        init => _allowedActions = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Fases válidas para transição a partir desta fase
    /// Vazio = apenas próxima fase na sequência
    /// </summary>
    public IReadOnlyList<TurnPhase> ValidNextPhases
    {
        get => _validNextPhases;
        init => _validNextPhases = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Se true, a fase avança automaticamente sem input do jogador
    /// Útil para fases de manutenção automática
    /// </summary>
    public bool AutoTransition { get; init; } = false;
    
    /// <summary>
    /// Se true, jogadores podem passar prioridade nesta fase
    /// Se false, a fase avança automaticamente (ex: Untap em Magic)
    /// </summary>
    public bool AllowPriority { get; init; } = true;
}
