using API.Contracts;
using Core.Run;
using Core.Run.Loadouts;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/loadout")]
public sealed class LoadoutController(ILoadoutQueryService loadouts, ILogger<LoadoutController> logger) : BaseApiController(logger)
{
    [HttpGet]
    [ProducesResponseType(typeof(LoadoutSnapshot), StatusCodes.Status200OK)]
    public IActionResult Read(Guid runId, [FromQuery] string actorId)
    {
        var result = loadouts.Read(runId, actorId);
        return result.IsSuccess ? Ok(result.Value) : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Loadout query failed", result.Error);
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(LoadoutPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public IActionResult Preview(Guid runId, [FromBody] LoadoutPreviewRequest request)
    {
        var result = loadouts.Preview(runId, request);
        if (result.IsSuccess) return Ok(result.Value);
        return result.Error.StartsWith(RunCommandErrors.VersionConflictPrefix, StringComparison.Ordinal)
            ? ApiProblem(StatusCodes.Status409Conflict, ApiErrorCodes.VersionConflict, "Loadout version conflict", result.Error)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Loadout preview failed", result.Error);
    }
}
