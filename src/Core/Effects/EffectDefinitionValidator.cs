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
        EffectType.APPLY_MODIFIER or EffectType.REMOVE_MODIFIER or EffectType.CONDENSE_STACKS or EffectType.MODIFY_ATTRIBUTE;

    public static ImmutableArray<string> Validate(IEnumerable<EffectDefinition> effects,
        Action<EffectDefinition, string>? inspect = null)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        var outputIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var randomGroups = new Dictionary<string, string>(StringComparer.Ordinal);
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
            bool SafeGroup(string? group) => group == null || group.Length is > 0 and <= 64 &&
                group.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
            void CheckGroup(string key, string contract)
            {
                if (randomGroups.TryGetValue(key, out var previous) && previous != contract) Error("random group has conflicting probabilities");
                else randomGroups[key] = contract;
            }
            if (effect.OutputId is { } outputId)
            {
                if (outputId.Length == 0 || !(char.IsAsciiLetter(outputId[0]) || outputId[0] == '_') ||
                    outputId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                    Error("outputId must be a formula-safe identifier");
                if (!outputIds.Add(outputId)) Error($"duplicate outputId: {outputId}");
            }
            if (!IsExecutable(effect.Type)) Error($"effect type {effect.Type} has no executable runtime");
            if (effect.Type == EffectType.MODIFY_ATTRIBUTE)
            {
                if (effect.AttributeMutation is not { } mutation || string.IsNullOrWhiteSpace(mutation.ComponentId) ||
                    string.IsNullOrWhiteSpace(mutation.ValueId) || !Enum.IsDefined(mutation.Operation) || !Enum.IsDefined(mutation.Lifetime) ||
                    effect.Parameters.Count(parameter => parameter.Parameter == EffectNumericParameter.Amount) != 1)
                    Error("attribute mutation requires component/value IDs, lifetime, operation and explicit Amount");
                if (effect.AttributeMutation?.Lifetime == Core.Entity.AttributeLifetime.RunBase && effect.Target is not (EffectTarget.SELF or EffectTarget.TARGET))
                    Error("persistent attribute mutation must target the run player");
            }
            else if (effect.AttributeMutation != null) Error("attributeMutation requires MODIFY_ATTRIBUTE");
            if (!Enum.IsDefined(effect.ExecutionScope) || !Enum.IsDefined(effect.ChildTiming)) Error("invalid execution scope or child timing");
            if (!SafeGroup(effect.ExecutionGroupId) || !SafeGroup(effect.ChanceGroupId) ||
                effect.ExecutionGroupId != null && effect.ExecutionScope == EffectExecutionScope.EveryInvocation ||
                effect.ChanceGroupId != null && effect.ChanceScope is not (EffectChanceScope.PerAction or EffectChanceScope.PerProc))
                Error("group IDs require a compatible scoped execution or chance policy");
            if (effect.ChanceGroupId != null && float.IsFinite(effect.Chance)) CheckGroup($"chance:{effect.ChanceScope}:{effect.ChanceGroupId}", Core.Determinism.CanonicalJson.ComputeHash(effect.Chance));
            if (effect.RandomInputs.Length > 16 || effect.RandomInputs.Select(input => input.InputId).Distinct(StringComparer.Ordinal).Count() != effect.RandomInputs.Length ||
                effect.RandomInputs.Any(input => string.IsNullOrWhiteSpace(input.InputId) || input.InputId.Length > 64 ||
                    input.InputId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_') ||
                    !Enum.IsDefined(input.Scope) || input.Chance is { } chance && (!float.IsFinite(chance) || chance is < 0 or > 1) ||
                    input.Chance != null && input.Probability != null ||
                    !SafeGroup(input.GroupId) || input.GroupId != null && input.Scope == EffectRandomScope.Impact))
                Error("invalid or duplicate scoped random inputs");
            foreach (var input in effect.RandomInputs)
            {
                if (input.Probability != null)
                {
                    var valid = EffectRandomProbabilityPolicies.Validate(input.Probability);
                    if (valid.IsFailure) { Error(valid.Error); continue; }
                }
                if (input.Chance is { } chance && !float.IsFinite(chance)) continue;
                if (input.GroupId != null)
                    CheckGroup($"input:{input.Scope}:{input.GroupId}", Core.Determinism.CanonicalJson.ComputeHash(new { Chance = input.Probability == null ? input.Chance ?? 1 : (float?)null, input.Probability }));
            }
            if (effect.Type == EffectType.CONDENSE_STACKS)
            {
                if (string.IsNullOrWhiteSpace(effect.CondensationRecipeId)) Error("condensation requires condensationRecipeId");
                if (effect.Target is EffectTarget.ALL_ENEMIES or EffectTarget.ALL_ALLIES || effect.Repeat != 1 ||
                    !effect.Parameters.IsEmpty || effect.FlatValue != null || !string.IsNullOrWhiteSpace(effect.FormulaValue) ||
                    effect.ChainedEffects is { Count: > 0 })
                    Error("condensation is one activation; numeric inputs and child effects belong to its recipe");
            }
            else if (effect.CondensationRecipeId != null) Error("condensationRecipeId requires CONDENSE_STACKS");
            if (!Enum.IsDefined(effect.Target)) Error("invalid target policy");
            if (effect.Continuation is { } continuation)
            {
                if (effect.Type is not (EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE) ||
                    effect.ResourceField != Core.Resources.ResourceValueField.Current ||
                    effect.Parameters.Count(parameter => parameter.Parameter == EffectNumericParameter.Amount) != 1 ||
                    effect.Target is EffectTarget.ALL_ENEMIES or EffectTarget.ALL_ALLIES)
                    Error("continuation requires a single-target current-resource impact with an explicit Amount");
                if (continuation.MaximumHops is < 1 or > 32 ||
                    continuation.Selector is not (EffectTarget.RANDOM_ENEMY or EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY) ||
                    continuation.Selector is EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY && string.IsNullOrWhiteSpace(continuation.SelectionResourceId) ||
                    string.IsNullOrWhiteSpace(continuation.OverflowPipelineId) || string.IsNullOrWhiteSpace(continuation.OverflowChannel) ||
                    continuation.OverflowStageIds.IsEmpty || continuation.ImpactStageIds.IsEmpty ||
                    continuation.OverflowStageIds.Distinct(StringComparer.Ordinal).Count() != continuation.OverflowStageIds.Length ||
                    continuation.ImpactStageIds.Distinct(StringComparer.Ordinal).Count() != continuation.ImpactStageIds.Length)
                    Error("invalid continuation selector, limits or numeric stages");
            }
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
            if (effect.Parameters.Where(parameter => parameter.Distribution != null).Select(parameter => parameter.Distribution!.Scope).Distinct().Count() > 1)
                Error("one effect cannot mix sequence distribution scopes");
            foreach (var parameter in effect.Parameters)
            {
                if (!EffectNumericParameters.Supports(effect.Type, parameter.Parameter)) Error("unsupported numeric parameter for effect type");
                if (string.IsNullOrWhiteSpace(parameter.UnitId) || string.IsNullOrWhiteSpace(parameter.Channel))
                    Error("numeric parameter requires channel and unitId");
                if (parameter.FlatValue is { } number && !float.IsFinite(number)) Error("numeric parameter must be finite");
                if (parameter.InputQuantityId == null && parameter.FlatValue == null && string.IsNullOrWhiteSpace(parameter.FormulaValue))
                    Error("numeric parameter requires a value or inputQuantityId");
                if (parameter.InputQuantityId != null && (string.IsNullOrWhiteSpace(parameter.InputQuantityId) ||
                    parameter.FlatValue != null || !string.IsNullOrWhiteSpace(parameter.FormulaValue)))
                    Error("input quantity cannot coexist with a recalculated base");
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
                if (parameter.Distribution is { } distribution)
                {
                    if (!Enum.IsDefined(distribution.Scope) || distribution.Scope == EffectDistributionScope.ParentSequence && effect.Repeat != 1)
                        Error("parent sequence distribution requires repeat 1 and a valid scope");
                    if (parameter.Parameter is not (EffectNumericParameter.Amount or EffectNumericParameter.StatusStacks or EffectNumericParameter.ModifierStacks) ||
                        effect.Type == EffectType.REMOVE_MODIFIER)
                        Error("sequence distribution supports resource amounts and applied stacks only");
                    if (string.IsNullOrWhiteSpace(parameter.PipelineId) || distribution.SourceStageIds.IsEmpty || parameter.StageIds.IsEmpty ||
                        distribution.SourceStageIds.Any(string.IsNullOrWhiteSpace) ||
                        distribution.SourceStageIds.Distinct(StringComparer.Ordinal).Count() != distribution.SourceStageIds.Length ||
                        distribution.SourceStageIds.Intersect(parameter.StageIds, StringComparer.Ordinal).Any())
                        Error("sequence distribution requires an explicit pipeline and disjoint nonempty source/impact stages");
                    var allocation = distribution.Allocation;
                    if (allocation == null || !Enum.IsDefined(allocation.Mode) || !Enum.IsDefined(allocation.RemainderAllocation) ||
                        allocation.AllowTargetScopedInput ||
                        allocation.Mode == Core.Calculations.CalculationDistributionMode.Continuous && allocation.Quantum != null ||
                        allocation.Mode == Core.Calculations.CalculationDistributionMode.Quantized &&
                        (allocation.Quantum is not { } quantum || !float.IsFinite(quantum) || quantum <= 0))
                        Error("invalid sequence allocation policy; target-scoped source budgets are not supported");
                    if (parameter.Parameter != EffectNumericParameter.Amount &&
                        (allocation?.Mode != Core.Calculations.CalculationDistributionMode.Quantized || allocation.Quantum != 1))
                        Error("distributed stacks require Quantized allocation with quantum 1");
                    if (effect.Target is EffectTarget.ALL_ENEMIES or EffectTarget.ALL_ALLIES ||
                        effect.TargetLoss?.Retarget is EffectTarget.ALL_ENEMIES or EffectTarget.ALL_ALLIES)
                        Error("sequence distribution currently requires one target per impact");
                    if (parameter.InputQuantityId?.StartsWith("payload.", StringComparison.Ordinal) == true ||
                        parameter.FormulaValue?.Contains("target.", StringComparison.OrdinalIgnoreCase) == true ||
                        parameter.FormulaValue?.Contains("repeat_index", StringComparison.Ordinal) == true ||
                        parameter.FormulaValue?.Contains("target_index", StringComparison.Ordinal) == true)
                        Error("source budget cannot depend on a target, payload owner or impact index");
                }
            }
            if (!effect.PayloadBindings.IsEmpty && effect.Type is not (EffectType.APPLY_STATUS or EffectType.APPLY_MODIFIER))
                Error("payload bindings require an instance application effect");
            if (effect.PayloadBindings.Select(binding => binding.ParameterId).Distinct(StringComparer.Ordinal).Count() != effect.PayloadBindings.Length)
                Error("payload bindings must be unique");
            foreach (var binding in effect.PayloadBindings)
                if (!StackPayloadPolicies.SafeId(binding.ParameterId) || binding.FlatValue is { } number && !float.IsFinite(number) ||
                    binding.CardEffectComponentId != null && (string.IsNullOrWhiteSpace(binding.CardEffectComponentId) ||
                        binding.FlatValue != null || !string.IsNullOrWhiteSpace(binding.FormulaValue)) ||
                    binding.CardEffectComponentId == null && binding.FlatValue == null && string.IsNullOrWhiteSpace(binding.FormulaValue))
                    Error("invalid or ambiguous payload binding");
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
