using API.Contracts;
using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/challenges/daily/current")]
public sealed class DailyChallengeController : BaseApiController
{
    private readonly DailyChallengeService _dailyChallenges;

    public DailyChallengeController(
        DailyChallengeService dailyChallenges,
        ILogger<DailyChallengeController> logger)
        : base(logger)
    {
        _dailyChallenges = dailyChallenges;
    }

    [HttpGet]
    public IActionResult GetCurrent()
    {
        var result = _dailyChallenges.GetCurrent();
        if (!result.IsSuccess)
            return MapFailure(result.Error!);

        var proof = result.Value!;
        return Ok(new
        {
            challenge = proof.Challenge,
            proof.ContentRevision,
            proof.EngineVersion,
            proofHash = proof.ProofHash
        });
    }

    [HttpPost("attempts")]
    public async Task<IActionResult> StartAttempt(
        [FromBody] DailyAttemptRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _dailyChallenges.StartAttemptAsync(request.PlayerId, cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result.Error!);

        var attempt = result.Value!;
        return Ok(new
        {
            attempt.ChallengeId,
            attempt.RunId,
            attempt.Sequence,
            attempt.Step,
            attempt.Seed,
            attempt.ContentRevision,
            stateHash = attempt.StateHash
        });
    }

    [HttpPost("submissions")]
    public async Task<IActionResult> Submit(
        [FromBody] DailySubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _dailyChallenges.SubmitAsync(request.RunId, request.PlayerId, cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result.Error!);

        var submission = result.Value!;
        return Ok(new
        {
            accepted = true,
            submission.ChallengeId,
            playerEntityId = submission.PlayerId,
            submission.RunId,
            submission.Sequence,
            stateHash = submission.StateHash,
            submission.CommandsReplayed
        });
    }

    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid page size", "Limit must be between 1 and 500");

        var result = await _dailyChallenges.GetLeaderboardAsync(limit, cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result.Error!);

        var challenge = _dailyChallenges.ResolveCurrent()!;
        var entries = result.Value!
            .Select((entry, index) => new { rank = index + 1, entry })
            .ToArray();
        return Ok(new { challengeId = challenge.ChallengeId, entries });
    }

    private IActionResult MapFailure(DailyChallengeError error)
    {
        return error.Kind switch
        {
            DailyChallengeErrorKind.InvalidPlayer => ApiBadRequest(
                ApiErrorCodes.InvalidRequest,
                "Player is required",
                error.Detail),
            DailyChallengeErrorKind.ChallengeNotConfigured or DailyChallengeErrorKind.AttemptNotFound => ApiNotFound(error.Detail),
            DailyChallengeErrorKind.ContentUnavailable => ApiProblem(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.DependencyUnavailable,
                "Challenge content unavailable",
                error.Detail),
            DailyChallengeErrorKind.OwnershipMismatch => ApiProblem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.RuleViolation,
                "Attempt ownership mismatch",
                error.Detail),
            _ => ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Daily challenge request rejected",
                error.Detail)
        };
    }
}

public sealed record DailyAttemptRequest(string PlayerId);
public sealed record DailySubmissionRequest(Guid RunId, string PlayerId);
