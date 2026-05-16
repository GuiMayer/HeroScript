using Core.Combat.Models;

namespace Core.Effects;

/// <summary>
/// Resultado de aplicacao de um efeito em um escopo de jogo.
/// Guarda o resultado semantico e, quando houver, o estado atualizado.
/// </summary>
public record EffectApplicationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public EffectScope Scope { get; init; }
    public EffectResult EffectResult { get; init; } = null!;
    public CombatState? UpdatedCombatState { get; init; }

    public static EffectApplicationResult FromEffectResult(EffectScope scope, EffectResult result, CombatState? updatedCombatState = null)
    {
        return new EffectApplicationResult
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            Scope = scope,
            EffectResult = result,
            UpdatedCombatState = updatedCombatState
        };
    }

    public static EffectApplicationResult Failure(EffectScope scope, string errorMessage)
    {
        return new EffectApplicationResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Scope = scope,
            EffectResult = EffectResult.CreateFailure(errorMessage)
        };
    }
}
