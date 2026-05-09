using Core.Common;

namespace Core.Math;

/// <summary>
/// Interface para avaliação de expressões matemáticas
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>
    /// Avalia uma expressão matemática com múltiplos passos
    /// </summary>
    /// <param name="request">Requisição com passos e parâmetros</param>
    /// <returns>Resultado da avaliação</returns>
    Result<ExpressionEvaluationResult> Evaluate(ExpressionEvaluationRequest request);
}

/// <summary>
/// Requisição de avaliação de expressão
/// </summary>
public class ExpressionEvaluationRequest
{
    public float InitialValue { get; set; }
    public List<ExpressionStep> Steps { get; set; } = new();
    public Dictionary<string, float>? Parameters { get; set; }
}

/// <summary>
/// Passo de uma expressão matemática
/// </summary>
public class ExpressionStep
{
    public string Operation { get; set; } = string.Empty;
    public float[]? Values { get; set; }
    public List<string>? Operands { get; set; }
}

/// <summary>
/// Resultado da avaliação de expressão
/// </summary>
public class ExpressionEvaluationResult
{
    public float Result { get; set; }
    public float InitialValue { get; set; }
    public List<ExpressionStep> Steps { get; set; } = new();
    public double ExecutionTimeMs { get; set; }
}
