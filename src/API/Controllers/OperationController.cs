using API.Models;
using Core.Math;
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
    private readonly IOperationMetadataProvider _metadataProvider;

    public OperationController(
        ILogger<OperationController> logger,
        IOperationMetadataProvider metadataProvider)
    {
        _logger = logger;
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
    }

    /// <summary>
    /// Get list of all available mathematical operations
    /// </summary>
    /// <returns>List of operation metadata</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<OperationMetadataDto>), StatusCodes.Status200OK)]
    public IActionResult GetOperations()
    {
        var operations = _metadataProvider.GetAllOperations();
        var dtos = operations.Select(MapToDto).ToList();
        return Ok(dtos);
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
        var result = _metadataProvider.GetOperation(name);

        if (result.IsFailure)
        {
            return NotFound(new { error = result.Error });
        }

        return Ok(MapToDto(result.Value));
    }

    /// <summary>
    /// Get operations grouped by category
    /// </summary>
    /// <returns>Dictionary of categories with their operations</returns>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(Dictionary<string, List<OperationMetadataDto>>), StatusCodes.Status200OK)]
    public IActionResult GetOperationsByCategory()
    {
        var grouped = _metadataProvider.GetOperationsByCategory();
        var dtos = grouped.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(MapToDto).ToList()
        );

        return Ok(dtos);
    }

    /// <summary>
    /// Maps Core OperationMetadata to API DTO
    /// </summary>
    private static OperationMetadataDto MapToDto(Core.Math.OperationMetadata metadata)
    {
        return new OperationMetadataDto
        {
            Name = metadata.Name,
            Symbol = metadata.Symbol,
            Description = metadata.Description,
            MinValues = metadata.MinValues,
            MaxValues = metadata.MaxValues,
            Category = metadata.Category,
            Behavior = metadata.Behavior,
            IsUnary = metadata.IsUnary
        };
    }
}
