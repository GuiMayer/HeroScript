using API.Contracts;
using Core.Run;
using API.Models.Combat;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Canonical mutation boundary for a combat owned by a run.
/// </summary>
[ApiController]
[Route("api/v1/combats/{combatId:guid}/commands")]
[Produces("application/json", "application/problem+json")]
public sealed class CombatCommandController : BaseApiController
{
    private readonly IRunQueryService _runs;
    private readonly IGameplayCommandGateway _commands;

    public CombatCommandController(
        IRunQueryService runs,
        IGameplayCommandGateway commands,
        ILogger<CombatCommandController> logger)
        : base(logger)
    {
        _runs = runs;
        _commands = commands;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CommandResultEnvelope<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public IActionResult Execute(Guid combatId, [FromBody] CommandEnvelope envelope)
    {
        if (!envelope.ExpectedSequence.HasValue || !envelope.ExpectedStep.HasValue)
        {
            return ApiBadRequest(
                ApiErrorCodes.InvalidRequest,
                "Command version is required",
                "ExpectedSequence and ExpectedStep are required for combat commands");
        }

        var runResult = _runs.GetRunByCombat(combatId);
        if (runResult.IsFailure)
            return ApiNotFound(runResult.Error);

        var identity = new RunCommandIdentity(
            envelope.CommandId,
            envelope.Type,
            envelope.ExpectedSequence.Value,
            envelope.ExpectedStep.Value);

        try
        {
            var result = _commands.Execute(
                runResult.Value.RunId,
                new GameplayCommandEnvelope(identity, envelope.Payload),
                combatId);
            if (result.IsFailure)
                return MapFailure(runResult.Value.RunId, combatId, result.Error);
            var receipt = result.Value.Receipt;
            var encounter = receipt.State.GetEncounter(combatId);
            return Ok(RunCommandController.MapReceipt(
                receipt,
                new
                {
                    // The run aggregate is available from GET /runs/{id} and
                    // was the largest duplicate in every combat response.
                    // Keep only the concurrency token needed by advanced
                    // clients plus the combat/read-model and animation data.
                    runStep = receipt.State.Determinism.Step,
                    combat = encounter == null ? null : CombatStateResponse.From(encounter.Combat),
                    resolution = receipt.CombatResolution
                },
                step: encounter?.Combat.Determinism.Step));
        }
        catch (Exception exception)
        {
            return HandleException(exception, "execute combat command", combatId.ToString());
        }
    }

    private IActionResult MapFailure(Guid runId, Guid combatId, string error)
    {
        var current = _runs.GetRun(runId);
        if (error.StartsWith(RunCommandErrors.VersionConflictPrefix, StringComparison.Ordinal))
        {
            return ApiProblem(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.VersionConflict,
                "Aggregate version conflict",
                error[RunCommandErrors.VersionConflictPrefix.Length..].Trim(),
                current.IsSuccess ? current.Value.Sequence : null,
                current.IsSuccess ? current.Value.GetEncounter(combatId)?.Combat.Determinism.Step : null);
        }

        return ApiProblem(
            StatusCodes.Status422UnprocessableEntity,
            ApiErrorCodes.RuleViolation,
            "Command rejected",
            error,
            current.IsSuccess ? current.Value.Sequence : null,
            current.IsSuccess ? current.Value.GetEncounter(combatId)?.Combat.Determinism.Step : null);
    }
}
