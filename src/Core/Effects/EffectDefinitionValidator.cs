using System.Collections.Immutable;
using Core.Combat.Models;

namespace Core.Effects;

/// <summary>Executable contract shared by publication and execution, including branches that may never run.</summary>
public static class EffectDefinitionValidator
{
    public static bool IsExecutable(EffectType type) => type is
        EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE or
        EffectType.APPLY_STATUS or EffectType.REMOVE_STATUS or EffectType.DISPEL_STATUS or
        EffectType.DRAW_CARD or EffectType.DISCARD_CARD or EffectType.EXHAUST_CARD or EffectType.ADD_CARD_TO_HAND or
        EffectType.APPLY_MODIFIER or EffectType.REMOVE_MODIFIER;

    public static ImmutableArray<string> Validate(IEnumerable<EffectDefinition> effects,
        Action<EffectDefinition, string>? inspect = null)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        var pending = new Stack<(EffectDefinition Effect, string Path, int Depth, long Multiplier)>();
        foreach (var (effect, index) in effects.Select((effect, index) => (effect, index)).Reverse())
            pending.Push((effect, $"effects[{index}]", 0, 1));
        long work = 0;
        while (pending.TryPop(out var item))
        {
            var (effect, path, depth, multiplier) = item;
            if (depth > EffectExecutionLimits.MaximumDepth || ++work > EffectExecutionLimits.MaximumSteps)
            {
                errors.Add($"{path}: effect execution limit exceeded");
                break;
            }
            if (effect == null) { errors.Add($"{path}: effect cannot be null"); continue; }
            void Error(string message) => errors.Add($"{path}: {message}");
            if (!IsExecutable(effect.Type)) Error($"effect type {effect.Type} has no executable runtime");
            if (!Enum.IsDefined(effect.Target)) Error("invalid target policy");
            if (effect.Timing != EffectTiming.IMMEDIATE) Error("only IMMEDIATE timing is executable; use an owner trigger for lifecycle timing");
            if (effect.IsPercentage) Error("isPercentage is not executable; express scaling through a formula or calculation influence");
            if (effect.ModifierKey != null || effect.ModifierValue != null || effect.ModifierFormula != null)
                Error("legacy modifier fields are not executable; use APPLY_MODIFIER with modifierId");
            if (!Enum.IsDefined(effect.ChanceScope) || !float.IsFinite(effect.Chance) || effect.Chance is < 0 or > 1)
                Error("invalid chance policy");
            if (effect.Repeat is < 1 or > EffectExecutionLimits.MaximumRepeat) Error("repeat is outside execution limits");
            var expansion = multiplier * System.Math.Clamp(effect.Repeat, 1, EffectExecutionLimits.MaximumRepeat);
            if (expansion > EffectExecutionLimits.MaximumSteps)
            {
                Error("nested repetition exceeds the effect execution limit");
                break;
            }
            if (effect.FlatValue is { } value && !float.IsFinite(value)) Error("flatValue must be finite");
            if (effect.Type is EffectType.DAMAGE or EffectType.HEAL && effect.FlatValue < 0)
                Error("resource aliases require a non-negative amount");
            if (effect.Target is EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY &&
                string.IsNullOrWhiteSpace(effect.SelectionResourceId)) Error("ranked target requires selectionResourceId");
            if (effect.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE)
            {
                if (string.IsNullOrWhiteSpace(effect.TargetResource)) Error("resource effect requires targetResource");
                if (string.IsNullOrWhiteSpace(effect.CalculationChannel)) Error("resource effect requires calculationChannel");
                if (!Enum.IsDefined(effect.Operation) || !Enum.IsDefined(effect.ResourceField)) Error("invalid resource operation or field");
            }
            if (effect.Type is EffectType.APPLY_STATUS or EffectType.REMOVE_STATUS && string.IsNullOrWhiteSpace(effect.StatusId))
                Error("status effect requires statusId");
            if (effect.StatusStacks is <= 0 || effect.StatusDuration is 0 or < -1) Error("invalid status stacks or duration");
            if (effect.Type == EffectType.DISPEL_STATUS && (effect.Dispel == null ||
                effect.Dispel.MaximumInstances < 1 || !Enum.IsDefined(effect.Dispel.Order))) Error("invalid dispel policy");
            if (effect.Type is EffectType.APPLY_MODIFIER or EffectType.REMOVE_MODIFIER && string.IsNullOrWhiteSpace(effect.ModifierId))
                Error("modifier effect requires modifierId");
            if (effect.ModifierStacks is <= 0 || effect.ModifierDuration is 0 or < -1) Error("invalid modifier stacks or duration");
            if (effect.ModifierOwner is { } owner && (!Enum.IsDefined(owner.Kind) ||
                owner.Kind is GameplayOwnerKind.Entity or GameplayOwnerKind.Side && string.IsNullOrWhiteSpace(owner.Id)))
                Error("invalid modifier owner");
            if (effect.Type is EffectType.DRAW_CARD or EffectType.DISCARD_CARD or EffectType.EXHAUST_CARD or EffectType.ADD_CARD_TO_HAND)
            {
                if (effect.CardCount is < 1 or > EffectExecutionLimits.MaximumSteps) Error("invalid cardCount");
                if (effect.CardInstanceIds.Any(id => id == Guid.Empty) || effect.CardInstanceIds.Distinct().Count() != effect.CardInstanceIds.Length)
                    Error("cardInstanceIds must be unique nonempty ids");
                if (!effect.CardInstanceIds.IsEmpty && effect.CardInstanceIds.Length != effect.CardCount) Error("cardInstanceIds must match cardCount");
            }
            if (effect.Type == EffectType.ADD_CARD_TO_HAND && string.IsNullOrWhiteSpace(effect.CardDefinitionId))
                Error("ADD_CARD_TO_HAND requires cardDefinitionId");
            if (effect.ConditionalEffects?.Count > 0) Error("conditionalEffects is unsupported; use chainedEffects with a condition");
            inspect?.Invoke(effect, path);
            foreach (var (child, index) in (effect.ChainedEffects ?? []).Select((child, index) => (child, index)).Reverse())
                pending.Push((child, $"{path}.chainedEffects[{index}]", depth + 1, expansion));
        }
        return errors.ToImmutable();
    }
}
