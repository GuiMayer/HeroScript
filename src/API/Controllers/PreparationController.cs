using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/preparations")]
public sealed class PreparationController : BaseApiController
{
    private readonly IRunManager _runManager;

    public PreparationController(IRunManager runManager, ILogger<PreparationController> logger)
        : base(logger)
    {
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
    }

    [HttpGet]
    public IActionResult List(Guid runId)
    {
        var run = _runManager.GetRun(runId);
        return run.IsFailure
            ? NotFound(new { error = run.Error })
            : Ok(run.Value.Preparations.Select(MapPreparation).ToList());
    }

    [HttpGet("{preparationInstanceId:guid}")]
    public IActionResult Get(Guid runId, Guid preparationInstanceId)
    {
        var run = _runManager.GetRun(runId);
        if (run.IsFailure)
            return NotFound(new { error = run.Error });

        var preparation = run.Value.Preparations.FirstOrDefault(
            item => item.PreparationInstanceId == preparationInstanceId);
        return preparation == null
            ? NotFound(new { error = $"Preparation not found: {preparationInstanceId}" })
            : Ok(MapPreparation(preparation));
    }

    private static object MapPreparation(PreparationState preparation)
    {
        return new
        {
            preparation.PreparationInstanceId,
            preparation.RunId,
            preparation.PreparationId,
            preparation.Options,
            preparation.AppliedOptionIds
        };
    }

}
