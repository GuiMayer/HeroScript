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

    [HttpPost("start")]
    public IActionResult Start(Guid runId, [FromBody] StartPreparationRequest? request = null)
    {
        try
        {
            var result = _runManager.CreatePreparation(runId, request?.PreparationId ?? "basic_preparation");
            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapPreparation(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "start preparation", runId.ToString());
        }
    }

    [HttpPost("{preparationInstanceId:guid}/apply/{optionId}")]
    public IActionResult Apply(Guid runId, Guid preparationInstanceId, string optionId)
    {
        try
        {
            var result = _runManager.ApplyPreparationOption(runId, preparationInstanceId, optionId);
            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapOption(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "apply preparation", runId.ToString());
        }
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

    private static object MapOption(PreparationOptionState option)
    {
        return new
        {
            option.OptionId,
            option.GoldCost,
            option.PowerPointCost,
            option.AddCardsToDiscard,
            option.ApplyModifiers,
            option.AppliedModifierInstanceIds,
            option.Applied
        };
    }
}

public sealed record StartPreparationRequest(string? PreparationId);
