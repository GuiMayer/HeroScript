using Core.Abstractions.Persistence;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Replay;

namespace API.Services;

public enum DailyChallengeErrorKind
{
    InvalidPlayer,
    ChallengeNotConfigured,
    ContentUnavailable,
    AttemptNotFound,
    OwnershipMismatch,
    RuleViolation
}

public sealed record DailyChallengeError(DailyChallengeErrorKind Kind, string Detail);

public sealed record DailyChallengeResult<T>(T? Value, DailyChallengeError? Error)
{
    public bool IsSuccess => Error == null;
    public static DailyChallengeResult<T> Success(T value) => new(value, null);
    public static DailyChallengeResult<T> Failure(DailyChallengeErrorKind kind, string detail) => new(default, new(kind, detail));
}

public sealed record DailyChallengeProof(
    DailyChallengeDefinition Challenge,
    string ContentRevision,
    string EngineVersion,
    string ProofHash);

public sealed record DailyAttempt(
    string ChallengeId,
    Guid RunId,
    int Sequence,
    ulong Step,
    ulong Seed,
    string ContentRevision,
    string StateHash);

public sealed record DailySubmission(
    string ChallengeId,
    string PlayerId,
    Guid RunId,
    int Sequence,
    string StateHash,
    int CommandsReplayed);

public sealed record DailyLeaderboardEntry(
    string PlayerId,
    Guid RunId,
    long Score,
    int Sequence,
    string StateHash);

/// <summary>
/// Application service for daily-challenge orchestration. It owns challenge lookup,
/// attempt recovery, replay verification and deterministic ranking; HTTP only maps its result.
/// </summary>
public sealed class DailyChallengeService
{
    private readonly IResourceCatalog<DailyChallengeDefinition> _challenges;
    private readonly IContentManifestProvider _content;
    private readonly IRunManager _runs;
    private readonly IRunCommitReader _repository;
    private readonly IRunReplayService _replay;

    public DailyChallengeService(
        IResourceCatalog<DailyChallengeDefinition> challenges,
        IContentManifestProvider content,
        IRunManager runs,
        IRunCommitReader repository,
        IRunReplayService replay)
    {
        _challenges = challenges;
        _content = content;
        _runs = runs;
        _repository = repository;
        _replay = replay;
    }

    public DailyChallengeResult<DailyChallengeProof> GetCurrent()
    {
        var challenge = ResolveCurrent();
        if (challenge == null)
            return DailyChallengeResult<DailyChallengeProof>.Failure(
                DailyChallengeErrorKind.ChallengeNotConfigured,
                "Current daily challenge is not configured");

        var manifest = _content.GetManifest(challenge.ConfigName);
        if (manifest.IsFailure)
        {
            return DailyChallengeResult<DailyChallengeProof>.Failure(
                DailyChallengeErrorKind.ContentUnavailable,
                manifest.Error);
        }

        var proof = new
        {
            challenge,
            contentRevision = manifest.Value.Revision,
            engineVersion = DeterministicContext.CurrentEngineVersion
        };
        return DailyChallengeResult<DailyChallengeProof>.Success(new DailyChallengeProof(
            challenge,
            manifest.Value.Revision,
            DeterministicContext.CurrentEngineVersion,
            CanonicalJson.ComputeHash(proof)));
    }

