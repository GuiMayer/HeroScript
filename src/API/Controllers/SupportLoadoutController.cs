using API.Contracts;
using Core.Run;
using Core.Run.Supports;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/support-loadout")]
public sealed class SupportLoadoutController(ISupportLoadoutQueryService loadouts, ILogger<SupportLoadoutController> logger) : BaseApiController(logger)
{
    [HttpGet]
    [ProducesResponseType(typeof(SupportLoadoutSnapshot), StatusCodes.Status200OK)]
    public IActionResult Read(Guid runId)
    {
        var result = loadouts.Read(runId);
        return result.IsSuccess ? Ok(result.Value) : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Support query failed", result.Error);
    }
    [HttpPost("preview")]
    [ProducesResponseType(typeof(SupportLoadoutPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public IActionResult Preview(Guid runId, [FromBody] SupportLoadoutPreviewRequest request)
    {
        var result = loadouts.Preview(runId, request);
        if (result.IsSuccess) return Ok(result.Value);
        return result.Error.StartsWith(RunCommandErrors.VersionConflictPrefix, StringComparison.Ordinal)
            ? ApiProblem(StatusCodes.Status409Conflict, ApiErrorCodes.VersionConflict, "Support version conflict", result.Error)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Support preview failed", result.Error);
    }
}
