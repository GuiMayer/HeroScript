using API.Contracts;
using API.Services;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Run;
using Core.Run.Projections;
using Core.Run.Replay;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/combats/{combatId:guid}")]
public sealed class CombatJournalController : BaseApiController
{
    private readonly IRunQueryService _runs;
    private readonly IRunCommitProjectionReader _commits;
    private readonly IRunReplayService _replay;
    private readonly IToolAccessPolicy _toolAccess;

    public CombatJournalController(
        IRunQueryService runs,
        IRunCommitProjectionReader commits,
        IRunReplayService replay,
        IToolAccessPolicy toolAccess,
        ILogger<CombatJournalController> logger)
        : base(logger)
    {
        _runs = runs;
        _commits = commits;
        _replay = replay;
        _toolAccess = toolAccess;
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
        if (!_toolAccess.Allows(run.Value, ToolCapabilities.TimelineRead))
            return ToolDenied(ToolCapabilities.TimelineRead);
        var entries = (await _commits.ReadCombatAsync(
                run.Value.RunId,
                combatId,
                afterSequence,
                limit,
                cancellationToken))
            .Select(commit => commit.ToJournalEntry())
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
        if (!_toolAccess.Allows(run.Value, ToolCapabilities.ReplayVerify))
            return ToolDenied(ToolCapabilities.ReplayVerify);

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

    private IActionResult ToolDenied(string capability) => ApiProblem(
        StatusCodes.Status403Forbidden,
        ApiErrorCodes.Forbidden,
        "Tool capability denied",
        $"The active access profile and game mode do not allow '{capability}'");
}
