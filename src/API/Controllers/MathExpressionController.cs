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
    /// Supports three modes:
    /// 1. Implicit mode (Values) - legacy accumulator-based operations
    /// 2. Explicit literal mode (Operands with numeric strings) - explicit operations with fixed values
    /// 3. Explicit symbolic mode (Operands with $current, $initial, params.X) - dynamic operations with parameters
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
            float currentValue = request.InitialValue;

            foreach (var step in request.Steps)
            {
                // Validate that only one mode is used
                bool hasValues = step.Values != null && step.Values.Length > 0;
                bool hasOperands = step.Operands != null && step.Operands.Count > 0;

                if (hasValues && hasOperands)
                {
                    return BadRequest(new { 
                        error = $"Step '{step.Operation}' cannot have both Values and Operands. Use one or the other.",
                        step = step.Operation
                    });
                }

                // Define unary operations that can work without values/operands
                var unaryOperations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "SQRT", "ABS", "NEGATE", "FLOOR", "CEIL", "LOG"
                };

                // MODE 0: Unary operations without values/operands (operates on current value)
                if (!hasValues && !hasOperands)
                {
                    if (!unaryOperations.Contains(step.Operation))
                    {
                        return BadRequest(new { 
                            error = $"Step '{step.Operation}' must have either Values or Operands.",
                            step = step.Operation
                        });
                    }

                    // Unary operation on current value - use empty array
                    expression.AddRawStep(step.Operation, Array.Empty<float>());
                    currentValue = MathEngine.SimulateOperationResult(step.Operation, currentValue, Array.Empty<float>());
                }
                // MODE 1: Implicit (Values) - legacy accumulator mode
                else if (hasValues)
                {
                    expression.AddRawStep(step.Operation, step.Values!);
                    currentValue = MathEngine.SimulateOperationResult(step.Operation, currentValue, step.Values!);
                }
                // MODE 2 & 3: Explicit (Operands) - detect if symbolic or literal
                else if (hasOperands)
                {
                    // Detect if any operand is symbolic
                    bool hasSymbolic = step.Operands!.Any(op => 
                        op.StartsWith("$", StringComparison.OrdinalIgnoreCase) || 
                        op.StartsWith("params.", StringComparison.OrdinalIgnoreCase));

                    if (hasSymbolic)
                    {
                        // MODE 3: Explicit symbolic - requires resolution
                        
                        // Validate operands first
                        var validationErrors = MathEngine.ValidateOperands(step.Operands!, request.Parameters);
                        if (validationErrors.Count > 0)
                        {
                            return BadRequest(new { 
                                error = "Invalid operands in step",
                                step = step.Operation,
                                validationErrors = validationErrors
                            });
                        }

                        // Resolve all operands
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
                                return BadRequest(new { 
                                    error = $"Failed to resolve operand '{operand}'",
                                    step = step.Operation,
                                    details = ex.Message
                                });
                            }
                        }

                        // Add resolved operation to expression
                        expression.AddRawStep(step.Operation, resolvedValues.ToArray());
                        currentValue = MathEngine.SimulateOperationResult(step.Operation, currentValue, resolvedValues.ToArray());
                    }
                    else
                    {
                        // MODE 2: Explicit literal - convert strings to floats
                        try
                        {
                            expression.AddRawStepWithOperands(step.Operation, step.Operands!);
                            
                            // Convert operands to floats for simulation
                            var numericValues = step.Operands!.Select(op => 
                                float.Parse(op, System.Globalization.NumberStyles.Float, 
                                    System.Globalization.CultureInfo.InvariantCulture)
                            ).ToArray();
                            
                            currentValue = MathEngine.SimulateOperationResult(step.Operation, currentValue, numericValues);
                        }
                        catch (ArgumentException ex)
                        {
                            return BadRequest(new { 
                                error = $"Invalid literal operand in step '{step.Operation}'",
                                details = ex.Message
                            });
                        }
                        catch (FormatException ex)
                        {
                            return BadRequest(new { 
                                error = $"Invalid numeric literal in step '{step.Operation}'",
                                details = ex.Message
                            });
                        }
                    }
                }
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
