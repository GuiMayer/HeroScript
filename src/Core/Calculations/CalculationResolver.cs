using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;

namespace Core.Calculations;

public sealed record ResolvedEffectAmount(float Value, CalculationResult? Calculation);

public interface ICalculationResolver
{
    Result<ResolvedEffectAmount> Resolve(EffectDefinition effect, string calculationId, CalculationSourceContext context);
}

/// <summary>One numerical entry point for all effect origins, including preview and replay.</summary>
public sealed class CalculationResolver(
    IRuntimeFormulaEvaluator formulas,
    IContentRuntimeResolver? runtimes = null,
    ICalculationEngine? engine = null,
    ICalculationInfluenceProvider? influences = null) : ICalculationResolver
{
    public Result<ResolvedEffectAmount> Resolve(EffectDefinition effect, string calculationId, CalculationSourceContext context)
    {
        if (effect.Type is not (EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE))
            return Result<ResolvedEffectAmount>.Success(new(0, null));
        var amount = effect.FlatValue ?? 0;
        if (!string.IsNullOrWhiteSpace(effect.FormulaValue))
        {
            var evaluated = GameplayFormulaContext.Evaluate(formulas, effect.FormulaValue,
                context.ContentRevision, context.Variables);
            if (evaluated.IsFailure) return Result<ResolvedEffectAmount>.Failure(evaluated.Error);
            amount += evaluated.Value;
        }
        if (!float.IsFinite(amount))
            return Result<ResolvedEffectAmount>.Failure("Effect amount must be finite");
        if (effect.Type is EffectType.DAMAGE or EffectType.HEAL && amount < 0)
            return Result<ResolvedEffectAmount>.Failure("DAMAGE and HEAL require non-negative amounts; use MODIFY_RESOURCE for signed changes");

        // Pure low-level simulations can omit a mode. A configured run cannot silently bypass its pipeline.
        if (context.Run?.ResolvedMode == null)
            return Result<ResolvedEffectAmount>.Success(new(amount, null));
        if (runtimes == null || engine == null || influences == null)
            return Result<ResolvedEffectAmount>.Failure("Calculation services are unavailable");
        var runtime = runtimes.Resolve(context.ContentRevision, context.Run.ConfigName);
        if (runtime.IsFailure) return Result<ResolvedEffectAmount>.Failure(runtime.Error);
        var pipeline = ResolvePipeline(effect, context.Run, runtime.Value);
        if (pipeline.IsFailure) return Result<ResolvedEffectAmount>.Failure(pipeline.Error);
        var collected = influences.Collect(context with { Pipeline = pipeline.Value });
        if (collected.IsFailure) return Result<ResolvedEffectAmount>.Failure(collected.Error);
        var calculated = engine.Calculate(new CalculationRequest
        {
            CalculationId = calculationId, Channel = effect.CalculationChannel, BaseValue = amount,
            BaseTrace = BuildBaseTrace(effect, context),
            Influences = collected.Value.Where(item => item.Channel == effect.CalculationChannel).ToArray(),
            Tags = context.Tags
        }, pipeline.Value);
        if (calculated.IsFailure) return Result<ResolvedEffectAmount>.Failure(calculated.Error);
        if (effect.Type is EffectType.DAMAGE or EffectType.HEAL && calculated.Value.Value < 0)
            return Result<ResolvedEffectAmount>.Failure("DAMAGE and HEAL pipelines must produce non-negative amounts");
        return Result<ResolvedEffectAmount>.Success(new(calculated.Value.Value, calculated.Value));
    }

    private static IReadOnlyList<CalculationBaseTrace> BuildBaseTrace(EffectDefinition effect, CalculationSourceContext context)
    {
        if (context.Card == null || string.IsNullOrWhiteSpace(context.ComponentId)) return [];
        var traces = new List<CalculationBaseTrace>();
        var upgrades = context.Card.UpgradeTrace.Where(item => item.ComponentId == context.ComponentId &&
            item.Attribute == CardEffectNumericAttribute.FlatValue.ToString()).ToArray();
        var original = upgrades.FirstOrDefault()?.PreviousValue ?? effect.FlatValue;
        traces.Add(new()
        {
            SourceKind = CalculationSourceKind.Card, SourceId = context.Card.DefinitionId,
            ComponentId = context.ComponentId, Attribute = "FlatValue", Operation = "Base", Output = original
        });
        traces.AddRange(upgrades.Select(item => new CalculationBaseTrace
        {
            SourceKind = CalculationSourceKind.Upgrade, SourceId = item.UpgradeId,
            ComponentId = item.ComponentId, Attribute = item.Attribute, Operation = item.Operation?.ToString() ?? string.Empty,
            Input = item.PreviousValue, Output = item.CurrentValue
        }));
        return traces;
    }

    public static Result<CalculationPipelineDefinition> ResolvePipeline(EffectDefinition effect, RunState run, ContentRuntime runtime)
    {
        var enabled = run.ResolvedMode?.Definition.CalculationPipelineIds ?? [];
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
    public static Dictionary<string, float> Build(CombatEntity source, CombatEntity? target,
        CombatEntity owner, RunState? run = null, IReadOnlyDictionary<string, float>? supplied = null)
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
