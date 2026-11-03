using API.Contracts;
using Core.Run;
using Core.Run.DeckLoadouts;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/deck-loadout")]
public sealed class DeckLoadoutController(IDeckLoadoutQueryService loadouts, ILogger<DeckLoadoutController> logger) : BaseApiController(logger)
{
    [HttpGet]
    [ProducesResponseType(typeof(DeckLoadoutSnapshot), StatusCodes.Status200OK)]
    public IActionResult Read(Guid runId)
    {
        var result = loadouts.Read(runId);
        return result.IsSuccess ? Ok(result.Value) : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Deck composition query failed", result.Error);
    }
    [HttpPost("preview")]
    [ProducesResponseType(typeof(DeckLoadoutPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public IActionResult Preview(Guid runId, [FromBody] DeckLoadoutPreviewRequest request)
    {
        var result = loadouts.Preview(runId, request);
        if (result.IsSuccess) return Ok(result.Value);
        return result.Error.StartsWith(RunCommandErrors.VersionConflictPrefix, StringComparison.Ordinal)
            ? ApiProblem(StatusCodes.Status409Conflict, ApiErrorCodes.VersionConflict, "Deck composition version conflict", result.Error)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Deck composition preview failed", result.Error);
    }
}
