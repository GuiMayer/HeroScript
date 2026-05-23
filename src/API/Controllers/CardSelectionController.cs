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

    private static object MapSelection(CardSelectionState selection)
    {
        return new
        {
            selection.SelectionInstanceId,
            selection.RunId,
            selection.SelectionId,
            selection.PickCount,
            selection.Options,
            selection.Completed,
            selection.PickedCardIds
        };
    }
}

public sealed record StartCardSelectionRequest(string? SelectionId);
public sealed record PickCardsRequest(IReadOnlyList<string> CardIds);
