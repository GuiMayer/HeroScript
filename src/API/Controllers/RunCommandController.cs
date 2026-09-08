using API.Contracts;
using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Canonical mutation boundary for a persisted run aggregate.
/// </summary>
[ApiController]
[Route("api/v1/runs/{runId:guid}/commands")]
[Produces("application/json", "application/problem+json")]
public sealed class RunCommandController : BaseApiController
{
    private readonly IGameplayCommandGateway _commands;
    private readonly IRunQueryService _runs;

    public RunCommandController(
        IGameplayCommandGateway commands,
        IRunQueryService runs,
        ILogger<RunCommandController> logger)
        : base(logger)
    {
        _commands = commands;
        _runs = runs;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CommandResultEnvelope<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public IActionResult Execute(Guid runId, [FromBody] CommandEnvelope envelope)
    {
        if (!envelope.ExpectedSequence.HasValue || !envelope.ExpectedStep.HasValue)
        {
            return ApiBadRequest(
                ApiErrorCodes.InvalidRequest,
                "Command version is required",
                "ExpectedSequence and ExpectedStep are required for run commands");
        }

        var identity = new RunCommandIdentity(
            envelope.CommandId,
            envelope.Type,
            envelope.ExpectedSequence.Value,
            envelope.ExpectedStep.Value);

        try
        {
            var result = _commands.Execute(
                runId,
                new GameplayCommandEnvelope(identity, envelope.Payload));
            return result.IsSuccess
                ? Ok(MapReceipt(result.Value.Receipt))
                : MapFailure(
                    runId,
                    result.Error,
                    useCombatStep: string.Equals(
                        envelope.Type.Trim(),
                        RunCommandTypes.ResolveCombat,
                        StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            return HandleException(exception, "execute run command", runId.ToString());
        }
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
}
