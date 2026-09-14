using API.Contracts;
using API.Services;
using Core.Run;
using Core.Run.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/content/activation-preview")]
[Produces("application/json", "application/problem+json")]
public sealed class RunContentActivationController : BaseApiController
{
    private readonly IRunQueryService _runs;
    private readonly IContentRevisionActivationPreviewService _preview;
    private readonly IToolAccessPolicy _toolAccess;

    public RunContentActivationController(
        IRunQueryService runs,
        IContentRevisionActivationPreviewService preview,
        IToolAccessPolicy toolAccess,
        ILogger<RunContentActivationController> logger)
        : base(logger)
    {
        _runs = runs;
        _preview = preview;
        _toolAccess = toolAccess;
    }

    [HttpGet]
    public IActionResult Get(Guid runId, [FromQuery] string targetRevision)
    {
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);
        if (!_toolAccess.Allows(run.Value, ToolCapabilities.ContentActivate))
        {
            return ApiProblem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "Tool capability denied",
                $"The active access profile and game mode do not allow '{ToolCapabilities.ContentActivate}'");
        }

        var result = _preview.Preview(runId, targetRevision);
        return result.IsSuccess
            ? Ok(result.Value)
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Content activation preview failed",
                result.Error,
                run.Value.Sequence,
                run.Value.Determinism.Step);
    }
}
