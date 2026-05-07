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
    /// Get list of all supported mathematical operations
    /// </summary>
    /// <returns>List of operation information</returns>
    [HttpGet("operations")]
    [ProducesResponseType(typeof(List<OperationInfoDto>), StatusCodes.Status200OK)]
    public IActionResult GetOperations()
    {
        var operations = new List<OperationInfoDto>
        {
            new() { Name = "ADD", Description = "Add values to current result", RequiresValues = true, Example = "{\"op\": \"ADD\", \"values\": [10, 20]}" },
            new() { Name = "SUBTRACT", Description = "Subtract values from current result", RequiresValues = true, Example = "{\"op\": \"SUBTRACT\", \"values\": [5]}" },
            new() { Name = "MULTIPLY", Description = "Multiply current result by values", RequiresValues = true, Example = "{\"op\": \"MULTIPLY\", \"values\": [2, 3]}" },
            new() { Name = "DIVIDE", Description = "Divide current result by values", RequiresValues = true, Example = "{\"op\": \"DIVIDE\", \"values\": [2]}" },
            new() { Name = "DIVIDE_INVERSE", Description = "Divide a value by current result (value / current)", RequiresValues = true, Example = "{\"op\": \"DIVIDE_INVERSE\", \"values\": [100]}" },
            new() { Name = "POW", Description = "Raise current result to power(s)", RequiresValues = true, Example = "{\"op\": \"POW\", \"values\": [2]}" },
            new() { Name = "POW_BASE", Description = "Raise a base to the power of current result (base ^ current)", RequiresValues = true, Example = "{\"op\": \"POW_BASE\", \"values\": [2]}" },
            new() { Name = "SQRT", Description = "Calculate square root of current result", RequiresValues = false, Example = "{\"op\": \"SQRT\", \"values\": []}" },
            new() { Name = "LOG", Description = "Calculate logarithm of current result (natural log if no base specified)", RequiresValues = false, Example = "{\"op\": \"LOG\", \"values\": [10]}" },
            new() { Name = "NEGATE", Description = "Negate current result (multiply by -1)", RequiresValues = false, Example = "{\"op\": \"NEGATE\", \"values\": []}" },
            new() { Name = "CLAMP", Description = "Clamp current result between min and max values", RequiresValues = true, Example = "{\"op\": \"CLAMP\", \"values\": [0, 100]}" }
        };

        return Ok(operations);
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

            // Build expression
            var expression = new MathExpression(request.InitialValue);

            // Apply each step
            foreach (var step in request.Steps)
            {
                ApplyStep(expression, step);
            }

            // Execute
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

    private void ApplyStep(MathExpression expression, MathStepDto step)
    {
        switch (step.Operation.ToUpper())
        {
            case "ADD":
                expression.Add(step.Values);
                break;

            case "SUBTRACT":
                expression.Subtract(step.Values);
                break;

            case "MULTIPLY":
                expression.Multiply(step.Values);
                break;

            case "DIVIDE":
                expression.Divide(step.Values);
                break;

            case "DIVIDE_INVERSE":
                if (step.Values.Length != 1)
                    throw new ArgumentException("DIVIDE_INVERSE requires exactly one value");
                expression.DivideInverse(step.Values[0]);
                break;

            case "POW":
                expression.Pow(step.Values);
                break;

            case "POW_BASE":
                if (step.Values.Length != 1)
                    throw new ArgumentException("POW_BASE requires exactly one value");
                expression.PowBase(step.Values[0]);
                break;

            case "SQRT":
                expression.Sqrt();
                break;

            case "LOG":
                if (step.Values.Length == 0)
                    expression.Log();
                else if (step.Values.Length == 1)
                    expression.Log(step.Values[0]);
                else
                    throw new ArgumentException("LOG accepts 0 or 1 value (base)");
                break;

            case "NEGATE":
                expression.Negate();
                break;

            case "CLAMP":
                if (step.Values.Length != 2)
                    throw new ArgumentException("CLAMP requires exactly 2 values (min, max)");
                expression.Clamp(step.Values[0], step.Values[1]);
                break;

            default:
                throw new InvalidOperationException($"Unknown operation: {step.Operation}");
        }
    }
}
