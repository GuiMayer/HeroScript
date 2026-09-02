using API.Contracts;
using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/combats/{combatId:guid}/resolutions")]
[Produces("application/json", "application/problem+json")]
public sealed class CombatResolutionController : BaseApiController
{
    private readonly IRunManager _runs;

    public CombatResolutionController(
        IRunManager runs,
        ILogger<CombatResolutionController> logger)
        : base(logger)
    {
        _runs = runs;
    }

    [HttpGet("{commandId:guid}")]
    [ProducesResponseType(typeof(CombatResolutionRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult Get(Guid combatId, Guid commandId)
    {
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);
        var resolution = run.Value.GetCombatResolution(commandId);
        return resolution == null || resolution.CombatId != combatId
            ? ApiNotFound($"Combat resolution not found: {commandId}")
            : Ok(resolution);
    }
}
