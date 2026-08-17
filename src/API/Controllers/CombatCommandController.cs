using System.Text.Json;
using API.Contracts;
using Core.Combat;
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
    private const string ExecuteActionType = "EXECUTE_ACTION";
    private const string EndTurnType = "END_TURN";

    private readonly IRunManager _runs;
    private readonly IRunCommandProcessor _commands;
    private readonly ICombatRunCoordinator _combats;
    private readonly IActionManager _actions;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public CombatCommandController(
        IRunManager runs,
        IRunCommandProcessor commands,
        ICombatRunCoordinator combats,
        IActionManager actions,
        ILogger<CombatCommandController> logger)
        : base(logger)
    {
        _runs = runs;
        _commands = commands;
        _combats = combats;
        _actions = actions;
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
            var existing = _commands.FindReceipt(runResult.Value.RunId, envelope.CommandId);
            if (existing.IsFailure)
                return MapFailure(runResult.Value.RunId, combatId, existing.Error);
            var commandResult = BuildCommand(type, payload, runResult.Value, combatId);
            if (commandResult.IsFailure)
            {
                return ApiProblem(
                    StatusCodes.Status422UnprocessableEntity,
                    ApiErrorCodes.RuleViolation,
                    "Command rejected",
                    commandResult.Error,
                    runResult.Value.Sequence,
                    runResult.Value.GetEncounter(combatId)?.Combat.Determinism.Step);
            }

            var result = _combats.ExecuteAction(combatId, commandResult.Value, identity);
            if (result.IsFailure)
                return MapFailure(runResult.Value.RunId, combatId, result.Error);

            var receipt = _commands.FindReceipt(runResult.Value.RunId, envelope.CommandId);
            if (receipt.IsFailure || receipt.Value == null)
            {
                return ApiProblem(
                    StatusCodes.Status503ServiceUnavailable,
                    ApiErrorCodes.DependencyUnavailable,
                    "Command receipt unavailable",
                    receipt.IsFailure ? receipt.Error : "The transition did not create a durable receipt");
            }

            var encounter = receipt.Value.State.GetEncounter(combatId);
            return Ok(RunCommandController.MapReceipt(
                receipt.Value,
                new { run = receipt.Value.State, combat = encounter?.Combat },
                existing.Value != null,
                encounter?.Combat.Determinism.Step));
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

    private Core.Common.Result<CombatActionCommand> BuildCommand(
        string type,
        JsonElement payload,
        RunState run,
        Guid combatId)
    {
        var combat = run.GetEncounter(combatId)?.Combat;
        if (combat == null)
            return Core.Common.Result<CombatActionCommand>.Failure($"Combat not found: {combatId}");

        var request = payload.Deserialize<CombatCommandPayload>(_jsonOptions) ?? new CombatCommandPayload();
        if (type == EndTurnType)
        {
            return Core.Common.Result<CombatActionCommand>.Success(new CombatActionCommand
            {
                ActorId = request.ActorId ?? combat.Hero.EntityId,
                ActionType = ActionType.END_TURN,
                RunId = run.RunId
            });
        }
        if (type != ExecuteActionType)
            return Core.Common.Result<CombatActionCommand>.Failure($"Unsupported combat command type: {type}");

        var actionType = request.ActionType;
        var powerId = request.PowerId;
        if (!string.IsNullOrWhiteSpace(request.ActionId))
        {
            var definition = _actions.GetDefinition(request.ActionId);
            if (definition.IsFailure)
                return Core.Common.Result<CombatActionCommand>.Failure(definition.Error);
            actionType = definition.Value.ActionType == ActionType.BASIC_ATTACK
                ? ActionType.BASIC_ATTACK
                : ActionType.POWER;
            powerId = actionType == ActionType.POWER ? definition.Value.ActionId : null;
        }

        if (!actionType.HasValue)
            return Core.Common.Result<CombatActionCommand>.Failure("ActionId or ActionType is required");

        return Core.Common.Result<CombatActionCommand>.Success(new CombatActionCommand
        {
            ActorId = request.ActorId ?? combat.Hero.EntityId,
            ActionType = actionType.Value,
            PowerId = powerId,
            TargetId = request.TargetId,
            CostOptionId = request.CostOptionId,
            CardId = request.CardId,
            RunId = run.RunId
        });
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

    private sealed record CombatCommandPayload(
        string? ActorId = null,
        string? ActionId = null,
        ActionType? ActionType = null,
        string? PowerId = null,
        string? TargetId = null,
        string? CostOptionId = null,
        string? CardId = null);
}
