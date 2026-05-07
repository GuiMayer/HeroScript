using API.Models;
using Core.Math;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace API.Controllers;

/// <summary>
/// Controller for evaluating custom math expressions
/// </summary>
[ApiController]
[Route("api/math/expression")]
public class MathExpressionController : ControllerBase
{
    private readonly ILogger<MathExpressionController> _logger;

    public MathExpressionController(ILogger<MathExpressionController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Evaluate a custom math expression
    /// </summary>
    /// <param name="request">Math expression request</param>
    /// <returns>Evaluation result</returns>
    [HttpPost("evaluate")]
    [ProducesResponseType(typeof(MathExpressionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Evaluate([FromBody] MathExpressionRequest request)
    {
        if (request.Steps == null || request.Steps.Count == 0)
        {
            return BadRequest(new { error = "At least one step is required" });
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();

            var expression = new MathExpression(request.InitialValue);

            foreach (var step in request.Steps)
            {
                expression.AddRawStep(step.Operation, step.Values);
            }

            var result = expression.Build();

            stopwatch.Stop();

            var response = new MathExpressionResponse
            {
                Result = result,
                InitialValue = request.InitialValue,
                Steps = request.Steps,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            };

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation in expression");
            return BadRequest(new { error = ex.Message });
        }
        catch (DivideByZeroException ex)
        {
            _logger.LogWarning(ex, "Division by zero in expression");
            return BadRequest(new { error = "Division by zero", details = ex.Message });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument in expression");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating expression");
            return StatusCode(500, new { error = "Failed to evaluate expression", details = ex.Message });
        }
    }
}
