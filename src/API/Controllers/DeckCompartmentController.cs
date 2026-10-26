using API.Contracts;
using Core.Run;
using Core.Run.DeckCompartments;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/deck-compartments")]
public sealed class DeckCompartmentController(IDeckCompartmentQueryService compartments, ILogger<DeckCompartmentController> logger) : BaseApiController(logger)
{
    [HttpGet]
    [ProducesResponseType(typeof(DeckCompartmentSnapshot), StatusCodes.Status200OK)]
    public IActionResult Read(Guid runId)
    {
        var result = compartments.Read(runId);
        return result.IsSuccess ? Ok(result.Value) : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Compartment query failed", result.Error);
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(DeckCompartmentPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public IActionResult Preview(Guid runId, [FromBody] DeckCompartmentPreviewRequest request)
    {
        var result = compartments.Preview(runId, request);
        if (result.IsSuccess) return Ok(result.Value);
        return result.Error.StartsWith(RunCommandErrors.VersionConflictPrefix, StringComparison.Ordinal)
            ? ApiProblem(StatusCodes.Status409Conflict, ApiErrorCodes.VersionConflict, "Compartment version conflict", result.Error)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Compartment preview failed", result.Error);
    }
}
