using System.Text.Json;
using API.Contracts;
using Core.Combat.Models;
using Core.Determinism;
using Core.Run;
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
    private readonly IRunManager _runs;
    private readonly IGameplayCommandGateway _commands;

    public CombatCommandController(
        IRunManager runs,
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

        var type = envelope.Type.Trim().ToUpperInvariant();
        if (type is not GameplayCommandTypes.PlayCard and
            not GameplayCommandTypes.ExecuteAction and
            not GameplayCommandTypes.EndTurn)
        {
            return ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Command rejected",
                $"Unsupported combat command type: {type}",
                runResult.Value.Sequence,
                runResult.Value.GetEncounter(combatId)?.Combat.Determinism.Step);
        }
        var payload = envelope.Payload.ValueKind == JsonValueKind.Undefined
            ? JsonSerializer.SerializeToElement(new { })
            : envelope.Payload.Clone();
        var identity = new RunCommandIdentity(
            envelope.CommandId,
            type,
            envelope.ExpectedSequence.Value,
            envelope.ExpectedStep.Value,
            CanonicalJson.ComputeHash(payload));

        try
        {
            var result = _commands.Execute(
                runResult.Value.RunId,
                new RunCommand(identity, payload),
                combatId);
            if (result.IsFailure)
                return MapFailure(runResult.Value.RunId, combatId, result.Error);
            var receipt = result.Value.Receipt;
            var encounter = receipt.State.GetEncounter(combatId);
            return Ok(RunCommandController.MapReceipt(
                receipt,
                new
                {
                    run = receipt.State,
                    combat = encounter?.Combat,
                    resolution = receipt.State.GetCombatResolution(receipt.CommandId)
                },
                step: encounter?.Combat.Determinism.Step));
        }
        catch (JsonException exception)
        {
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid command payload", exception.Message);
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
