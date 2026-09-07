using API.Contracts;
using Core.Abstractions.Persistence;
using Core.Run;
using Core.Run.Projections;
using Core.Run.Replay;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/runs/{runId:guid}")]
public sealed class RunJournalController : BaseApiController
{
    private readonly IRunCommitReader _commits;
    private readonly IRunCommitProjectionReader _projections;
    private readonly IRunReplayService _replay;

    public RunJournalController(
        IRunCommitReader commits,
        IRunCommitProjectionReader projections,
        IRunReplayService replay,
        ILogger<RunJournalController> logger)
        : base(logger)
    {
        _commits = commits;
        _projections = projections;
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
        var entries = (await _projections.ReadRunAsync(runId, afterSequence, limit, cancellationToken))
            .Select(commit => commit.ToJournalEntry())
            .ToArray();
        if (entries.Length == 0 && await _commits.LoadLatestStateAsync(runId, cancellationToken) == null)
            return ApiNotFound($"Run journal not found: {runId}");

        return Ok(new
        {
            runId,
            afterSequence,
            lastSequence = entries.LastOrDefault()?.Sequence ?? afterSequence,
            returned = entries.Length,
            entries
        });
    }

    [HttpGet("commits")]
    public async Task<IActionResult> GetCommits(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var journal = await _projections.ReadRunAsync(runId, 0, int.MaxValue, cancellationToken);
        if (journal.Count == 0)
            return ApiNotFound($"Run commits not found: {runId}");

        return Ok(new
        {
            runId,
            count = journal.Count,
            commits = journal.Select(commit => new
            {
                commit.Sequence,
                step = commit.AfterStep,
                commandId = commit.RootCommand.CommandId,
                commandType = commit.RootCommand.Type,
                commit.StateHash,
                commit.PreviousStateHash,
                commit.LogicalTimestamp,
                frameCount = commit.Frames.Count,
                factCount = commit.Facts.Count
            })
        });
    }

    [HttpGet("commits/{sequence:int}")]
    public async Task<IActionResult> GetCommit(
        Guid runId,
        int sequence,
        CancellationToken cancellationToken = default)
    {
        if (sequence < 1)
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid commit", "Sequence must be positive");

        var commit = await _commits.LoadCommitAsync(runId, sequence, cancellationToken);
        return commit == null
            ? ApiNotFound($"Run commit not found: {runId}/{sequence}")
            : Ok(commit);
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline(
        Guid runId,
        [FromQuery] int afterSequence = 0,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (afterSequence < 0 || limit is < 1 or > 1000)
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid timeline cursor", "afterSequence must be non-negative and limit must be between 1 and 1000");
        var items = (await _projections.ReadRunAsync(runId, afterSequence, limit, cancellationToken))
            .Select(commit => new
            {
                commit.Sequence,
                step = commit.AfterStep,
                commandId = commit.RootCommand.CommandId,
                commandType = commit.RootCommand.Type,
                commit.LogicalTimestamp,
                commit.PreviousStateHash,
                commit.StateHash,
                frames = commit.Frames
            })
            .ToArray();
        if (items.Length == 0 && await _commits.LoadLatestStateAsync(runId, cancellationToken) == null)
            return ApiNotFound($"Run timeline not found: {runId}");
        return Ok(new
        {
            runId,
            afterSequence,
            returned = items.Length,
            nextCursor = items.LastOrDefault()?.Sequence ?? afterSequence,
            items
        });
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

}
