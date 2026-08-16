using API.Contracts;
using Core.Abstractions.Persistence;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Replay;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/challenges/daily/current")]
public sealed class DailyChallengeController : BaseApiController
{
    private readonly IResourceCatalog<DailyChallengeDefinition> _challenges;
    private readonly IContentManifestProvider _content;
    private readonly IRunManager _runs;
    private readonly IRunStateRepository _repository;
    private readonly IRunReplayService _replay;

    public DailyChallengeController(
        IResourceCatalog<DailyChallengeDefinition> challenges,
        IContentManifestProvider content,
        IRunManager runs,
        IRunStateRepository repository,
        IRunReplayService replay,
        ILogger<DailyChallengeController> logger)
        : base(logger)
    {
        _challenges = challenges;
        _content = content;
        _runs = runs;
        _repository = repository;
        _replay = replay;
    }

    [HttpGet]
    public IActionResult GetCurrent()
    {
        var challenge = ResolveCurrent();
        if (challenge == null)
            return ApiNotFound("Current daily challenge is not configured");
        var manifest = _content.GetManifest(challenge.ConfigName);
        if (manifest.IsFailure)
        {
            return ApiProblem(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.DependencyUnavailable,
                "Challenge content unavailable",
                manifest.Error);
        }

        var proof = new
        {
            challenge,
            contentRevision = manifest.Value.Revision,
            engineVersion = DeterministicContext.CurrentEngineVersion
        };
        return Ok(new
        {
            proof.challenge,
            proof.contentRevision,
            proof.engineVersion,
            proofHash = CanonicalJson.ComputeHash(proof)
        });
    }

    [HttpPost("attempts")]
    public async Task<IActionResult> StartAttempt(
        [FromBody] DailyAttemptRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlayerId) || request.PlayerId.Length > 128)
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Player is required", "PlayerId is required");
        var challenge = ResolveCurrent();
        if (challenge == null)
            return ApiNotFound("Current daily challenge is not configured");

        var result = _runs.StartRun(new RunStartOptions(
            challenge.ConfigName,
            challenge.RunDefinitionId,
            request.PlayerId,
            challenge.Seed,
            ModeId: challenge.ModeId,
            ChallengeId: challenge.ChallengeId));
        if (result.IsSuccess)
            return Ok(MapAttempt(challenge, result.Value));

        if (result.Error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var runId in await _repository.ListRunIdsAsync(cancellationToken))
            {
                var existing = await _repository.LoadLatestAsync(runId, cancellationToken);
                if (existing != null &&
                    string.Equals(existing.PlayerEntityId, request.PlayerId, StringComparison.Ordinal) &&
                    string.Equals(existing.ChallengeId, challenge.ChallengeId, StringComparison.Ordinal))
                {
                    return Ok(MapAttempt(challenge, existing));
                }
            }
        }

        return ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Daily attempt rejected",
                result.Error);
    }

    [HttpPost("submissions")]
    public async Task<IActionResult> Submit(
        [FromBody] DailySubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var challenge = ResolveCurrent();
        if (challenge == null)
            return ApiNotFound("Current daily challenge is not configured");
        if (string.IsNullOrWhiteSpace(request.PlayerId) || request.PlayerId.Length > 128)
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Player is required", "PlayerId is required");
        var state = await _repository.LoadLatestAsync(request.RunId, cancellationToken);
        if (state == null || !string.Equals(state.ChallengeId, challenge.ChallengeId, StringComparison.Ordinal))
            return ApiNotFound($"Daily attempt not found: {request.RunId}");
        if (!string.Equals(state.PlayerEntityId, request.PlayerId, StringComparison.Ordinal))
        {
            return ApiProblem(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.RuleViolation,
                "Attempt ownership mismatch",
                "The run does not belong to the submitted player");
        }

        var verification = await _replay.VerifyAsync(request.RunId, cancellationToken);
        return verification.IsValid
            ? Ok(new
            {
                accepted = true,
                challengeId = challenge.ChallengeId,
                state.PlayerEntityId,
                state.RunId,
                state.Sequence,
                stateHash = CanonicalJson.ComputeHash(state),
                verification.CommandsReplayed
            })
            : ApiProblem(
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.RuleViolation,
                "Daily submission rejected",
                string.Join("; ", verification.Errors));
    }

    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            return ApiBadRequest(ApiErrorCodes.InvalidRequest, "Invalid page size", "Limit must be between 1 and 500");
        var challenge = ResolveCurrent();
        if (challenge == null)
            return ApiNotFound("Current daily challenge is not configured");

        var entries = new List<DailyLeaderboardEntry>();
        foreach (var runId in await _repository.ListRunIdsAsync(cancellationToken))
        {
            var state = await _repository.LoadLatestAsync(runId, cancellationToken);
            if (state == null || !string.Equals(state.ChallengeId, challenge.ChallengeId, StringComparison.Ordinal) ||
                !IsCompleted(state))
                continue;
            var verification = await _replay.VerifyAsync(runId, cancellationToken);
            if (!verification.IsValid)
                continue;
            entries.Add(new DailyLeaderboardEntry(
                state.PlayerEntityId,
                state.RunId,
                checked((long)state.Gold + (long)state.PowerPoints * 10L),
                state.Sequence,
                CanonicalJson.ComputeHash(state)));
        }

        var ranked = entries
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.RunId)
            .Take(limit)
            .Select((entry, index) => new { rank = index + 1, entry })
            .ToArray();
        return Ok(new { challengeId = challenge.ChallengeId, entries = ranked });
    }

    private DailyChallengeDefinition? ResolveCurrent() => _challenges
        .GetAll("default")
        .Where(challenge => challenge.IsCurrent)
        .OrderBy(challenge => challenge.ChallengeId, StringComparer.Ordinal)
        .FirstOrDefault();

    private static object MapAttempt(DailyChallengeDefinition challenge, RunState state) => new
    {
        challengeId = challenge.ChallengeId,
        state.RunId,
        state.Sequence,
        state.Determinism.Step,
        state.Determinism.Seed,
        state.Determinism.ContentRevision,
        stateHash = CanonicalJson.ComputeHash(state)
    };

    private static bool IsCompleted(RunState state)
    {
        var node = state.Map.Nodes.FirstOrDefault(item =>
            string.Equals(item.NodeId, state.CurrentNodeId, StringComparison.Ordinal));
        return state.ActiveEncounterId == null && node != null && node.NextNodeIds.Count == 0 &&
               state.Map.ResolvedNodeIds.Contains(node.NodeId, StringComparer.Ordinal);
    }

    private sealed record DailyLeaderboardEntry(
        string PlayerId,
        Guid RunId,
        long Score,
        int Sequence,
        string StateHash);
}

public sealed record DailyAttemptRequest(string PlayerId);
public sealed record DailySubmissionRequest(Guid RunId, string PlayerId);
