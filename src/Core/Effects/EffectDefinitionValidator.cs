using System.Collections.Immutable;
using Core.Combat.Models;

namespace Core.Effects;

/// <summary>Executable contract shared by publication and execution, including branches that may never run.</summary>
public static class EffectDefinitionValidator
{
    public static bool IsExecutable(EffectType type) => type is
        EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE or
        EffectType.APPLY_STATUS or EffectType.REMOVE_STATUS or EffectType.DISPEL_STATUS or
        EffectType.CARD_ZONE_FLOW or
        EffectType.APPLY_MODIFIER or EffectType.REMOVE_MODIFIER;

    public static ImmutableArray<string> Validate(IEnumerable<EffectDefinition> effects,
        Action<EffectDefinition, string>? inspect = null)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        var outputIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            if (effect.OutputId is { } outputId)
            {
                if (outputId.Length == 0 || !(char.IsAsciiLetter(outputId[0]) || outputId[0] == '_') ||
                    outputId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                    Error("outputId must be a formula-safe identifier");
                if (!outputIds.Add(outputId)) Error($"duplicate outputId: {outputId}");
            }
            if (!IsExecutable(effect.Type)) Error($"effect type {effect.Type} has no executable runtime");
            if (!Enum.IsDefined(effect.Target)) Error("invalid target policy");
            if (effect.TargetLoss == null || !Enum.IsDefined(effect.TargetLoss.Policy))
                Error("invalid target loss policy");
            else if (effect.TargetLoss.Policy == EffectTargetLossPolicy.Retarget)
            {
                if (effect.TargetLoss.Retarget is not { } selector || !Enum.IsDefined(selector) ||
                    selector is EffectTarget.TARGET or EffectTarget.SELF)
                    Error("retarget requires an automatic target selector");
                if (effect.TargetLoss.Retarget is EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY &&
                    string.IsNullOrWhiteSpace(effect.SelectionResourceId))
                    Error("ranked retarget requires selectionResourceId");
            }
            else if (effect.TargetLoss.Retarget != null) Error("retarget selector requires the Retarget loss policy");
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
            if (effect.Parameters.Select(parameter => parameter.Parameter).Distinct().Count() != effect.Parameters.Length)
                Error("numeric parameter overrides must be unique");
            foreach (var parameter in effect.Parameters)
            {
                if (!EffectNumericParameters.Supports(effect.Type, parameter.Parameter)) Error("unsupported numeric parameter for effect type");
                if (string.IsNullOrWhiteSpace(parameter.UnitId) || string.IsNullOrWhiteSpace(parameter.Channel))
                    Error("numeric parameter requires channel and unitId");
                if (parameter.FlatValue is { } number && !float.IsFinite(number)) Error("numeric parameter must be finite");
                if (parameter.FlatValue == null && string.IsNullOrWhiteSpace(parameter.FormulaValue)) Error("numeric parameter requires a value");
                var conversion = Core.Calculations.CalculationValuePolicy.Validate(parameter.Conversion);
                if (conversion.IsFailure) Error(conversion.Error);
                if (parameter.Parameter != EffectNumericParameter.Amount && !parameter.Conversion.RequireInteger)
                    Error("count/duration parameter must require integer conversion");
                if (parameter.Parameter == EffectNumericParameter.Amount && (effect.FlatValue != null || !string.IsNullOrWhiteSpace(effect.FormulaValue)))
                    Error("amount parameter cannot coexist with flatValue/formulaValue");
                if (parameter.Parameter == EffectNumericParameter.StatusStacks && effect.StatusStacks != null ||
                    parameter.Parameter == EffectNumericParameter.StatusDuration && effect.StatusDuration != null ||
                    parameter.Parameter == EffectNumericParameter.ModifierStacks && effect.ModifierStacks != null ||
                    parameter.Parameter == EffectNumericParameter.ModifierDuration && effect.ModifierDuration != null)
                    Error("numeric parameter cannot coexist with a literal override of the same field");
                if (parameter.StageIds.Any(string.IsNullOrWhiteSpace) || parameter.StageIds.Distinct(StringComparer.Ordinal).Count() != parameter.StageIds.Length)
                    Error("numeric parameter stage IDs must be unique nonempty values");
            }
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
            if (effect.Type == EffectType.CARD_ZONE_FLOW)
            {
                if (effect.CardCount is < 1 or > EffectExecutionLimits.MaximumSteps) Error("invalid cardCount");
                if (effect.CardInstanceIds.Any(id => id == Guid.Empty) || effect.CardInstanceIds.Distinct().Count() != effect.CardInstanceIds.Length)
                    Error("cardInstanceIds must be unique nonempty ids");
                if (!effect.CardInstanceIds.IsEmpty && !effect.Parameters.Any(parameter => parameter.Parameter == EffectNumericParameter.CardCount) &&
                    effect.CardInstanceIds.Length != effect.CardCount) Error("cardInstanceIds must match cardCount");
            }
            if (effect.Type == EffectType.CARD_ZONE_FLOW && string.IsNullOrWhiteSpace(effect.CardZoneFlowId))
                Error("CARD_ZONE_FLOW requires cardZoneFlowId");
            inspect?.Invoke(effect, path);
            foreach (var (child, index) in (effect.ChainedEffects ?? []).Select((child, index) => (child, index)).Reverse())
                pending.Push((child, $"{path}.chainedEffects[{index}]", depth + 1, expansion));
        }
        return errors.ToImmutable();
    }
}
