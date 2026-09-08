using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}/card-selections")]
public sealed class CardSelectionController : BaseApiController
{
    private readonly IRunQueryService _runManager;

    public CardSelectionController(IRunQueryService runManager, ILogger<CardSelectionController> logger)
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
            : Ok(run.Value.CardSelections.Select(MapSelection).ToList());
    }

    [HttpGet("{selectionInstanceId:guid}")]
    public IActionResult Get(Guid runId, Guid selectionInstanceId)
    {
        var run = _runManager.GetRun(runId);
        if (run.IsFailure)
            return NotFound(new { error = run.Error });

        var selection = run.Value.CardSelections.FirstOrDefault(
            item => item.SelectionInstanceId == selectionInstanceId);
        return selection == null
            ? NotFound(new { error = $"Card selection not found: {selectionInstanceId}" })
            : Ok(MapSelection(selection));
    }

    private static object MapSelection(CardSelectionState selection)
    {
        return new
        {
            selection.SelectionInstanceId,
            selection.RunId,
            selection.SelectionId,
            selection.PickCount,
            selection.OfferCount,
            selection.CardPoolId,
            selection.Options,
            selection.RerollsUsed,
            selection.FreeRerollsRemaining,
            selection.RerollCosts,
            selection.Completed,
            selection.PickedCardIds,
            selection.DecomposedCardIds
        };
    }
}
