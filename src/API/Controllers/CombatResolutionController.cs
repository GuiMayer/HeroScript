using API.Contracts;
using Core.Run;
using Core.Run.Projections;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/combats/{combatId:guid}/resolutions")]
[Produces("application/json", "application/problem+json")]
public sealed class CombatResolutionController : BaseApiController
{
    private readonly IRunQueryService _runs;
    private readonly ICombatResolutionReader _resolutions;

    public CombatResolutionController(
        IRunQueryService runs,
        ICombatResolutionReader resolutions,
        ILogger<CombatResolutionController> logger)
        : base(logger)
    {
        _runs = runs;
        _resolutions = resolutions;
    }

    [HttpGet("{commandId:guid}")]
    [ProducesResponseType(typeof(CombatResolutionRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid combatId,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);
        var result = await _resolutions.GetAsync(
            run.Value.RunId,
            commandId,
            cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
            return ApiProblem(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "Combat resolution read failed",
                result.Error);
        var resolution = result.Value;
        return resolution == null || resolution.CombatId != combatId
            ? ApiNotFound($"Combat resolution not found: {commandId}")
            : Ok(resolution);
    }
}
