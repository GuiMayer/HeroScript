using API.Models;
using Core.Math;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace API.Controllers;

/// <summary>
/// Controller for evaluating custom math expressions
/// </summary>
[ApiController]
[Route("api/v1/simulations/math/expression")]
public class MathExpressionController : ControllerBase
{
    private readonly ILogger<MathExpressionController> _logger;
    private readonly IExpressionEvaluator _expressionEvaluator;

    public MathExpressionController(
        ILogger<MathExpressionController> logger,
        IExpressionEvaluator expressionEvaluator)
    {
        _logger = logger;
        _expressionEvaluator = expressionEvaluator ?? throw new ArgumentNullException(nameof(expressionEvaluator));
    }

    /// <summary>
    /// Evaluate a custom math expression
    /// Supports two definition modes:
    /// 1. Simple mode (Values) - applies numeric values to the implicit accumulator
    /// 2. Explicit mode (Operands) - accepts numeric literals or symbolic references ($current, $initial, params.X)
    /// </summary>
    /// <param name="request">Math expression request</param>
    /// <returns>Evaluation result</returns>
    [HttpPost("evaluate")]
    [ProducesResponseType(typeof(MathExpressionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Evaluate([FromBody] MathExpressionRequest request)
    {
        try
        {
            // Convert API request to Core request
            var evaluationRequest = new ExpressionEvaluationRequest
            {
                InitialValue = request.InitialValue,
                Steps = request.Steps.Select(s => new ExpressionStep
                {
                    Operation = s.Operation,
                    Values = s.Values,
                    Operands = s.Operands
                }).ToList(),
                Parameters = request.Parameters
            };

            // Evaluate using Core service
            var result = _expressionEvaluator.Evaluate(evaluationRequest);

            if (result.IsFailure)
            {
                _logger.LogWarning("Expression evaluation failed: {Error}", result.Error);
                return BadRequest(new { error = result.Error });
            }

            // Convert Core response to API response
            var response = new MathExpressionResponse
            {
                Result = result.Value.Result,
                InitialValue = result.Value.InitialValue,
                Steps = request.Steps,
                ExecutionTimeMs = result.Value.ExecutionTimeMs
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error evaluating expression");
            return StatusCode(500, new { error = "Failed to evaluate expression", details = ex.Message });
        }
    }
}
