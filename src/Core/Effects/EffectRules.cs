using Core.Common;

namespace Core.Effects;

public static class EffectRules
{
    public static Result<bool> CanExecute(EffectInstance effect, IEffectContext context)
    {
        if (context.CombatState == null)
        {
            return RequiresCombatContext(effect.Definition.Type)
                ? Result<bool>.Failure($"Effect type {effect.Definition.Type} requires combat context")
                : Result<bool>.Success(true);
        }

        var source = context.CombatState.GetEntity(effect.SourceEntityId);
        if (source == null)
            return Result<bool>.Failure($"Source entity {effect.SourceEntityId} not found");

        var target = context.CombatState.GetEntity(effect.TargetEntityId);
        if (target == null && effect.Definition.Target == EffectTarget.TARGET)
            return Result<bool>.Failure($"Target entity {effect.TargetEntityId} not found");
        if (target != null && !target.IsAlive && RequiresLiveTarget(effect.Definition.Type))
            return Result<bool>.Failure($"Target entity {effect.TargetEntityId} is not alive");

        return Result<bool>.Success(true);
    }

    public static Result<bool> Validate(EffectDefinition definition)
    {
        if (definition.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE
            && definition.FlatValue == null
            && string.IsNullOrEmpty(definition.FormulaValue))
        {
            return Result<bool>.Failure(
                $"Effect type {definition.Type} requires FlatValue or FormulaValue");
        }
        if (definition.Type is EffectType.APPLY_STATUS or EffectType.REMOVE_STATUS
            && string.IsNullOrEmpty(definition.StatusId))
        {
            return Result<bool>.Failure($"Effect type {definition.Type} requires StatusId");
        }
        if (definition.Chance is < 0f or > 1f)
            return Result<bool>.Failure($"Chance must be between 0.0 and 1.0, got {definition.Chance}");
        if (definition.Repeat < 1)
            return Result<bool>.Failure($"Repeat must be at least 1, got {definition.Repeat}");
        return Result<bool>.Success(true);
    }

    private static bool RequiresLiveTarget(EffectType type) =>
        type is EffectType.DAMAGE or EffectType.HEAL or EffectType.APPLY_STATUS;

    private static bool RequiresCombatContext(EffectType type) =>
        type is EffectType.DAMAGE
            or EffectType.HEAL
            or EffectType.APPLY_STATUS
            or EffectType.REMOVE_STATUS
            or EffectType.DISPEL_STATUS
            or EffectType.PREVENT_ACTIONS
            or EffectType.FORCE_TARGET
            or EffectType.SKIP_TURN
            or EffectType.REFLECT_DAMAGE
            or EffectType.ABSORB_DAMAGE;
}
