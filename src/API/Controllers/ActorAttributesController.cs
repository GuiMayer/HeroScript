using Core.Calculations;
using Core.Combat;
using Core.Run;
using Microsoft.AspNetCore.Mvc;
using API.Contracts;

namespace API.Controllers;

[ApiController]
public sealed class ActorAttributesController(IRunManager runs, ICombatRunCoordinator combats,
    IActorAttributeProjectionService attributes, ILogger<ActorAttributesController> logger) : BaseApiController(logger)
{
    [HttpGet("/api/v1/runs/{runId:guid}/character-attributes")]
    public IActionResult GetCharacter(Guid runId)
    {
        var loaded = runs.GetRun(runId);
        if (loaded.IsFailure) return ApiNotFound(loaded.Error);
        if (loaded.Value.ActiveEncounterId is { } combatId)
        {
            var encounter = combats.GetCombatState(combatId);
            if (encounter.IsFailure) return ApiNotFound(encounter.Error);
            var active = attributes.Project(encounter.Value.RunState, encounter.Value.CombatState, loaded.Value.PlayerEntityId);
            return active.IsSuccess ? Ok(active.Value) : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Attribute query failed", active.Error);
        }
        var result = attributes.Project(loaded.Value, null, loaded.Value.PlayerEntityId);
        return result.IsSuccess ? Ok(result.Value) : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Attribute query failed", result.Error);
    }

    [HttpGet("/api/v1/combats/{combatId:guid}/actors/{actorId}/attributes")]
    public IActionResult GetActor(Guid combatId, string actorId)
    {
        var encounter = combats.GetCombatState(combatId);
        if (encounter.IsFailure) return ApiNotFound(encounter.Error);
        var result = attributes.Project(encounter.Value.RunState, encounter.Value.CombatState, actorId);
        return result.IsSuccess ? Ok(result.Value) : ApiBadRequest(ApiErrorCodes.InvalidOperation, "Attribute query failed", result.Error);
    }
}
