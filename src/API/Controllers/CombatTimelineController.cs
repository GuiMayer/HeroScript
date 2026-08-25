using API.Contracts;
using Core.Run.Sandbox;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/combats/{combatId:guid}/timeline")]
public sealed class CombatTimelineController : BaseApiController
{
    private readonly ICombatTimelineProjectionService _timeline;

    public CombatTimelineController(ICombatTimelineProjectionService timeline, ILogger<CombatTimelineController> logger)
        : base(logger)
    {
        _timeline = timeline;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid combatId,
        [FromQuery] int afterSequence = 0,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
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
        var result = await _timeline.GetHistoricalStateAsync(combatId, sequence, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Historical combat state unavailable",
                result.Error);
    }
}
