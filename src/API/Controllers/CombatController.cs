using Microsoft.AspNetCore.Mvc;
using API.Contracts;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using API.Models.Combat;
using Core.Determinism;
using Core.Combat.TurnPhase;

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

    public CombatController(
        ICombatRunCoordinator combatRunCoordinator,
        ILogger<CombatController> logger)
        : base(logger)
    {
        _combatRunCoordinator = combatRunCoordinator ?? throw new ArgumentNullException(nameof(combatRunCoordinator));
    }

    [HttpGet("/api/v1/runs/{runId:guid}/encounters/current")]
    public IActionResult GetCurrentRunEncounter(Guid runId)
    {
        try
        {
            var result = _combatRunCoordinator.GetCurrentEncounter(runId);
            return result.IsFailure
                ? NotFound(new { error = result.Error })
                : Ok(MapToStateResponse(result.Value.CombatState));
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

            return Ok(MapToStateResponse(result.Value));
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

    [HttpGet("/api/v1/combats/{combatId:guid}/stack")]
    public IActionResult GetStack(Guid combatId)
    {
        var state = GetCombatStateIncludingOwned(combatId);
        if (state.IsFailure)
            return ApiNotFound(state.Error);
        var stack = state.Value.PhaseState?.ActionStack ?? new ActionStack();
        return Ok(new
        {
            combatId,
            phase = state.Value.PhaseState?.CurrentPhaseId,
            priorityActorId = state.Value.GetCurrentPriorityPlayer(),
            stack.IsResolving,
            stack.CurrentlyResolving,
            actions = stack.Actions
                .OrderByDescending(action => action.StackPosition)
                .ToArray()
        });
    }

    private Result<CombatState> GetCombatStateIncludingOwned(Guid combatId)
    {
        var recovered = _combatRunCoordinator.GetCombatState(combatId);
        return recovered.IsSuccess
            ? Result<CombatState>.Success(recovered.Value.CombatState)
            : Result<CombatState>.Failure(recovered.Error);
    }

    // Mappers
    private CombatStateResponse MapToStateResponse(CombatState state)
    {
        return new CombatStateResponse
        {
            CombatId = state.CombatId,
            RunId = state.RunId,
            RunNodeId = state.RunNodeId,
            Seed = state.Determinism.Seed,
            Step = state.Determinism.Step,
            ContentRevision = state.Determinism.ContentRevision,
            EngineVersion = state.Determinism.EngineVersion,
            StateHash = CanonicalJson.ComputeHash(state),
            Status = state.Status.ToString(),
            CurrentTurn = state.CurrentTurn,
            Hero = new HeroStateDto
            {
                EntityId = state.Hero.EntityId,
                Name = state.Hero.Name,
                IsAlive = state.Hero.IsAlive,
                Resources = MapResources(state.Hero)
            },
            Enemies = state.Enemies.Select(e => new EnemyStateDto
            {
                EntityId = e.EntityId,
                Name = e.Name,
                IsAlive = e.IsAlive,
                Resources = MapResources(e)
            }).ToList(),
            TotalActions = state.ActionHistory.Count,
            Board = state.Board,
            Phase = state.PhaseState,
            Activation = state.ActivationState
        };
    }

    private static IReadOnlyDictionary<string, ResourcePoolDto> MapResources(CombatEntity entity)
    {
        return entity.ResourceState.Resources
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                pair => pair.Key,
                pair => new ResourcePoolDto
                {
                    Current = pair.Value.Current,
                    Maximum = pair.Value.Maximum,
                    Minimum = pair.Value.Minimum
                },
                StringComparer.Ordinal);
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
