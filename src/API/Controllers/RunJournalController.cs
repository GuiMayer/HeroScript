using API.Contracts;
using Core.Abstractions.Persistence;
using Core.Run;
using Core.Run.Replay;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}")]
public sealed class RunJournalController : BaseApiController
{
    private readonly IRunStateRepository _repository;
    private readonly IRunReplayService _replay;

    public RunJournalController(
        IRunStateRepository repository,
        IRunReplayService replay,
        ILogger<RunJournalController> logger)
        : base(logger)
    {
        _repository = repository;
        _replay = replay;
    }

    [HttpGet("journal")]
    public async Task<IActionResult> GetJournal(
        Guid runId,
        [FromQuery] int afterSequence = 0,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (afterSequence < 0 || limit is < 1 or > 1000)
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid journal cursor", "afterSequence must be non-negative and limit must be between 1 and 1000");
        if (_repository is not IRunCheckpointRepository checkpoints)
            return JournalUnavailable();

        var entries = await checkpoints.LoadJournalAsync(runId, afterSequence, limit, cancellationToken);
        if (entries.Count == 0 && await _repository.LoadLatestAsync(runId, cancellationToken) == null)
            return ApiNotFound($"Run journal not found: {runId}");

        return Ok(new
        {
            runId,
            afterSequence,
            lastSequence = entries.LastOrDefault()?.Sequence ?? afterSequence,
            returned = entries.Count,
            entries
        });
    }

    [HttpGet("checkpoints")]
    public async Task<IActionResult> GetCheckpoints(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (_repository is not IRunCheckpointRepository checkpoints)
            return JournalUnavailable();

        var journal = await checkpoints.LoadCheckpointsAsync(runId, cancellationToken);
        if (journal.Count == 0)
            return ApiNotFound($"Run checkpoints not found: {runId}");

        var retainedSnapshots = (await _repository.ListSnapshotsAsync(runId, cancellationToken)).ToHashSet();
        return Ok(new
        {
            runId,
            count = journal.Count,
            checkpoints = journal.Select(item => new
            {
                item.State.Sequence,
                item.JournalEntry.Step,
                item.JournalEntry.CommandId,
                item.JournalEntry.CommandType,
                item.JournalEntry.StateHash,
                item.JournalEntry.PreviousStateHash,
                item.JournalEntry.LogicalTimestamp,
                snapshotRetained = retainedSnapshots.Contains(item.State.Sequence)
            })
        });
    }

    [HttpGet("checkpoints/{sequence:int}")]
    public async Task<IActionResult> GetCheckpoint(
        Guid runId,
        int sequence,
        CancellationToken cancellationToken = default)
    {
        if (sequence < 1)
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid checkpoint", "Sequence must be positive");
        if (_repository is not IRunCheckpointRepository checkpoints)
            return JournalUnavailable();

        var checkpoint = (await checkpoints.LoadCheckpointsAsync(runId, cancellationToken))
            .FirstOrDefault(item => item.State.Sequence == sequence);
        return checkpoint == null
            ? ApiNotFound($"Run checkpoint not found: {runId}/{sequence}")
            : Ok(checkpoint);
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var verification = await _replay.VerifyAsync(runId, cancellationToken);
        return verification.Errors.Any(error => error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            ? ApiNotFound(verification.Errors[0])
            : Ok(verification);
    }

    private IActionResult JournalUnavailable() => ApiProblem(
        StatusCodes.Status503ServiceUnavailable,
        ApiErrorCodes.DependencyUnavailable,
        "Run journal unavailable",
        "The configured repository does not support durable journals");
}

