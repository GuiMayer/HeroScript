using System.Collections.Immutable;
using System.Globalization;
using Core.Common;
using Core.Content;
using Core.Effects;
using Core.Math;

namespace Core.GameplayEvents.Responses;

/// <summary>Uses the canonical evaluator at the captured definition revision; never introduces a second expression language.</summary>
internal sealed class GameplayResponseEvaluation(IRuntimeFormulaEvaluator formulas, IContentRuntimeResolver? runtimes)
{
    internal Result<bool> Condition(GameplayResponseCandidate candidate)
    {
        var expression = candidate.Registration.Response.Definition.Condition;
        if (expression == null) return Result<bool>.Success(true);
        if (formulas is not IRevisionedRuntimeFormulaEvaluator revisioned)
            return Result<bool>.Failure("Response conditions require a revisioned formula evaluator");
        var variables = Variables(candidate, [expression]);
        if (variables.IsFailure) return Result<bool>.Failure(variables.Error);
        var result = revisioned.EvaluateAtRevision(expression, candidate.Registration.Origin.ContentRevision, variables.Value.ToDictionary());
        return result.IsFailure ? Result<bool>.Failure(result.Error) : !float.IsFinite(result.Value)
            ? Result<bool>.Failure("Response condition produced a non-finite value") : Result<bool>.Success(result.Value > 0);
    }

    internal Result<ImmutableDictionary<string, float>> EffectVariables(GameplayResponseCandidate candidate)
    {
        var pending = new Stack<EffectDefinition>(candidate.Registration.Response.Definition.Effects.Reverse());
        var expressions = new List<string>();
        var visited = 0;
        while (pending.TryPop(out var effect))
        {
            if (++visited > EffectExecutionLimits.MaximumSteps)
                return Result<ImmutableDictionary<string, float>>.Failure("Response effect reference budget exceeded");
            if (effect.FormulaValue != null) expressions.Add(effect.FormulaValue);
            if (effect.Condition != null) expressions.Add(effect.Condition);
            expressions.AddRange(effect.Parameters.Where(parameter => parameter.FormulaValue != null).Select(parameter => parameter.FormulaValue!));
            foreach (var child in effect.ChainedEffects ?? []) pending.Push(child);
        }
        return Variables(candidate, expressions);
    }

    private Result<ImmutableDictionary<string, float>> Variables(GameplayResponseCandidate candidate, IEnumerable<string> expressions)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expression in expressions)
        {
            if (expression.Contains(' ', StringComparison.Ordinal))
            {
                var tokens = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (var index = 0; index < tokens.Length; index += 2) Add(tokens[index]);
            }
            else if (Typed(expression)) Add(expression);
            else if (!float.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && expression is not ("$current" or "$initial") &&
                !CanonicalExecutionVariable(expression))
            {
                if (runtimes == null) return Result<ImmutableDictionary<string, float>>.Failure("Response formula references require captured content");
                var runtime = runtimes.Resolve(candidate.Registration.Origin.ContentRevision);
                if (runtime.IsFailure) return Result<ImmutableDictionary<string, float>>.Failure(runtime.Error);
                var formula = runtime.Value.GetDefinition<FormulaDefinition>("formulas", expression);
                if (formula.IsFailure) return Result<ImmutableDictionary<string, float>>.Failure(formula.Error);
                if (formula.Value.Operations.Count > EffectExecutionLimits.MaximumSteps)
                    return Result<ImmutableDictionary<string, float>>.Failure("Response formula reference budget exceeded");
                foreach (var operation in formula.Value.Operations)
                foreach (var token in (operation.Operands ?? []).Concat(new[] { operation.Value, operation.Min, operation.Max }.OfType<string>()))
                    if (token.StartsWith("params.", StringComparison.OrdinalIgnoreCase)) Add(token[7..]);
            }
            if (references.Count > 128) return Result<ImmutableDictionary<string, float>>.Failure("Response numeric reference budget exceeded");
        }
        return GameplayResponseNumbers.Read(candidate.Occurrence, candidate.Registration, candidate.Snapshot, candidate.OwnerActorId, references);

        void Add(string name) { if (Typed(name)) references.Add(name); }
    }

    private static bool Typed(string name) => name.StartsWith("event.", StringComparison.Ordinal) ||
        name.StartsWith("owner.", StringComparison.Ordinal) || name.StartsWith("scope.", StringComparison.Ordinal);

    private static bool CanonicalExecutionVariable(string name) => new[] { "source.", "target.", "run.", "results.", "rolls.", "captures.", "continuation.", "parent." }
        .Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}
