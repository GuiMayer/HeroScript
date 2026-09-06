using Core.Common;
using Core.Logging;
using System.Globalization;
using Core.Content;

namespace Core.Math;

public interface IRuntimeFormulaEvaluator
{
    Result<float> Evaluate(
        string expressionOrFormulaId,
        Dictionary<string, float>? variables = null,
        float initialValue = 0f);

}

public interface IRevisionedRuntimeFormulaEvaluator : IRuntimeFormulaEvaluator
{
    Result<float> EvaluateAtRevision(
        string expressionOrFormulaId,
        string contentRevision,
        Dictionary<string, float>? variables = null,
        float initialValue = 0f);
}

public sealed class RuntimeFormulaEvaluator : IRevisionedRuntimeFormulaEvaluator
{
    private readonly IMathEngine _mathEngine;
    private readonly IExpressionEvaluator _expressionEvaluator;
    private readonly ILogger _logger;
    private readonly IContentRuntimeResolver? _contentRuntimes;

    public RuntimeFormulaEvaluator(
        IMathEngine mathEngine,
        IExpressionEvaluator expressionEvaluator,
        ILogger logger,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _mathEngine = mathEngine ?? throw new ArgumentNullException(nameof(mathEngine));
        _expressionEvaluator = expressionEvaluator ?? throw new ArgumentNullException(nameof(expressionEvaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contentRuntimes = contentRuntimes;
    }

    public Result<float> Evaluate(
        string expressionOrFormulaId,
        Dictionary<string, float>? variables = null,
        float initialValue = 0f) =>
        EvaluateCore(expressionOrFormulaId, variables, initialValue, contentRevision: null);

    public Result<float> EvaluateAtRevision(
        string expressionOrFormulaId,
        string contentRevision,
        Dictionary<string, float>? variables = null,
        float initialValue = 0f) =>
        EvaluateCore(expressionOrFormulaId, variables, initialValue, contentRevision);

    private Result<float> EvaluateCore(
        string expressionOrFormulaId,
        Dictionary<string, float>? variables,
        float initialValue,
        string? contentRevision)
    {
        if (string.IsNullOrWhiteSpace(expressionOrFormulaId))
            return Result<float>.Failure("Formula or expression cannot be empty");

        variables ??= new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(contentRevision) &&
            !expressionOrFormulaId.Contains(' ', StringComparison.Ordinal))
        {
            var revisioned = EvaluateRevisionedFormula(
                expressionOrFormulaId,
                contentRevision,
                variables,
                initialValue);
            if (revisioned != null)
                return revisioned;
        }

        if (IsFormulaId(expressionOrFormulaId))
            return EvaluateFormula(expressionOrFormulaId, variables, initialValue);

        return EvaluateInlineExpression(expressionOrFormulaId, variables, initialValue);
    }

    private Result<float>? EvaluateRevisionedFormula(
        string formulaId,
        string contentRevision,
        Dictionary<string, float> variables,
        float initialValue)
    {
        if (_contentRuntimes == null)
            return Result<float>.Failure("Revisioned formula runtime is not configured");

        var runtime = _contentRuntimes.Resolve(contentRevision);
        if (runtime.IsFailure)
            return Result<float>.Failure(runtime.Error);
        var definition = runtime.Value.GetDefinition<FormulaDefinition>("formulas", formulaId);
        if (definition.IsFailure)
        {
            // A single numeric/variable token is still a valid inline expression.
            if (variables.ContainsKey(formulaId) ||
                float.TryParse(formulaId, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return null;
            return Result<float>.Failure(definition.Error);
        }

        try
        {
            var expression = _mathEngine.BuildFromDefinition(
                formulaId,
                definition.Value,
                initialValue,
                variables);
            return Result<float>.Success(expression.Build());
        }
        catch (Exception exception)
        {
            _logger.LogError(
                $"Failed to evaluate formula '{formulaId}' from revision '{contentRevision}': {exception.Message}",
                exception);
            return Result<float>.Failure(
                $"Failed to evaluate revisioned formula '{formulaId}': {exception.Message}");
        }
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

    /// <summary>Checks the actual inline grammar without substituting gameplay values or executing rules.</summary>
    public static Result ValidateSyntax(string expression, Func<string, bool> variableExists, Func<string, bool> formulaExists)
    {
        if (string.IsNullOrWhiteSpace(expression)) return Result.Failure("Formula or expression cannot be empty");
        if (!expression.Contains(' ', StringComparison.Ordinal) && formulaExists(expression)) return Result.Success();
        var tokens = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length % 2 == 0) return Result.Failure("Invalid inline expression; operators and operands must be space-separated");
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (index % 2 == 1)
            {
                if (ResolveInlineOperation(token) == null) return Result.Failure($"Unsupported inline expression operator: '{token}'");
            }
            else if (!token.Equals("$initial", StringComparison.OrdinalIgnoreCase) &&
                     !token.Equals("$current", StringComparison.OrdinalIgnoreCase) && !variableExists(token) &&
                     !(float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)))
                return Result.Failure($"Unknown formula, variable or invalid operand: '{token}'");
        }
        return Result.Success();
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
