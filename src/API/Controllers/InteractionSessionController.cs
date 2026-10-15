using API.Contracts;
using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/session")]
public sealed class InteractionSessionController(InteractionSessionService sessions,
    ILogger<InteractionSessionController> logger) : BaseApiController(logger)
{
    [HttpGet]
    [ProducesResponseType(typeof(InteractionSession), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public IActionResult Read(Guid runId, [FromQuery] int? expectedSequence = null)
    {
        var result = sessions.Read(runId, expectedSequence, Request.Headers.IfNoneMatch.ToString());
        if (result.IsFailure)
            return result.Error.StartsWith("VERSION_CONFLICT:", StringComparison.Ordinal)
                ? ApiProblem(409, ApiErrorCodes.VersionConflict, "Session revision conflict", result.Error)
                : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Session unavailable", result.Error);
        Response.Headers.ETag = result.Value.ETag;
        Response.Headers.CacheControl = "private, no-cache";
        return result.Value.Session == null ? StatusCode(304) : Ok(result.Value.Session);
    }
}
