using API.Contracts;
using Core.Run.Content;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Read-only card legality and resolution projections for visual clients.
/// Gameplay mutations remain exclusive to the command endpoints.
/// </summary>
[ApiController]
[Route("api/v1/combats/{combatId:guid}/cards")]
public sealed class CombatCardController : BaseApiController
{
    private readonly ICardInspectionService _cards;

    public CombatCardController(
        ICardInspectionService cards,
        ILogger<CombatCardController> logger)
        : base(logger) => _cards = cards ?? throw new ArgumentNullException(nameof(cards));

    [HttpGet("{cardInstanceId:guid}/evaluation")]
    public IActionResult Evaluate(
        Guid combatId,
        Guid cardInstanceId,
        [FromQuery] string? actorId = null,
        [FromQuery] string[]? targetIds = null,
        [FromQuery] string? costOptionId = null)
    {
        var result = _cards.Inspect(new CardInspectionRequest
        {
            CombatId = combatId,
            CardInstanceId = cardInstanceId,
            ActorId = actorId,
            SelectedTargetIds = targetIds ?? [],
            CostOptionId = costOptionId
        });
        return result.IsSuccess
            ? Ok(result.Value)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Card evaluation failed", result.Error);
    }

    [HttpGet("evaluations")]
    public IActionResult EvaluateHand(
        Guid combatId,
        [FromQuery] string? actorId = null,
        [FromQuery] string[]? targetIds = null,
        [FromQuery] string? costOptionId = null)
    {
        var result = _cards.InspectHand(combatId, actorId, targetIds, costOptionId);
        return result.IsSuccess
            ? Ok(new { combatId, cards = result.Value })
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Card evaluation failed", result.Error);
    }
}
