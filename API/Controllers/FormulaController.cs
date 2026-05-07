using API.Models;
using Core.Math;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace API.Controllers;

/// <summary>
/// Controller for evaluating formulas from MathFormulas.json
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FormulaController : ControllerBase
{
    private readonly MathEngine _mathEngine;
    private readonly ILogger<FormulaController> _logger;

    public FormulaController(ILogger<FormulaController> logger)
    {
        _logger = logger;
        _mathEngine = new MathEngine();
    }

    /// <summary>
    /// Get list of all available formulas
    /// </summary>
    /// <returns>List of formula information</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<FormulaInfoDto>), StatusCodes.Status200OK)]
    public IActionResult GetFormulas()
    {
        try
        {
            var formulas = _mathEngine.GetAvailableFormulas();
            var origins = MathEngine.GetFormulaOrigins();
            
            var result = formulas.Select(name => new FormulaInfoDto
            {
                Name = name,
                Description = _mathEngine.GetFormulaDescription(name) ?? string.Empty,
                DefaultParams = _mathEngine.GetFormulaDefaultParams(name) ?? new Dictionary<string, float>(),
                Origin = origins.TryGetValue(name, out var origin) ? origin : null
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting formulas");
            return StatusCode(500, new { error = "Failed to load formulas", details = ex.Message });
        }
    }

    /// <summary>
    /// Get details of a specific formula
    /// </summary>
    /// <param name="name">Formula name</param>
    /// <returns>Formula information</returns>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(FormulaInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetFormula(string name)
    {
        try
        {
            var description = _mathEngine.GetFormulaDescription(name);
            if (description == null)
            {
                return NotFound(new { error = $"Formula '{name}' not found" });
            }

            var origins = MathEngine.GetFormulaOrigins();
            var result = new FormulaInfoDto
            {
                Name = name,
                Description = description,
                DefaultParams = _mathEngine.GetFormulaDefaultParams(name) ?? new Dictionary<string, float>(),
                Origin = origins.TryGetValue(name, out var origin) ? origin : null
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting formula {FormulaName}", name);
            return StatusCode(500, new { error = "Failed to load formula", details = ex.Message });
        }
    }

    /// <summary>
    /// Evaluate a formula with given input and optional parameter overrides
    /// </summary>
    /// <param name="request">Formula evaluation request</param>
    /// <returns>Evaluation result</returns>
    [HttpPost("evaluate")]
    [ProducesResponseType(typeof(FormulaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Evaluate([FromBody] FormulaRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FormulaName))
        {
            return BadRequest(new { error = "FormulaName is required" });
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Build expression from formula
            var expression = _mathEngine.BuildFromFormula(
                request.FormulaName,
                request.InputValue,
                request.ParamOverrides
            );

            // Get steps before execution
            var steps = expression.GetSteps()
                .Select(s => new MathStepDto
                {
                    Operation = s.Operation,
                    Values = s.Values
                })
                .ToList();

            // Execute
            var result = expression.Build();

            stopwatch.Stop();

            // Get formula info
            var description = _mathEngine.GetFormulaDescription(request.FormulaName) ?? string.Empty;
            var defaultParams = _mathEngine.GetFormulaDefaultParams(request.FormulaName) ?? new Dictionary<string, float>();
            
            // Merge params (defaults + overrides)
            var paramsUsed = new Dictionary<string, float>(defaultParams);
            if (request.ParamOverrides != null)
            {
                foreach (var kvp in request.ParamOverrides)
                {
                    paramsUsed[kvp.Key] = kvp.Value;
                }
            }

            var response = new FormulaResponse
            {
                Result = result,
                FormulaName = request.FormulaName,
                Description = description,
                Steps = steps,
                ParamsUsed = paramsUsed,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            };

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid formula request: {FormulaName}", request.FormulaName);
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation in formula: {FormulaName}", request.FormulaName);
            return BadRequest(new { error = ex.Message });
        }
        catch (DivideByZeroException ex)
        {
            _logger.LogWarning(ex, "Division by zero in formula: {FormulaName}", request.FormulaName);
            return BadRequest(new { error = "Division by zero", details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating formula: {FormulaName}", request.FormulaName);
            return StatusCode(500, new { error = "Failed to evaluate formula", details = ex.Message });
        }
    }

    /// <summary>
    /// Reload formula cache (admin operation)
    /// </summary>
    /// <returns>Success message</returns>
    [HttpPost("reload")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Reload()
    {
        try
        {
            MathEngine.ReloadFormulas();
            _logger.LogInformation("Formula cache reloaded");
            return Ok(new { message = "Formula cache reloaded successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading formulas");
            return StatusCode(500, new { error = "Failed to reload formulas", details = ex.Message });
        }
    }
}
