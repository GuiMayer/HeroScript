using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/run/{runId:guid}/card-selection")]
public sealed class CardSelectionController : BaseApiController
{
    private readonly IRunManager _runManager;

    public CardSelectionController(IRunManager runManager, ILogger<CardSelectionController> logger)
        : base(logger)
    {
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
    }

    [HttpGet("/api/v1/runs/{runId:guid}/card-selections")]
    public IActionResult List(Guid runId)
    {
        var run = _runManager.GetRun(runId);
        return run.IsFailure
            ? NotFound(new { error = run.Error })
            : Ok(run.Value.CardSelections.Select(MapSelection).ToList());
    }

    [HttpGet("/api/v1/runs/{runId:guid}/card-selections/{selectionInstanceId:guid}")]
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

    [HttpPost("start")]
    public IActionResult Start(Guid runId, [FromBody] StartCardSelectionRequest? request = null)
    {
        try
        {
            var result = _runManager.CreateCardSelection(runId, request?.SelectionId ?? "basic_reward");
            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapSelection(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "start card selection", runId.ToString());
        }
    }

    [HttpPost("{selectionInstanceId:guid}/pick")]
    public IActionResult Pick(Guid runId, Guid selectionInstanceId, [FromBody] PickCardsRequest request)
    {
        try
        {
            var result = _runManager.PickCards(runId, selectionInstanceId, request.CardIds);
            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapSelection(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "pick card selection", runId.ToString());
        }
    }

    [HttpPost("{selectionInstanceId:guid}/reroll")]
    public IActionResult Reroll(Guid runId, Guid selectionInstanceId, [FromBody] RerollCardSelectionRequest? request = null)
    {
        try
        {
            var result = _runManager.RerollCardSelection(runId, selectionInstanceId, request?.LockedCardIds);
            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapSelection(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "reroll card selection", runId.ToString());
        }
    }

    [HttpPost("{selectionInstanceId:guid}/decompose/{cardId}")]
    public IActionResult Decompose(Guid runId, Guid selectionInstanceId, string cardId)
    {
        try
        {
            var result = _runManager.DecomposeCardSelectionOption(runId, selectionInstanceId, cardId);
            return result.IsFailure ? BadRequest(new { error = result.Error }) : Ok(MapSelection(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "decompose card selection option", runId.ToString());
        }
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
            selection.RerollCostGold,
            selection.Completed,
            selection.PickedCardIds,
            selection.DecomposedCardIds
        };
    }
}

public sealed record StartCardSelectionRequest(string? SelectionId);
public sealed record PickCardsRequest(IReadOnlyList<string> CardIds);
public sealed record RerollCardSelectionRequest(IReadOnlyList<string>? LockedCardIds);
