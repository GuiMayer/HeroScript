using API.Contracts;
using API.Services;
using Core.Run;
using Core.Run.Sandbox;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/combats/{combatId:guid}/timeline")]
public sealed class CombatTimelineController : BaseApiController
{
    private readonly ICombatTimelineProjectionService _timeline;
    private readonly IRunQueryService _runs;
    private readonly IToolAccessPolicy _toolAccess;

    public CombatTimelineController(
        ICombatTimelineProjectionService timeline,
        IRunQueryService runs,
        IToolAccessPolicy toolAccess,
        ILogger<CombatTimelineController> logger)
        : base(logger)
    {
        _timeline = timeline;
        _runs = runs;
        _toolAccess = toolAccess;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid combatId,
        [FromQuery] int afterSequence = 0,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var denied = Authorize(combatId, ToolCapabilities.TimelineRead);
        if (denied != null)
            return denied;
        var result = await _timeline.GetAsync(combatId, afterSequence, limit, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Combat timeline unavailable",
                result.Error);
    }

    [HttpGet("{sequence:int}/state")]
    public async Task<IActionResult> GetState(
        Guid combatId,
        int sequence,
        CancellationToken cancellationToken = default)
    {
        var denied = Authorize(combatId, ToolCapabilities.TimelineInspectState);
        if (denied != null)
            return denied;
        var result = await _timeline.GetHistoricalStateAsync(combatId, sequence, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Historical combat state unavailable",
                result.Error);
    }

    private IActionResult? Authorize(Guid combatId, string capability)
    {
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);
        return _toolAccess.Allows(run.Value, capability)
            ? null
            : ApiProblem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "Tool capability denied",
                $"The active access profile and game mode do not allow '{capability}'");
    }
}
