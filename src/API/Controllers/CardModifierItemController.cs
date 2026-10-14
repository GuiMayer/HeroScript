using API.Contracts;
using Core.Run.Content;
using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/card-modifier-items")]
public sealed class CardModifierItemController(ICardModifierItemQueryService items, IRunQueryService runs,
    CardModifierItemApplicationPlanner planner,
    ILogger<CardModifierItemController> logger) : BaseApiController(logger)
{
    [HttpGet]
    public IActionResult Read(Guid runId)
    {
        var result = items.Read(runId);
        return result.IsSuccess ? Ok(result.Value)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Modifier item query failed", result.Error);
    }

    [HttpGet("{itemInstanceId:guid}/preview/{cardInstanceId:guid}")]
    public IActionResult Preview(Guid runId, Guid itemInstanceId, Guid cardInstanceId)
    {
        var run = runs.GetRun(runId);
        if (run.IsFailure) return ApiBadRequest(ApiErrorCodes.InvalidOperation, "Modifier item preview failed", run.Error);
        var planned = planner.Plan(run.Value, itemInstanceId, cardInstanceId);
        return planned.IsSuccess ? Ok(new { runId, runSequence = run.Value.Sequence, expectedStep = run.Value.Determinism.Step,
            contentRevision = run.Value.Determinism.ContentRevision, itemInstanceId, cardInstanceId,
            before = planned.Value.Before, after = planned.Value.After })
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Modifier item preview failed", planned.Error);
    }
}
