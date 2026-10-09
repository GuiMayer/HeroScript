using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;

namespace Core.Calculations;

public sealed record ResolvedEffectAmount(
    float Value,
    CalculationResult? Calculation,
    CalculationPipelineDefinition? Pipeline = null)
{
    public ImmutableSortedDictionary<string, CalculationResult> Captures { get; init; } =
        ImmutableSortedDictionary<string, CalculationResult>.Empty.WithComparers(StringComparer.Ordinal);
}

public interface ICalculationResolver
{
    Result<ResolvedEffectAmount> Resolve(EffectDefinition effect, string calculationId, CalculationSourceContext context);
    Result<ResolvedEffectAmount> ResolveParameter(EffectDefinition owner, EffectNumericParameterDefinition parameter,
        string calculationId, CalculationSourceContext context);
    Result<ResolvedEffectAmount> ResolveProbability(EffectDefinition owner, EffectRandomProbabilityDefinition probability,
        EffectRandomScope scope, string calculationId, CalculationSourceContext context);
}

/// <summary>One numerical entry point for all effect origins, including preview and replay.</summary>
public sealed class CalculationResolver(
    IRuntimeFormulaEvaluator formulas,
    IContentRuntimeResolver? runtimes = null,
    ICalculationEngine? engine = null,
    ICalculationInfluenceProvider? influences = null,
    bool allowUnconfiguredCalculations = false) : ICalculationResolver
{
    public Result<ResolvedEffectAmount> Resolve(EffectDefinition effect, string calculationId, CalculationSourceContext context)
        => ResolveNumeric(effect, calculationId, context, "scalar", new());

    public Result<ResolvedEffectAmount> ResolveProbability(EffectDefinition owner, EffectRandomProbabilityDefinition probability,
        EffectRandomScope scope, string calculationId, CalculationSourceContext context)
    {
        var valid = EffectRandomProbabilityPolicies.Validate(probability);
        if (valid.IsFailure) return Result<ResolvedEffectAmount>.Failure(valid.Error);
        if (runtimes == null || context.Run?.ResolvedMode == null)
            return Result<ResolvedEffectAmount>.Failure("Calculated random inputs require pinned calculation services");
        var runtime = runtimes.Resolve(context.ContentRevision, context.Run.ConfigName);
        if (runtime.IsFailure) return Result<ResolvedEffectAmount>.Failure(runtime.Error);
        var numeric = owner with { FlatValue = probability.FlatValue, FormulaValue = probability.FormulaValue,
            CalculationChannel = probability.Channel, CalculationPipelineId = probability.PipelineId };
        var pipeline = ResolvePipeline(numeric, context.Run, runtime.Value);
        if (pipeline.IsFailure) return Result<ResolvedEffectAmount>.Failure(pipeline.Error);
        valid = EffectRandomProbabilityPolicies.ValidatePipeline(probability, pipeline.Value, scope);
        if (valid.IsFailure) return Result<ResolvedEffectAmount>.Failure(valid.Error);
        var sharedSource = scope != EffectRandomScope.Impact && probability.SharedContextCapture == EffectRandomSharedContextCapture.SourceOnly;
        var variables = context.Variables.Where(pair => !pair.Key.StartsWith("captures.", StringComparison.OrdinalIgnoreCase) &&
            (!sharedSource || !pair.Key.StartsWith("target.", StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.StartsWith("target_", StringComparison.OrdinalIgnoreCase) && pair.Key is not ("target_index" or "repeat_index")))
            .ToDictionary(StringComparer.Ordinal);
        var captures = ImmutableSortedDictionary.CreateBuilder<string, CalculationResult>(StringComparer.Ordinal);
        var captureContext = context with { CaptureOnly = true, Target = sharedSource ? null : context.Target, Variables = variables };
        foreach (var (id, capture) in probability.Captures)
        {
            var capturePipeline = ResolvePipeline(owner with { CalculationChannel = capture.Channel,
                CalculationPipelineId = capture.PipelineId }, context.Run, runtime.Value);
            if (capturePipeline.IsFailure) return Result<ResolvedEffectAmount>.Failure(capturePipeline.Error);
            valid = EffectRandomProbabilityPolicies.ValidateCapturePipeline(capture, capturePipeline.Value, scope, probability.SharedContextCapture);
            if (valid.IsFailure) return Result<ResolvedEffectAmount>.Failure(valid.Error);
            var captured = ResolveNumeric(owner with { FlatValue = capture.FlatValue, FormulaValue = capture.FormulaValue,
                CalculationChannel = capture.Channel, CalculationPipelineId = capture.PipelineId },
                calculationId + ":capture:" + id, captureContext with { StageIds = capture.StageIds }, capture.UnitId,
                capture.Conversion, "CapturedValue");
            if (captured.IsFailure) return captured;
            captures[id] = captured.Value.Calculation!;
            variables["captures." + id] = captured.Value.Value;
        }
        var result = ResolveNumeric(numeric, calculationId, context with { CaptureOnly = true, StageIds = probability.StageIds,
            Target = sharedSource ? null : context.Target,
            Variables = variables },
            probability.UnitId, probability.Conversion, "Probability");
        if (result.IsFailure) return result;
        return result.Value.Value is < 0 or > 1 || !float.IsFinite(result.Value.Value)
            ? Result<ResolvedEffectAmount>.Failure("Calculated probability must be finite and within [0, 1]")
            : Result<ResolvedEffectAmount>.Success(result.Value with { Captures = captures.ToImmutable() });
    }

    public Result<ResolvedEffectAmount> ResolveParameter(EffectDefinition owner,
        EffectNumericParameterDefinition parameter, string calculationId, CalculationSourceContext context)
    {
        if (parameter.Distribution != null)
            return Result<ResolvedEffectAmount>.Failure("Sequence distribution must be planned by the effect executor before numeric resolution");
        var numericEffect = owner with
        {
            FlatValue = parameter.FlatValue,
            FormulaValue = parameter.FormulaValue,
            CalculationChannel = parameter.Channel,
            CalculationPipelineId = parameter.PipelineId
        };
        return ResolveNumeric(numericEffect, calculationId, context with
        {
            StageIds = parameter.StageIds,
            CaptureOnly = context.CaptureOnly || parameter.Parameter != EffectNumericParameter.Amount
        }, parameter.UnitId, parameter.Conversion, parameter.Parameter.ToString());
    }

    private Result<ResolvedEffectAmount> ResolveNumeric(EffectDefinition effect, string calculationId,
        CalculationSourceContext context, string unitId, CalculationValuePolicy policy, string? parameterAttribute = null)
    {
        if (parameterAttribute == null && effect.Type is not (EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE))
            return Result<ResolvedEffectAmount>.Success(new(0, null));
        if (context.InputQuantity != null && (effect.FlatValue != null || !string.IsNullOrWhiteSpace(effect.FormulaValue)))
            return Result<ResolvedEffectAmount>.Failure("Transported quantity cannot coexist with a recalculated base");
        var amount = effect.FlatValue ?? 0;
        float? formulaAmount = null;
        if (!string.IsNullOrWhiteSpace(effect.FormulaValue))
        {
            var evaluated = GameplayFormulaContext.Evaluate(formulas, effect.FormulaValue,
                context.ContentRevision, context.Variables);
            if (evaluated.IsFailure) return Result<ResolvedEffectAmount>.Failure(evaluated.Error);
            formulaAmount = evaluated.Value;
            amount += formulaAmount.Value;
        }
        if (!float.IsFinite(amount))
            return Result<ResolvedEffectAmount>.Failure("Effect amount must be finite");
        if (parameterAttribute is not ("Probability" or "CapturedValue") && effect.Type is EffectType.DAMAGE or EffectType.HEAL && amount < 0)
            return Result<ResolvedEffectAmount>.Failure("DAMAGE and HEAL require non-negative amounts; use MODIFY_RESOURCE for signed changes");

        CalculationPipelineDefinition pipeline;
        if (context.Run?.ResolvedMode == null)
        {
            if (!allowUnconfiguredCalculations)
                return Result<ResolvedEffectAmount>.Failure(
                    $"Effect {effect.EffectId} requires a configured calculation pipeline");
            pipeline = context.Pipeline ?? new CalculationPipelineDefinition
            {
                PipelineId = "__unconfigured_test_identity__",
                Channel = effect.CalculationChannel,
                UnitId = unitId,
                Buckets = [new() { BucketId = "identity", Order = 0, Operation = CalculationBucketOperation.Add }]
            };
        }
        else
        {
            if (runtimes == null || engine == null || influences == null)
                return Result<ResolvedEffectAmount>.Failure("Calculation services are unavailable");
            var runtime = runtimes.Resolve(context.ContentRevision, context.Run.ConfigName);
            if (runtime.IsFailure) return Result<ResolvedEffectAmount>.Failure(runtime.Error);
            var resolvedPipeline = ResolvePipeline(effect, context.Run, runtime.Value);
            if (resolvedPipeline.IsFailure) return Result<ResolvedEffectAmount>.Failure(resolvedPipeline.Error);
            pipeline = resolvedPipeline.Value;
        }
        var tags = NormalizeTags(effect, context.Tags);
        var pipelineValidation = CalculationEngine.ValidateDefinition(pipeline);
        if (pipelineValidation.IsFailure) return Result<ResolvedEffectAmount>.Failure(pipelineValidation.Error);
        if (context.StageIds.Any(id => !pipeline.Stages.Any(stage => stage.StageId == id)))
            return Result<ResolvedEffectAmount>.Failure("Unknown calculation stage selection");
        var calculationContext = context with { Pipeline = pipeline, Tags = tags };
        var collected = influences?.Collect(calculationContext)
            ?? Result<IReadOnlyList<CalculationInfluence>>.Success([]);
        if (collected.IsFailure) return Result<ResolvedEffectAmount>.Failure(collected.Error);
        var calculated = (engine ?? new CalculationEngine(formulas)).Calculate(new CalculationRequest
        {
            CalculationId = calculationId,
            ContentRevision = context.ContentRevision,
            Channel = effect.CalculationChannel,
            BaseValue = amount,
            UnitId = unitId,
            StageIds = context.StageIds,
            InputQuantity = context.InputQuantity,
            ValuePolicy = policy,
            CaptureOnly = context.CaptureOnly,
            StageContextIds = pipeline.Stages.Where(stage => stage.Scope != CalculationStageScope.Shared)
                .ToImmutableSortedDictionary(stage => stage.StageId, stage =>
                    stage.Scope == CalculationStageScope.Actor ? context.Actor?.InstanceId ?? string.Empty : context.Target?.InstanceId ?? string.Empty,
                    StringComparer.Ordinal),
            BaseTrace = BuildBaseTrace(effect, context, formulaAmount, amount, parameterAttribute),
            Influences = collected.Value.Where(item => item.Channel == effect.CalculationChannel &&
                calculationContext.SelectsInfluence(item.Channel, item.Bucket)).ToArray(),
            Tags = tags,
            Variables = context.Variables
        }, pipeline);
        if (calculated.IsFailure) return Result<ResolvedEffectAmount>.Failure(calculated.Error);
        if (parameterAttribute is not ("Probability" or "CapturedValue") && effect.Type is EffectType.DAMAGE or EffectType.HEAL && calculated.Value.Value < 0)
            return Result<ResolvedEffectAmount>.Failure("DAMAGE and HEAL pipelines must produce non-negative amounts");
        return Result<ResolvedEffectAmount>.Success(new(calculated.Value.Value, calculated.Value, pipeline));
    }

    private static IReadOnlyList<CalculationBaseTrace> BuildBaseTrace(
        EffectDefinition effect,
        CalculationSourceContext context,
        float? formulaAmount,
        float amount, string? parameterAttribute)
    {
        var traces = new List<CalculationBaseTrace>();
        if (context.Card != null && !string.IsNullOrWhiteSpace(context.ComponentId))
        {
            var attribute = parameterAttribute == null ? "FlatValue" : $"{parameterAttribute}.FlatValue";
            var componentTrace = context.Card.UpgradeTrace.Where(item => item.ComponentId == context.ComponentId).ToArray();
            var lastStructural = Array.FindLastIndex(componentTrace, item => item.Attribute.StartsWith("Component.", StringComparison.Ordinal));
            var upgrades = componentTrace.Skip(lastStructural + 1).Where(item => item.Attribute == attribute).ToArray();
            var original = upgrades.FirstOrDefault()?.PreviousValue ?? effect.FlatValue;
            traces.Add(new()
            {
                SourceKind = CalculationSourceKind.Card,
                SourceId = context.Card.DefinitionId,
                ComponentId = context.ComponentId,
                Attribute = attribute,
                Operation = "Base",
                Output = original
            });
            traces.AddRange(upgrades.Select(item => new CalculationBaseTrace
            {
                SourceKind = CalculationSourceKind.Upgrade,
                SourceId = item.UpgradeId,
                ComponentId = item.ComponentId,
                Attribute = item.Attribute,
                Operation = item.Operation?.ToString() ?? string.Empty,
                Input = item.PreviousValue,
                Output = item.CurrentValue
            }));
        }
        else if (effect.FlatValue.HasValue)
        {
            traces.Add(new()
            {
                SourceKind = CalculationSourceKind.Effect,
                SourceId = effect.EffectId,
                ComponentId = context.ComponentId ?? string.Empty,
                Attribute = parameterAttribute ?? "FlatValue",
                Operation = "Base",
                Output = effect.FlatValue
            });
        }
        if (formulaAmount.HasValue)
        {
            traces.Add(new()
            {
                SourceKind = CalculationSourceKind.Effect,
                SourceId = effect.EffectId,
                ComponentId = context.ComponentId ?? string.Empty,
                Attribute = parameterAttribute == null ? "FormulaValue" : $"{parameterAttribute}.FormulaValue",
                Operation = "Add",
                Input = amount - formulaAmount.Value,
                Output = amount
            });
        }
        return traces;
    }

    public static IReadOnlySet<string> NormalizeTags(EffectDefinition effect, IReadOnlySet<string> supplied)
    {
        var tags = supplied.Concat(effect.Tags).ToHashSet(StringComparer.Ordinal);
        tags.Add(effect.Type switch
        {
            EffectType.DAMAGE => "effect.damage",
            EffectType.HEAL => "effect.heal",
            EffectType.MODIFY_RESOURCE => "effect.modify_resource",
            _ => $"effect.{effect.Type.ToString().ToLowerInvariant()}"
        });
        var operation = effect.Type switch
        {
            EffectType.DAMAGE => ResourceEffectOperation.SUBTRACT,
            EffectType.HEAL => ResourceEffectOperation.ADD,
            _ => effect.Operation
        };
        if (effect.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE)
        {
            tags.Add($"resource.{operation.ToString().ToLowerInvariant()}");
            tags.Add($"resource.field.{effect.ResourceField.ToString().ToLowerInvariant()}");
        }
        return tags;
    }

    public static Result<CalculationPipelineDefinition> ResolvePipeline(EffectDefinition effect, RunState run, ContentRuntime runtime)
    {
        var enabled = run.ResolvedMode?.Definition.CalculationPipelineIds ?? [];
        if (string.IsNullOrWhiteSpace(effect.CalculationPipelineId) &&
            run.ResolvedMode?.Definition.DefaultCalculationPipelines.TryGetValue(effect.CalculationChannel, out var defaultId) == true)
        {
            if (string.IsNullOrWhiteSpace(defaultId))
                return Result<CalculationPipelineDefinition>.Failure("Default calculation pipeline ID cannot be empty");
            return ResolvePipeline(effect with { CalculationPipelineId = defaultId }, run, runtime);
        }
        if (!string.IsNullOrWhiteSpace(effect.CalculationPipelineId))
        {
            if (!enabled.Contains(effect.CalculationPipelineId, StringComparer.Ordinal))
                return Result<CalculationPipelineDefinition>.Failure($"Calculation pipeline is not enabled by mode: {effect.CalculationPipelineId}");
            var explicitPipeline = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", effect.CalculationPipelineId);
            if (explicitPipeline.IsSuccess && explicitPipeline.Value.Channel != effect.CalculationChannel)
                return Result<CalculationPipelineDefinition>.Failure("Explicit calculation pipeline has a different channel");
            return explicitPipeline;
        }
        var compatible = new List<CalculationPipelineDefinition>();
        foreach (var id in enabled.OrderBy(id => id, StringComparer.Ordinal))
        {
            var definition = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", id);
            if (definition.IsFailure) return Result<CalculationPipelineDefinition>.Failure(definition.Error);
            if (definition.Value.Channel == effect.CalculationChannel) compatible.Add(definition.Value);
        }
        return compatible.Count == 1 ? Result<CalculationPipelineDefinition>.Success(compatible[0])
            : Result<CalculationPipelineDefinition>.Failure($"Effect channel {effect.CalculationChannel} requires exactly one enabled pipeline; found {compatible.Count}");
    }
}

public static class GameplayFormulaContext
{
    public static Dictionary<string, float> Build(CombatActorState source, CombatActorState? target,
        CombatActorState owner, RunState? run = null, IReadOnlyDictionary<string, float>? supplied = null)
    {
        var variables = supplied?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, float>(StringComparer.Ordinal);
        ResourceFormulaVariables.AddOwner(variables, "source", source.ResourceState);
        ResourceFormulaVariables.AddOwner(variables, "owner", owner.ResourceState);
        if (target != null) ResourceFormulaVariables.AddOwner(variables, "target", target.ResourceState);
        if (run != null) ResourceFormulaVariables.AddOwner(variables, "run", run.ResourceState);
        return variables;
    }

    public static Result<float> Evaluate(IRuntimeFormulaEvaluator formulas, string expression,
        string revision, IReadOnlyDictionary<string, float> variables)
    {
        var copy = variables.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var result = !string.IsNullOrWhiteSpace(revision) && formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(expression, revision, copy) : formulas.Evaluate(expression, copy);
        return result.IsSuccess && !float.IsFinite(result.Value)
            ? Result<float>.Failure("Formula produced a non-finite value") : result;
    }
}
