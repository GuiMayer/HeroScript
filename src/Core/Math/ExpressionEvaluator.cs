using Core.Common;
using System.Diagnostics;

namespace Core.Math;

/// <summary>
/// Serviço para avaliação de expressões matemáticas customizadas
/// Suporta três modos:
/// 1. Implicit mode (Values) - operações baseadas em acumulador
/// 2. Explicit literal mode (Operands com strings numéricas) - operações com valores fixos
/// 3. Explicit symbolic mode (Operands com $current, $initial, params.X) - operações com parâmetros dinâmicos
/// </summary>
public class ExpressionEvaluator : IExpressionEvaluator
{
    private static readonly HashSet<string> UnaryOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "SQRT", "ABS", "NEGATE", "FLOOR", "CEIL", "LOG"
    };

    /// <summary>
    /// Avalia uma expressão matemática com múltiplos passos
    /// </summary>
    public Result<ExpressionEvaluationResult> Evaluate(ExpressionEvaluationRequest request)
    {
        if (request.Steps == null || request.Steps.Count == 0)
        {
            return Result<ExpressionEvaluationResult>.Failure("At least one step is required");
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var expression = new MathExpression(request.InitialValue);
            float currentValue = request.InitialValue;

            foreach (var step in request.Steps)
            {
                var stepResult = ProcessStep(step, request, ref currentValue, expression);
                if (stepResult.IsFailure)
                {
                    return Result<ExpressionEvaluationResult>.Failure(stepResult.Error);
                }
            }

            var result = expression.Build();
            stopwatch.Stop();

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
            return Result<ExpressionEvaluationResult>.Failure($"Invalid operation: {ex.Message}");
        }
        catch (DivideByZeroException ex)
        {
            return Result<ExpressionEvaluationResult>.Failure($"Division by zero: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            return Result<ExpressionEvaluationResult>.Failure($"Invalid argument: {ex.Message}");
        }
        catch (Exception ex)
        {
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

        // MODE 1: Implicit (Values) - modo acumulador legado
        if (hasValues)
        {
            return ProcessImplicitMode(step, ref currentValue, expression);
        }

        // MODE 2 & 3: Explicit (Operands)
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
    /// Processa modo implícito com Values (MODE 1)
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
    /// Processa modo explícito com Operands (MODE 2 & 3)
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
    /// Processa modo simbólico (MODE 3)
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
    /// Processa modo literal (MODE 2)
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
