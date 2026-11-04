using System.Collections.Immutable;
using Core.Common;
using Core.Math;

namespace Core.Calculations;

public static class ContextualInfluencePolicies
{
    public static string? Validate(ContextualInfluenceDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.InfluenceId) || string.IsNullOrWhiteSpace(definition.Channel) ||
            string.IsNullOrWhiteSpace(definition.Bucket)) return "influence is incomplete";
        if (!Enum.IsDefined(definition.Scope)) return $"influence {definition.InfluenceId} has an invalid scope";
        if (definition.Value.HasValue == !string.IsNullOrWhiteSpace(definition.Formula))
            return $"influence {definition.InfluenceId} must define exactly one of value or formula";
        if (definition.Value is { } value && !float.IsFinite(value))
            return $"influence {definition.InfluenceId} value must be finite";
        if (definition.RequiredTags.Any(string.IsNullOrWhiteSpace) || definition.ExcludedTags.Any(string.IsNullOrWhiteSpace))
            return $"influence {definition.InfluenceId} contains an empty tag";
        if (definition.SubjectKeyCondition is { } condition &&
            (condition.Keys.IsDefaultOrEmpty || condition.RequiredKeys.IsDefault ||
             condition.Keys.Any(string.IsNullOrWhiteSpace) ||
             condition.Keys.Distinct(StringComparer.Ordinal).Count() != condition.Keys.Length ||
             condition.RequiredKeys.Any(key => !condition.Keys.Contains(key, StringComparer.Ordinal)) ||
             condition.MinimumCount < 0 || condition.MinimumCount > condition.Keys.Length ||
             condition.MaximumCount < condition.MinimumCount))
            return $"influence {definition.InfluenceId} has an invalid subject key condition";
        return null;
    }

    public static Result<IReadOnlyList<CalculationInfluence>> Resolve(
        IEnumerable<ContextualInfluenceDefinition> definitions, CalculationSourceKind kind, string sourceId,
        CalculationSourceContext context, IRuntimeFormulaEvaluator formulas)
    {
        var result = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var definition in definitions.OrderByDescending(item => item.Priority)
                     .ThenBy(item => item.InfluenceId, StringComparer.Ordinal))
        {
            if (!context.SelectsInfluence(definition.Channel, definition.Bucket)) continue;
            var invalid = Validate(definition);
            if (invalid != null) return Result<IReadOnlyList<CalculationInfluence>>.Failure(invalid);
            if (!definition.RequiredTags.All(context.Tags.Contains) || definition.ExcludedTags.Any(context.Tags.Contains))
                continue;
            if (definition.SubjectKeyCondition is { } condition)
            {
                if (context.Card is not { } card) continue;
                var count = condition.Keys.Count(key => card.Tags.Contains(key, StringComparer.Ordinal));
                if (count < condition.MinimumCount || count > condition.MaximumCount ||
                    !condition.RequiredKeys.All(key => card.Tags.Contains(key, StringComparer.Ordinal)))
                    continue;
            }
            var value = definition.Value;
            if (!value.HasValue)
            {
                var variables = context.Variables.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                var evaluated = !string.IsNullOrWhiteSpace(context.ContentRevision) &&
                                formulas is IRevisionedRuntimeFormulaEvaluator revisioned
                    ? revisioned.EvaluateAtRevision(definition.Formula!, context.ContentRevision, variables)
                    : formulas.Evaluate(definition.Formula!, variables);
                if (evaluated.IsFailure) return Result<IReadOnlyList<CalculationInfluence>>.Failure(evaluated.Error);
                value = evaluated.Value;
            }
            if (!float.IsFinite(value.Value))
                return Result<IReadOnlyList<CalculationInfluence>>.Failure($"Influence {definition.InfluenceId} produced a non-finite value");
            result.Add(new()
            {
                InfluenceId = definition.InfluenceId, SourceKind = kind, SourceId = sourceId,
                Channel = definition.Channel, Bucket = definition.Bucket, Value = value.Value, Priority = definition.Priority
            });
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }
}
