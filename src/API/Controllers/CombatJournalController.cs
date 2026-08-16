using API.Contracts;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Run;
using Core.Run.Replay;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/combats/{combatId:guid}")]
public sealed class CombatJournalController : BaseApiController
{
    private readonly IRunManager _runs;
    private readonly IRunStateRepository _repository;
    private readonly IRunReplayService _replay;

    public CombatJournalController(
        IRunManager runs,
        IRunStateRepository repository,
        IRunReplayService replay,
        ILogger<CombatJournalController> logger)
        : base(logger)
    {
        _runs = runs;
        _repository = repository;
        _replay = replay;
    }

    [HttpGet("journal")]
    public async Task<IActionResult> GetJournal(
        Guid combatId,
        [FromQuery] int afterSequence = 0,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (afterSequence < 0 || limit is < 1 or > 1000)
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid journal cursor", "afterSequence must be non-negative and limit must be between 1 and 1000");
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);
        if (_repository is not IRunCheckpointRepository checkpoints)
            return JournalUnavailable();

        var entries = (await checkpoints.LoadCheckpointsAsync(run.Value.RunId, cancellationToken))
            .Where(item => item.JournalEntry.Sequence > afterSequence)
            .Where(item => item.State.GetEncounter(combatId) != null)
            .Where(item => IsCombatCommand(item.JournalEntry.CommandType))
            .OrderBy(item => item.JournalEntry.Sequence)
            .Take(limit)
            .Select(item => item.JournalEntry)
            .ToArray();
        return Ok(new
        {
            combatId,
            runId = run.Value.RunId,
            afterSequence,
            lastSequence = entries.LastOrDefault()?.Sequence ?? afterSequence,
            returned = entries.Length,
            entries
        });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify(
        Guid combatId,
        CancellationToken cancellationToken = default)
    {
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);

        var verification = await _replay.VerifyAsync(run.Value.RunId, cancellationToken);
        var expectedCombat = run.Value.GetEncounter(combatId)?.Combat;
        var actualCombat = verification.FinalState?.GetEncounter(combatId)?.Combat;
        var expectedHash = expectedCombat == null ? string.Empty : CanonicalJson.ComputeHash(expectedCombat);
        var actualHash = actualCombat == null ? string.Empty : CanonicalJson.ComputeHash(actualCombat);
        return Ok(new
        {
            combatId,
            runId = run.Value.RunId,
            isValid = verification.IsValid &&
                      !string.IsNullOrEmpty(expectedHash) &&
                      string.Equals(expectedHash, actualHash, StringComparison.Ordinal),
            verification.Reexecuted,
            verification.CommandsReplayed,
            expectedStateHash = expectedHash,
            actualStateHash = actualHash,
            verification.Errors
        });
    }

    private static bool IsCombatCommand(string type) => type is
        RunCommandTypes.StartEncounter or
        RunCommandTypes.ResolveCombat or
        "COMBAT_ACTION" or
        "EXECUTE_ACTION" or
        "END_TURN";

    private IActionResult JournalUnavailable() => ApiProblem(
        StatusCodes.Status503ServiceUnavailable,
        ApiErrorCodes.DependencyUnavailable,
        "Combat journal unavailable",
        "The configured repository does not support durable journals");
}

