using Core.Abstractions.Persistence;
using Core.Common;

namespace Core.Run.Projections;

public static class CombatResolutionProjection
{
    public static CombatResolutionRecord? FromCommit(RunCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.CombatResolution is not { } header)
            return null;

        var frames = commit.Frames
            .Where(frame => frame.CombatId == header.CombatId)
            .OrderBy(frame => frame.FrameIndex)
            .Select(frame => new CombatAnimationFrame
            {
                FrameId = frame.ResolutionFrameId ?? Guid.Empty,
                Index = frame.FrameIndex,
                RunSequence = commit.Sequence,
                CombatStep = frame.CombatStep ?? frame.Step,
                TransitionType = frame.Kind,
                Payload = frame.Resolution,
                StateAfter = frame.CombatStateAfter,
                SnapshotSequence = frame.SnapshotSequence ?? commit.Sequence,
                EffectSteps = frame.EffectSteps,
                Calculations = frame.Calculations,
                Applications = frame.Applications,
                CardZoneSteps = frame.CardZoneSteps
            })
            .ToArray();

        return new CombatResolutionRecord
        {
            CommandId = header.CommandId,
            CombatId = header.CombatId,
            CommandType = header.CommandType,
            Mode = header.Mode,
            RootSequence = header.RootSequence,
            InitialCombatStateHash = header.InitialCombatStateHash,
            FinalCombatStateHash = header.FinalCombatStateHash,
            ResolutionFingerprint = header.ResolutionFingerprint,
            Frames = frames
        };
    }
}

public interface ICombatResolutionReader
{
    Task<Result<CombatResolutionRecord?>> GetAsync(
        Guid runId,
        Guid commandId,
        CancellationToken cancellationToken = default);
}

public sealed class CombatResolutionReader : ICombatResolutionReader
{
    private readonly IRunCommitReader _commits;

    public CombatResolutionReader(IRunCommitReader commits)
    {
        _commits = commits ?? throw new ArgumentNullException(nameof(commits));
    }

    public async Task<Result<CombatResolutionRecord?>> GetAsync(
        Guid runId,
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty || commandId == Guid.Empty)
            return Result<CombatResolutionRecord?>.Failure("Run id and command id are required");

        try
        {
            var commit = await _commits.FindCommandAsync(runId, commandId, cancellationToken)
                .ConfigureAwait(false);
            return Result<CombatResolutionRecord?>.Success(
                commit == null ? null : CombatResolutionProjection.FromCommit(commit));
        }
        catch (Exception exception)
        {
            return Result<CombatResolutionRecord?>.Failure(
                $"Failed to read combat resolution {commandId}: {exception.Message}",
                exception);
        }
    }
}
