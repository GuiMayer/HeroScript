using Core.Combat.Models;

namespace Core.Damage;

/// <summary>
/// Interface para cálculo de dano usando o pipeline
/// </summary>
public interface IDamageCalculator
{
    /// <summary>
    /// Calcula dano de uma ação considerando attacker e target
    /// </summary>
    DamageResult CalculateDamage(
        ActionDefinition action,
        CombatEntity attacker,
        CombatEntity target);

    /// <summary>
    /// Calcula dano usando a fonte de aleatoriedade explícita da transição.
    /// </summary>
    DamageResult CalculateDamage(
        ActionDefinition action,
        CombatEntity attacker,
        CombatEntity target,
        IRandomProvider randomProvider);
}

public interface IRevisionedDamageCalculator
{
    DamageResult CalculateDamage(
        ActionDefinition action,
        CombatEntity attacker,
        CombatEntity target,
        IRandomProvider randomProvider,
        string contentRevision,
        string? pipelineId = null);
}
