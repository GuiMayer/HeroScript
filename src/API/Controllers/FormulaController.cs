using API.Helpers;
using API.Models;
using Core.Content;
using Core.Math;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace API.Controllers;

/// <summary>
/// Controller for evaluating formulas from MathFormulas.json
/// </summary>
[ApiController]
[Route("api/v1/simulations/formulas")]
public class FormulaController : ControllerBase
{
    private readonly IMathEngine _mathEngine;
    private readonly IContentRuntimeResolver _contentRuntimes;
    private readonly ILogger<FormulaController> _logger;

    public FormulaController(
        ILogger<FormulaController> logger,
        IMathEngine mathEngine,
        IContentRuntimeResolver contentRuntimes)
    {
        _logger = logger;
        _mathEngine = mathEngine;
        _contentRuntimes = contentRuntimes;
    }

    /// <summary>
    /// Get list of all available formulas
    /// </summary>
    /// <returns>List of formula information</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<FormulaInfoDto>), StatusCodes.Status200OK)]
    public IActionResult GetFormulas([FromQuery] string contentRevision)
    {
        try
        {
            var runtime = _contentRuntimes.Resolve(contentRevision);
            if (runtime.IsFailure)
                return NotFound(new { error = runtime.Error });
            var result = runtime.Value.GetDefinitions("formulas").Keys.Select(name =>
            {
                var definition = runtime.Value.GetDefinition<FormulaDefinition>("formulas", name).Value;
                return new FormulaInfoDto
                {
                    Name = name,
                    Description = definition.Description,
                    DefaultParams = new Dictionary<string, float>(definition.Params),
                    Origin = runtime.Value.Manifest.ConfigName
                };
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
    /// <param name="contentRevision">Published content revision containing the formula.</param>
    /// <returns>Formula information</returns>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(FormulaInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetFormula(string name, [FromQuery] string contentRevision)
    {
        try
        {
            var runtime = _contentRuntimes.Resolve(contentRevision);
            if (runtime.IsFailure)
                return NotFound(new { error = runtime.Error });
            var definition = runtime.Value.GetDefinition<FormulaDefinition>("formulas", name);
            if (definition.IsFailure)
                return NotFound(new { error = definition.Error });
            var result = new FormulaInfoDto
            {
                Name = name,
                Description = definition.Value.Description,
                DefaultParams = new Dictionary<string, float>(definition.Value.Params),
                Origin = runtime.Value.Manifest.ConfigName
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
        var validation = ValidationHelper.ValidateRequired(request.FormulaName, "FormulaName");
        if (!validation.IsValid)
        {
            return BadRequest(new { error = validation.ErrorMessage });
        }
        if (string.IsNullOrWhiteSpace(request.ContentRevision))
            return BadRequest(new { error = "ContentRevision is required" });

        try
        {
            var stopwatch = Stopwatch.StartNew();

            var runtime = _contentRuntimes.Resolve(request.ContentRevision);
            if (runtime.IsFailure)
                return NotFound(new { error = runtime.Error });
            var definition = runtime.Value.GetDefinition<FormulaDefinition>("formulas", request.FormulaName);
            if (definition.IsFailure)
                return NotFound(new { error = definition.Error });

            var expression = _mathEngine.BuildFromDefinition(
                request.FormulaName,
                definition.Value,
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
            var paramsUsed = new Dictionary<string, float>(definition.Value.Params, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in request.ParamOverrides ?? [])
                paramsUsed[name] = value;

            var response = new FormulaResponse
            {
                Result = result,
                FormulaName = request.FormulaName,
                Description = definition.Value.Description,
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

}