    public async Task<DailyChallengeResult<DailyAttempt>> StartAttemptAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 128)
        {
            return DailyChallengeResult<DailyAttempt>.Failure(
                DailyChallengeErrorKind.InvalidPlayer,
                "PlayerId is required");
        }

        var challenge = ResolveCurrent();
        if (challenge == null)
        {
            return DailyChallengeResult<DailyAttempt>.Failure(
                DailyChallengeErrorKind.ChallengeNotConfigured,
                "Current daily challenge is not configured");
        }

        var result = _runs.StartRun(new RunStartOptions(
            challenge.ConfigName,
            challenge.RunDefinitionId,
            playerId,
            challenge.Seed,
            ModeId: challenge.ModeId,
            ChallengeId: challenge.ChallengeId));
        if (result.IsSuccess)
            return DailyChallengeResult<DailyAttempt>.Success(MapAttempt(challenge, result.Value));

        if (result.Error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var runId in await _repository.ListRunIdsAsync(cancellationToken))
            {
                var existing = await _repository.LoadLatestStateAsync(runId, cancellationToken);
                if (existing != null &&
                    string.Equals(existing.PlayerEntityId, playerId, StringComparison.Ordinal) &&
                    string.Equals(existing.ChallengeId, challenge.ChallengeId, StringComparison.Ordinal))
                {
                    return DailyChallengeResult<DailyAttempt>.Success(MapAttempt(challenge, existing));
                }
            }
        }

        return DailyChallengeResult<DailyAttempt>.Failure(DailyChallengeErrorKind.RuleViolation, result.Error);
    }

    public async Task<DailyChallengeResult<DailySubmission>> SubmitAsync(
        Guid runId,
        string playerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 128)
        {
            return DailyChallengeResult<DailySubmission>.Failure(
                DailyChallengeErrorKind.InvalidPlayer,
                "PlayerId is required");
        }

        var challenge = ResolveCurrent();
        if (challenge == null)
        {
            return DailyChallengeResult<DailySubmission>.Failure(
                DailyChallengeErrorKind.ChallengeNotConfigured,
                "Current daily challenge is not configured");
        }

        var state = await _repository.LoadLatestStateAsync(runId, cancellationToken);
        if (state == null || !string.Equals(state.ChallengeId, challenge.ChallengeId, StringComparison.Ordinal))
        {
            return DailyChallengeResult<DailySubmission>.Failure(
                DailyChallengeErrorKind.AttemptNotFound,
                $"Daily attempt not found: {runId}");
        }
        if (!string.Equals(state.PlayerEntityId, playerId, StringComparison.Ordinal))
        {
            return DailyChallengeResult<DailySubmission>.Failure(
                DailyChallengeErrorKind.OwnershipMismatch,
                "The run does not belong to the submitted player");
        }

        var verification = await _replay.VerifyAsync(runId, cancellationToken);
        if (!verification.IsValid)
        {
            return DailyChallengeResult<DailySubmission>.Failure(
                DailyChallengeErrorKind.RuleViolation,
                string.Join("; ", verification.Errors));
        }

        return DailyChallengeResult<DailySubmission>.Success(new DailySubmission(
            challenge.ChallengeId,
            state.PlayerEntityId,
            state.RunId,
            state.Sequence,
            CanonicalJson.ComputeHash(state),
            verification.CommandsReplayed));
    }

    public async Task<DailyChallengeResult<IReadOnlyList<DailyLeaderboardEntry>>> GetLeaderboardAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var challenge = ResolveCurrent();
        if (challenge == null)
        {
            return DailyChallengeResult<IReadOnlyList<DailyLeaderboardEntry>>.Failure(
                DailyChallengeErrorKind.ChallengeNotConfigured,
                "Current daily challenge is not configured");
        }

        var entries = new List<DailyLeaderboardEntry>();
        foreach (var runId in await _repository.ListRunIdsAsync(cancellationToken))
        {
            var state = await _repository.LoadLatestStateAsync(runId, cancellationToken);
            if (state == null || !string.Equals(state.ChallengeId, challenge.ChallengeId, StringComparison.Ordinal) ||
                !IsCompleted(state))
                continue;
            var verification = await _replay.VerifyAsync(runId, cancellationToken);
            if (!verification.IsValid)
                continue;
            entries.Add(new DailyLeaderboardEntry(
                state.PlayerEntityId,
                state.RunId,
                CalculateScore(state, challenge),
                state.Sequence,
                CanonicalJson.ComputeHash(state)));
        }

        return DailyChallengeResult<IReadOnlyList<DailyLeaderboardEntry>>.Success(entries
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.RunId)
            .Take(limit)
            .ToArray());
    }

    public DailyChallengeDefinition? ResolveCurrent() => _challenges
        .GetAll("default")
        .Where(challenge => challenge.IsCurrent)
        .OrderBy(challenge => challenge.ChallengeId, StringComparer.Ordinal)
        .FirstOrDefault();

    private static long CalculateScore(RunState state, DailyChallengeDefinition challenge)
    {
        var score = challenge.ScoreResourceWeights.Sum(weight =>
            state.ResourceState.Current(weight.Key) * weight.Value);
        return checked((long)System.Math.Round(score, MidpointRounding.AwayFromZero));
    }

    private static DailyAttempt MapAttempt(DailyChallengeDefinition challenge, RunState state) => new(
        challenge.ChallengeId,
        state.RunId,
        state.Sequence,
        state.Determinism.Step,
        state.Determinism.Seed,
        state.Determinism.ContentRevision,
        CanonicalJson.ComputeHash(state));

    private static bool IsCompleted(RunState state)
    {
        var node = state.Map.Nodes.FirstOrDefault(item =>
            string.Equals(item.NodeId, state.CurrentNodeId, StringComparison.Ordinal));
        return state.ActiveEncounterId == null && node != null && node.NextNodeIds.Count == 0 &&
               state.Map.ResolvedNodeIds.Contains(node.NodeId, StringComparer.Ordinal);
    }
}
