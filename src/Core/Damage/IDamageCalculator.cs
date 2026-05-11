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
}
