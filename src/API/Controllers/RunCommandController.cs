using System.Text.Json;
using API.Contracts;
using Core.Combat;
using Core.Determinism;
using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Canonical mutation boundary for a persisted run aggregate.
/// </summary>
[ApiController]
[Route("api/v1/runs/{runId:guid}/commands")]
public sealed class RunCommandController : BaseApiController
{
    private readonly IRunCommandProcessor _commands;
    private readonly IRunManager _runs;
    private readonly ICombatRunCoordinator _combats;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public RunCommandController(
        IRunCommandProcessor commands,
        IRunManager runs,
        ICombatRunCoordinator combats,
        ILogger<RunCommandController> logger)
        : base(logger)
    {
        _commands = commands;
        _runs = runs;
        _combats = combats;
    }

    [HttpPost]
    public IActionResult Execute(Guid runId, [FromBody] CommandEnvelope envelope)
    {
        if (!envelope.ExpectedSequence.HasValue || !envelope.ExpectedStep.HasValue)
        {
            return ApiBadRequest(
                ApiErrorCodes.InvalidRequest,
                "Command version is required",
                "ExpectedSequence and ExpectedStep are required for run commands");
        }

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
            if (type == RunCommandTypes.StartEncounter)
                return ExecuteStartEncounter(runId, identity, payload);
            if (type == RunCommandTypes.ResolveCombat)
                return ExecuteResolveEncounter(runId, identity, payload);

            var result = _commands.Execute(runId, new RunCommand(identity, payload));
            return result.IsSuccess
                ? Ok(MapReceipt(result.Value))
                : MapFailure(runId, result.Error, useCombatStep: false);
        }
        catch (JsonException exception)
        {
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid command payload", exception.Message);
        }
        catch (Exception exception)
        {
            return HandleException(exception, "execute run command", runId.ToString());
        }
    }

    private IActionResult ExecuteStartEncounter(
        Guid runId,
        RunCommandIdentity identity,
        JsonElement payload)
    {
        var existing = _commands.FindReceipt(runId, identity.CommandId);
        if (existing.IsFailure)
            return MapFailure(runId, existing.Error, useCombatStep: false);
        var request = payload.Deserialize<StartEncounterPayload>(_jsonOptions)
            ?? throw new JsonException("START_ENCOUNTER payload is required");
        var result = _combats.StartEncounter(
            runId,
            request.HeroId,
            request.EnemyIds,
            request.InitialEnergy,
            identity);
        if (result.IsFailure)
            return MapFailure(runId, result.Error, useCombatStep: false);

        return ReceiptResponse(runId, identity.CommandId, existing.Value != null);
    }

    private IActionResult ExecuteResolveEncounter(
        Guid runId,
        RunCommandIdentity identity,
        JsonElement payload)
    {
        var existing = _commands.FindReceipt(runId, identity.CommandId);
        if (existing.IsFailure)
            return MapFailure(runId, existing.Error, useCombatStep: true);
        var request = payload.Deserialize<ResolveEncounterPayload>(_jsonOptions)
            ?? throw new JsonException("RESOLVE_COMBAT payload is required");
        var result = _combats.ResolveEncounter(runId, request.CombatId, identity);
        if (result.IsFailure)
            return MapFailure(runId, result.Error, useCombatStep: true);

        return ReceiptResponse(runId, identity.CommandId, existing.Value != null);
    }

    private IActionResult ReceiptResponse(Guid runId, Guid commandId, bool duplicate)
    {
        var receipt = _commands.FindReceipt(runId, commandId);
        return receipt.IsSuccess && receipt.Value != null
            ? Ok(MapReceipt(receipt.Value, duplicate: duplicate))
            : ApiProblem(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.DependencyUnavailable,
                "Command receipt unavailable",
                receipt.IsFailure ? receipt.Error : "The transition did not create a durable receipt");
    }

    private IActionResult MapFailure(Guid runId, string error, bool useCombatStep)
    {
        var current = _runs.GetRun(runId);
        if (error.StartsWith(RunCommandErrors.VersionConflictPrefix, StringComparison.Ordinal))
        {
            var currentStep = current.IsSuccess
                ? useCombatStep
                    ? current.Value.GetActiveEncounter()?.Combat.Determinism.Step ?? current.Value.Determinism.Step
                    : current.Value.Determinism.Step
                : (ulong?)null;
            return ApiProblem(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.VersionConflict,
                "Aggregate version conflict",
                error[RunCommandErrors.VersionConflictPrefix.Length..].Trim(),
                current.IsSuccess ? current.Value.Sequence : null,
                currentStep);
        }

        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return ApiNotFound(error);

        return ApiProblem(
            StatusCodes.Status422UnprocessableEntity,
            ApiErrorCodes.RuleViolation,
            "Command rejected",
            error,
            current.IsSuccess ? current.Value.Sequence : null,
            current.IsSuccess ? current.Value.Determinism.Step : null);
    }

    internal static object MapReceipt(
        RunCommandReceipt receipt,
        object? state = null,
        bool? duplicate = null,
        ulong? step = null)
    {
        return new
        {
            receipt.CommandId,
            type = receipt.CommandType,
            receipt.Sequence,
            step = step ?? receipt.Step,
            receipt.PreviousStateHash,
            receipt.StateHash,
            duplicate = duplicate ?? receipt.Duplicate,
            state = state ?? receipt.State,
            events = Array.Empty<object>()
        };
    }

    private sealed record StartEncounterPayload(
        string HeroId,
        IReadOnlyList<string> EnemyIds,
        int InitialEnergy = 3);

    private sealed record ResolveEncounterPayload(Guid CombatId);
}
