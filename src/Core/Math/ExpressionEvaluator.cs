using Core.Common;
using Core.Logging;
using System.Diagnostics;

namespace Core.Math;

/// <summary>
/// Serviço para avaliação de expressões matemáticas customizadas
/// Suporta dois modos de definição:
/// 1. Simple mode (Values) - aplica valores numéricos ao acumulador implícito
/// 2. Explicit mode (Operands) - aceita literais numéricos ou referências simbólicas ($current, $initial, params.X)
/// Segue padrões estabelecidos em docs/core-service-patterns.md
/// </summary>
public class ExpressionEvaluator : IExpressionEvaluator
{
    private readonly ILogger _logger;

    private static readonly HashSet<string> UnaryOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "SQRT", "ABS", "NEGATE", "FLOOR", "CEIL", "LOG"
    };

    public ExpressionEvaluator(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Avalia uma expressão matemática com múltiplos passos
    /// </summary>
    public Result<ExpressionEvaluationResult> Evaluate(ExpressionEvaluationRequest request)
    {
        if (request == null)
            return Result<ExpressionEvaluationResult>.Failure("Request cannot be null");

        if (request.Steps == null || request.Steps.Count == 0)
            return Result<ExpressionEvaluationResult>.Failure("At least one step is required");

        try
        {
            _logger.LogDebug($"Evaluating expression with {request.Steps.Count} steps, initial value: {request.InitialValue}");

            var stopwatch = Stopwatch.StartNew();
            var expression = new MathExpression(request.InitialValue);
            float currentValue = request.InitialValue;

            foreach (var step in request.Steps)
            {
                var stepResult = ProcessStep(step, request, ref currentValue, expression);
                if (stepResult.IsFailure)
                {
                    _logger.LogError($"Step '{step.Operation}' failed: {stepResult.Error}");
                    return Result<ExpressionEvaluationResult>.Failure(stepResult.Error);
                }
            }

            var result = expression.Build();
            stopwatch.Stop();

            _logger.LogDebug($"Expression evaluated successfully: {request.InitialValue} -> {result} in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");

            return Result<ExpressionEvaluationResult>.Success(new ExpressionEvaluationResult
            {
                Result = result,
                InitialValue = request.InitialValue,
                Steps = request.Steps,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError($"Invalid operation: {ex.Message}", ex);
            return Result<ExpressionEvaluationResult>.Failure($"Invalid operation: {ex.Message}");
        }
        catch (DivideByZeroException ex)
        {
            _logger.LogError($"Division by zero: {ex.Message}", ex);
            return Result<ExpressionEvaluationResult>.Failure($"Division by zero: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError($"Invalid argument: {ex.Message}", ex);
            return Result<ExpressionEvaluationResult>.Failure($"Invalid argument: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Evaluation failed: {ex.Message}", ex);
            return Result<ExpressionEvaluationResult>.Failure($"Evaluation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Processa um passo individual da expressão
    /// </summary>
    private Result<bool> ProcessStep(
        ExpressionStep step,
        ExpressionEvaluationRequest request,
        ref float currentValue,
        MathExpression expression)
    {
        bool hasValues = step.Values != null && step.Values.Length > 0;
        bool hasOperands = step.Operands != null && step.Operands.Count > 0;

        // Validar que apenas um modo é usado
        if (hasValues && hasOperands)
        {
            return Result<bool>.Failure(
                $"Step '{step.Operation}' cannot have both Values and Operands. Use one or the other.");
        }

        // MODE 0: Unary operations sem values/operands
        if (!hasValues && !hasOperands)
        {
            return ProcessUnaryOperation(step, ref currentValue, expression);
        }

        // MODE 1: Simple implicit accumulator (Values)
        if (hasValues)
        {
            return ProcessImplicitMode(step, ref currentValue, expression);
        }

        // MODE 2: Explicit operands (numeric literals or symbolic references)
        if (hasOperands)
        {
            return ProcessExplicitMode(step, request, ref currentValue, expression);
        }

        return Result<bool>.Failure($"Step '{step.Operation}' must have either Values or Operands.");
    }

    /// <summary>
    /// Processa operação unária (MODE 0)
    /// </summary>
    private Result<bool> ProcessUnaryOperation(
        ExpressionStep step,
        ref float currentValue,
        MathExpression expression)
    {
        if (!UnaryOperations.Contains(step.Operation))
        {
            return Result<bool>.Failure(
                $"Step '{step.Operation}' must have either Values or Operands.");
        }

        expression.AddRawStep(step.Operation, Array.Empty<float>());
        currentValue = MathEngine.SimulateOperationResult(step.Operation, currentValue, Array.Empty<float>());
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Processa modo simples com acumulador implícito (MODE 1)
    /// </summary>
    private Result<bool> ProcessImplicitMode(
        ExpressionStep step,
        ref float currentValue,
        MathExpression expression)
    {
        expression.AddRawStep(step.Operation, step.Values!);
        currentValue = MathEngine.SimulateOperationResult(step.Operation, currentValue, step.Values!);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Processa modo explícito com Operands (MODE 2)
    /// </summary>
    private Result<bool> ProcessExplicitMode(
        ExpressionStep step,
        ExpressionEvaluationRequest request,
        ref float currentValue,
        MathExpression expression)
    {
        // Detectar se algum operando é simbólico
        bool hasSymbolic = step.Operands!.Any(op =>
            op.StartsWith("$", StringComparison.OrdinalIgnoreCase) ||
            op.StartsWith("params.", StringComparison.OrdinalIgnoreCase));

        if (hasSymbolic)
        {
            return ProcessSymbolicMode(step, request, ref currentValue, expression);
        }
        else
        {
            return ProcessLiteralMode(step, ref currentValue, expression);
        }
    }

    /// <summary>
    /// Processa a variação simbólica do modo explícito
    /// </summary>
    private Result<bool> ProcessSymbolicMode(
        ExpressionStep step,
        ExpressionEvaluationRequest request,
        ref float currentValue,
        MathExpression expression)
    {
        // Validar operandos
        var validationErrors = MathEngine.ValidateOperands(step.Operands!, request.Parameters);
        if (validationErrors.Count > 0)
        {
            return Result<bool>.Failure(
                $"Invalid operands in step '{step.Operation}': {string.Join(", ", validationErrors)}");
        }

        // Resolver todos os operandos
        var resolvedValues = new List<float>();
        foreach (var operand in step.Operands!)
        {
            try
            {
                float resolvedValue = MathEngine.ResolveOperandPublic(
                    operand,
                    request.Parameters ?? new Dictionary<string, float>(),
                    request.InitialValue,
                    currentValue
                );
                resolvedValues.Add(resolvedValue);
            }
            catch (ArgumentException ex)
            {
                return Result<bool>.Failure(
                    $"Failed to resolve operand '{operand}' in step '{step.Operation}': {ex.Message}");
            }
        }

        // Adicionar operação resolvida
        expression.AddRawStep(step.Operation, resolvedValues.ToArray());
        currentValue = MathEngine.SimulateOperationResult(step.Operation, currentValue, resolvedValues.ToArray());
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Processa a variação literal do modo explícito
    /// </summary>
    private Result<bool> ProcessLiteralMode(
        ExpressionStep step,
        ref float currentValue,
        MathExpression expression)
    {
        try
        {
            expression.AddRawStepWithOperands(step.Operation, step.Operands!);

            // Converter operandos para floats para simulação
            var numericValues = step.Operands!.Select(op =>
                float.Parse(op, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture)
            ).ToArray();

            currentValue = MathEngine.SimulateOperationResult(step.Operation, currentValue, numericValues);
            return Result<bool>.Success(true);
        }
        catch (ArgumentException ex)
        {
            return Result<bool>.Failure(
                $"Invalid literal operand in step '{step.Operation}': {ex.Message}");
        }
        catch (FormatException ex)
        {
            return Result<bool>.Failure(
                $"Invalid numeric literal in step '{step.Operation}': {ex.Message}");
        }
    }
}
