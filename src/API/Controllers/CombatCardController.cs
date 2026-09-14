using API.Contracts;
using API.Services;
using Core.Run;
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
    private readonly IRunQueryService _runs;
    private readonly IToolAccessPolicy _toolAccess;

    public CombatCardController(
        ICardInspectionService cards,
        IRunQueryService runs,
        IToolAccessPolicy toolAccess,
        ILogger<CombatCardController> logger)
        : base(logger)
    {
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _runs = runs;
        _toolAccess = toolAccess;
    }

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
            CostOptionId = costOptionId,
            MaximumDetail = MaximumDetail(combatId)
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
        var result = _cards.InspectHand(combatId, actorId, targetIds, costOptionId, MaximumDetail(combatId));
        return result.IsSuccess
            ? Ok(new { combatId, cards = result.Value })
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Card evaluation failed", result.Error);
    }

    private InspectionDetailLevel MaximumDetail(Guid combatId)
    {
        var run = _runs.GetRunByCombat(combatId);
        return run.IsSuccess && _toolAccess.Allows(run.Value, ToolCapabilities.CardInspectFull)
            ? InspectionDetailLevel.Full
            : InspectionDetailLevel.Resolved;
    }
}
