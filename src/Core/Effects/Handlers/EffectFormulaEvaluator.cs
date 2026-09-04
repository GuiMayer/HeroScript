using Core.Logging;
using Core.Math;
using Core.Resources;

namespace Core.Effects.Handlers;

public sealed class EffectFormulaEvaluator
{
    private readonly IRuntimeFormulaEvaluator _formulaEvaluator;
    private readonly ILogger _logger;

    public EffectFormulaEvaluator(IRuntimeFormulaEvaluator formulaEvaluator, ILogger logger)
    {
        _formulaEvaluator = formulaEvaluator ?? throw new ArgumentNullException(nameof(formulaEvaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public float Calculate(EffectInstance effect, string targetId, IEffectContext context)
    {
        if (!string.IsNullOrEmpty(effect.Definition.FormulaValue))
        {
            var variables = BuildVariables(effect, targetId, context);
            var result = !string.IsNullOrWhiteSpace(context.ContentRevision) &&
                         _formulaEvaluator is IRevisionedRuntimeFormulaEvaluator revisioned
                ? revisioned.EvaluateAtRevision(
                effect.Definition.FormulaValue,
                    context.ContentRevision,
                    variables)
                : _formulaEvaluator.Evaluate(effect.Definition.FormulaValue, variables);
            if (result.IsSuccess)
                return result.Value;

            _logger.LogWarning(
                $"Failed to evaluate formula for effect {effect.InstanceId}: {result.Error}. Using flat value.");
        }

        return effect.Definition.FlatValue ?? 0f;
    }

    public bool EvaluateCondition(string condition, EffectInstance effect, IEffectContext context)
    {
        var targetId = string.IsNullOrWhiteSpace(effect.TargetEntityId)
            ? effect.SourceEntityId
            : effect.TargetEntityId;
        var variables = BuildVariables(effect, targetId, context);
        var result = !string.IsNullOrWhiteSpace(context.ContentRevision) &&
                     _formulaEvaluator is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(
                condition,
                context.ContentRevision,
                variables)
            : _formulaEvaluator.Evaluate(condition, variables);
        if (result.IsFailure)
        {
            _logger.LogWarning($"Failed to evaluate condition for effect {effect.InstanceId}: {result.Error}");
            return false;
        }

        return result.Value > 0f;
    }

    private static Dictionary<string, float> BuildVariables(
        EffectInstance effect,
        string targetId,
        IEffectContext context)
    {
        var variables = new Dictionary<string, float>();
        var source = context.CombatState?.GetEntity(effect.SourceEntityId);
        var target = context.CombatState?.GetEntity(targetId);

        if (source != null)
            ResourceFormulaVariables.AddOwner(variables, "source", source.ResourceState);
        if (target != null)
            ResourceFormulaVariables.AddOwner(variables, "target", target.ResourceState);

        return variables;
    }
}
