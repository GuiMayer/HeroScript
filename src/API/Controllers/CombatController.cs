using Microsoft.AspNetCore.Mvc;
using API.Contracts;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using API.Models.Combat;
using Core.Combat.LegalActions;

namespace API.Controllers;

/// <summary>
/// Read-only combat projections and legality queries. Gameplay mutations are
/// owned exclusively by CombatCommandController and RunCommandController.
/// </summary>
[ApiController]
[Route("api/v1/combats")]
public class CombatController : BaseApiController
{
    private readonly ICombatRunCoordinator _combatRunCoordinator;
    private readonly ILegalActionQueryService _legalActions;

    public CombatController(
        ICombatRunCoordinator combatRunCoordinator,
        ILegalActionQueryService legalActions,
        ILogger<CombatController> logger)
        : base(logger)
    {
        _combatRunCoordinator = combatRunCoordinator ?? throw new ArgumentNullException(nameof(combatRunCoordinator));
        _legalActions = legalActions ?? throw new ArgumentNullException(nameof(legalActions));
    }

    [HttpGet("/api/v1/combats/{combatId:guid}/legal-actions")]
    public IActionResult GetLegalActions(Guid combatId, [FromQuery] string? actorId = null)
    {
        var result = _legalActions.Get(combatId, actorId);
        return result.IsSuccess
            ? Ok(result.Value)
            : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Legal action query failed", result.Error);
    }

    [HttpGet("/api/v1/runs/{runId:guid}/encounters/current")]
    public IActionResult GetCurrentRunEncounter(Guid runId)
    {
        try
        {
            var result = _combatRunCoordinator.GetCurrentEncounter(runId);
            return result.IsFailure
                ? NotFound(new { error = result.Error })
                : Ok(CombatStateResponse.From(result.Value.CombatState));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get current run encounter", runId.ToString());
        }
    }

    /// <summary>
    /// Obtém estado atual do combate.
    /// </summary>
    [HttpGet("/api/v1/combats/{combatId:guid}")]
    public IActionResult GetState(Guid combatId)
    {
        try
        {
            var result = GetCombatStateIncludingOwned(combatId);

            if (result.IsFailure)
                return NotFound(new { error = result.Error });

            return Ok(CombatStateResponse.From(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get combat state", combatId.ToString());
        }
    }

    /// <summary>
    /// Obtém histórico de ações do combate.
    /// </summary>
    [HttpGet("{combatId}/history")]
    public IActionResult GetHistory(Guid combatId)
    {
        try
        {
            var state = GetCombatStateIncludingOwned(combatId);
            if (state.IsFailure)
                return NotFound(new { error = state.Error });

            var actions = state.Value.ActionHistory;
            return Ok(new CombatHistoryResponse
            {
                CombatId = combatId,
                TotalActions = actions.Count,
                Actions = actions.Select(MapToActionDto).ToList()
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get combat history", combatId.ToString());
        }
    }

    private Result<CombatState> GetCombatStateIncludingOwned(Guid combatId)
    {
        var recovered = _combatRunCoordinator.GetCombatState(combatId);
        return recovered.IsSuccess
            ? Result<CombatState>.Success(recovered.Value.CombatState)
            : Result<CombatState>.Failure(recovered.Error);
    }

    private ActionDto MapToActionDto(CombatAction action)
    {
        return new ActionDto
        {
            ActionId = action.ActionId,
            Timestamp = action.Timestamp,
            Turn = action.Turn,
            ActorId = action.ActorId,
            ActionType = action.ActionType.ToString(),
            PowerId = action.PowerId,
            TargetId = action.TargetId,
            Applications = action.Applications.Select(application => new ActionApplicationDto
            {
                EffectInstanceId = application.EffectInstanceId,
                EffectType = application.EffectType.ToString(),
                TargetEntityId = application.TargetEntityId,
                ResourceId = application.ResourceId,
                ResourceField = application.ResourceField?.ToString(),
                ResourceOperation = application.ResourceOperation?.ToString(),
                PreviousValue = application.PreviousValue,
                CurrentValue = application.CurrentValue,
                SignedAmount = application.PreviousValue.HasValue && application.CurrentValue.HasValue
                    ? application.CurrentValue.Value - application.PreviousValue.Value
                    : null,
                StatusId = application.StatusId,
                StatusInstanceId = application.StatusInstanceId,
                ProvenanceKind = application.Provenance.Kind.ToString(),
                ProvenanceSourceId = application.Provenance.SourceId,
                ProvenanceComponentId = application.Provenance.ComponentId
            }).ToList()
        };
    }
}
