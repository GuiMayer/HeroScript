using Core.Common;
using Core.Logging;
using System.Globalization;

namespace Core.Math;

public interface IRuntimeFormulaEvaluator
{
    Result<float> Evaluate(string expressionOrFormulaId, Dictionary<string, float>? variables = null, float initialValue = 0f);
}

public sealed class RuntimeFormulaEvaluator : IRuntimeFormulaEvaluator
{
    private readonly IMathEngine _mathEngine;
    private readonly IExpressionEvaluator _expressionEvaluator;
    private readonly ILogger _logger;

    public RuntimeFormulaEvaluator(IMathEngine mathEngine, IExpressionEvaluator expressionEvaluator, ILogger logger)
    {
        _mathEngine = mathEngine ?? throw new ArgumentNullException(nameof(mathEngine));
        _expressionEvaluator = expressionEvaluator ?? throw new ArgumentNullException(nameof(expressionEvaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Result<float> Evaluate(string expressionOrFormulaId, Dictionary<string, float>? variables = null, float initialValue = 0f)
    {
        if (string.IsNullOrWhiteSpace(expressionOrFormulaId))
            return Result<float>.Failure("Formula or expression cannot be empty");

        variables ??= new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        if (IsFormulaId(expressionOrFormulaId))
            return EvaluateFormula(expressionOrFormulaId, variables, initialValue);

        return EvaluateInlineExpression(expressionOrFormulaId, variables, initialValue);
    }

    private Result<float> EvaluateFormula(string formulaId, Dictionary<string, float> variables, float initialValue)
    {
        try
        {
            var expression = _mathEngine.BuildFromFormula(formulaId, initialValue, variables);
            return Result<float>.Success(expression.Build());
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to evaluate formula '{formulaId}': {ex.Message}", ex);
            return Result<float>.Failure($"Failed to evaluate formula '{formulaId}': {ex.Message}");
        }
    }

    private Result<float> EvaluateInlineExpression(string expression, Dictionary<string, float> variables, float initialValue)
    {
        var tokens = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Length % 2 == 0)
            return Result<float>.Failure($"Invalid inline expression: '{expression}'");

        var steps = new List<ExpressionStep>();
        if (!TryResolveToken(tokens[0], variables, initialValue, initialValue, out var current))
            return Result<float>.Failure($"Invalid inline expression operand: '{tokens[0]}'");

        steps.Add(new ExpressionStep { Operation = "SET", Values = new[] { current } });

        for (var i = 1; i < tokens.Length; i += 2)
        {
            var operation = ResolveInlineOperation(tokens[i]);
            if (operation == null)
                return Result<float>.Failure($"Unsupported inline expression operator: '{tokens[i]}'");

            if (!TryResolveToken(tokens[i + 1], variables, initialValue, current, out var value))
                return Result<float>.Failure($"Invalid inline expression operand: '{tokens[i + 1]}'");

            steps.Add(new ExpressionStep { Operation = operation, Values = new[] { value } });

            var simulation = _expressionEvaluator.Evaluate(new ExpressionEvaluationRequest
            {
                InitialValue = initialValue,
                Parameters = variables,
                Steps = steps
            });
            if (simulation.IsFailure)
                return Result<float>.Failure(simulation.Error);

            current = simulation.Value.Result;
        }

        var result = _expressionEvaluator.Evaluate(new ExpressionEvaluationRequest
        {
            InitialValue = initialValue,
            Parameters = variables,
            Steps = steps
        });

        return result.IsSuccess
            ? Result<float>.Success(result.Value.Result)
            : Result<float>.Failure(result.Error);
    }

    private bool IsFormulaId(string value)
    {
        return !value.Contains(' ', StringComparison.Ordinal) && _mathEngine.FormulaExists(value);
    }

    private static string? ResolveInlineOperation(string op)
    {
        return op switch
        {
            "+" => "ADD",
            "-" => "SUBTRACT",
            "*" => "MULTIPLY",
            "/" => "DIVIDE",
            "%" => "MODULO",
            _ => null
        };
    }

    private static bool TryResolveToken(string token, Dictionary<string, float> variables, float initialValue, float currentValue, out float value)
    {
        if (token.Equals("$initial", StringComparison.OrdinalIgnoreCase))
        {
            value = initialValue;
            return true;
        }

        if (token.Equals("$current", StringComparison.OrdinalIgnoreCase))
        {
            value = currentValue;
            return true;
        }

        return variables.TryGetValue(token, out value) ||
            float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
