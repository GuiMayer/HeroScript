using API.Models;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Controller for mathematical operations metadata
/// </summary>
[ApiController]
[Route("api/operation")]
public class OperationController : ControllerBase
{
    private readonly ILogger<OperationController> _logger;
    private static readonly List<OperationMetadataDto> _operations = InitializeOperations();

    public OperationController(ILogger<OperationController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get list of all available mathematical operations
    /// </summary>
    /// <returns>List of operation metadata</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<OperationMetadataDto>), StatusCodes.Status200OK)]
    public IActionResult GetOperations()
    {
        return Ok(_operations);
    }

    /// <summary>
    /// Get metadata for a specific operation
    /// </summary>
    /// <param name="name">Operation name (case-insensitive)</param>
    /// <returns>Operation metadata</returns>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(OperationMetadataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetOperation(string name)
    {
        var operation = _operations.FirstOrDefault(op => 
            op.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (operation == null)
        {
            return NotFound(new { error = $"Operation '{name}' not found" });
        }

        return Ok(operation);
    }

    /// <summary>
    /// Get operations grouped by category
    /// </summary>
    /// <returns>Dictionary of categories with their operations</returns>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(Dictionary<string, List<OperationMetadataDto>>), StatusCodes.Status200OK)]
    public IActionResult GetOperationsByCategory()
    {
        var grouped = _operations
            .GroupBy(op => op.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        return Ok(grouped);
    }

    private static List<OperationMetadataDto> InitializeOperations()
    {
        return new List<OperationMetadataDto>
        {
            // Basic arithmetic operations
            new OperationMetadataDto
            {
                Name = "ADD",
                Symbol = "+",
                Description = "Add values to the current result (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "basic",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadataDto
            {
                Name = "SUBTRACT",
                Symbol = "-",
                Description = "Subtract values from the current result (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "basic",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadataDto
            {
                Name = "MULTIPLY",
                Symbol = "*",
                Description = "Multiply the current result by values (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "basic",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadataDto
            {
                Name = "DIVIDE",
                Symbol = "/",
                Description = "Divide the current result by values (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "basic",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadataDto
            {
                Name = "DIVIDE_INVERSE",
                Symbol = "÷⁻¹",
                Description = "Divide a numerator by the current result (inverse division)",
                MinValues = 1,
                MaxValues = 1,
                Category = "advanced",
                Behavior = "unary",
                IsUnary = true
            },

            // Power operations
            new OperationMetadataDto
            {
                Name = "POW",
                Symbol = "^",
                Description = "Raise the current result to a power (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "advanced",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadataDto
            {
                Name = "POW_BASE",
                Symbol = "base^x",
                Description = "Raise a base to the power of the current result",
                MinValues = 1,
                MaxValues = 1,
                Category = "advanced",
                Behavior = "unary",
                IsUnary = true
            },
            new OperationMetadataDto
            {
                Name = "SQRT",
                Symbol = "√",
                Description = "Calculate the square root of the current result",
                MinValues = 0,
                MaxValues = 0,
                Category = "advanced",
                Behavior = "unary",
                IsUnary = true
            },

            // Logarithm
            new OperationMetadataDto
            {
                Name = "LOG",
                Symbol = "log",
                Description = "Calculate logarithm of the current result (natural log if no base provided)",
                MinValues = 0,
                MaxValues = 1,
                Category = "advanced",
                Behavior = "unary-optional",
                IsUnary = true
            },

            // Unary operations
            new OperationMetadataDto
            {
                Name = "NEGATE",
                Symbol = "-x",
                Description = "Negate the current result (multiply by -1)",
                MinValues = 0,
                MaxValues = 0,
                Category = "basic",
                Behavior = "unary",
                IsUnary = true
            },
            new OperationMetadataDto
            {
                Name = "ABS",
                Symbol = "|x|",
                Description = "Calculate the absolute value of the current result",
                MinValues = 0,
                MaxValues = 0,
                Category = "basic",
                Behavior = "unary",
                IsUnary = true
            },
            new OperationMetadataDto
            {
                Name = "ROUND",
                Symbol = "round",
                Description = "Round the current result to specified decimal places (default: 0)",
                MinValues = 0,
                MaxValues = 1,
                Category = "basic",
                Behavior = "unary-optional",
                IsUnary = true
            },
            new OperationMetadataDto
            {
                Name = "FLOOR",
                Symbol = "⌊x⌋",
                Description = "Round down the current result to the nearest integer",
                MinValues = 0,
                MaxValues = 0,
                Category = "basic",
                Behavior = "unary",
                IsUnary = true
            },
            new OperationMetadataDto
            {
                Name = "CEIL",
                Symbol = "⌈x⌉",
                Description = "Round up the current result to the nearest integer",
                MinValues = 0,
                MaxValues = 0,
                Category = "basic",
                Behavior = "unary",
                IsUnary = true
            },

            // Multi-value operations
            new OperationMetadataDto
            {
                Name = "MIN",
                Symbol = "min",
                Description = "Return the minimum value between the current result and provided values",
                MinValues = 1,
                MaxValues = -1,
                Category = "multi-value",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadataDto
            {
                Name = "MAX",
                Symbol = "max",
                Description = "Return the maximum value between the current result and provided values",
                MinValues = 1,
                MaxValues = -1,
                Category = "multi-value",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadataDto
            {
                Name = "CLAMP",
                Symbol = "clamp",
                Description = "Clamp the current result between min and max values",
                MinValues = 2,
                MaxValues = 2,
                Category = "multi-value",
                Behavior = "unary",
                IsUnary = true
            },

            // Special operations
            new OperationMetadataDto
            {
                Name = "SET",
                Symbol = "=",
                Description = "Set the accumulator to a specific value (ignores previous result)",
                MinValues = 1,
                MaxValues = 1,
                Category = "special",
                Behavior = "set",
                IsUnary = false
            }
        };
    }
}
