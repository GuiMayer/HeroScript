using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Determinism;
using Core.Run.Branching;
using Core.Run.Runtime;

namespace Core.Run.Replay;

public sealed record RunSemanticReplayVerification
{
    public Guid RunId { get; init; }
    public bool IsValid { get; init; }
    public bool Reexecuted { get; init; }
    public int CommandsReplayed { get; init; }
    public string ExpectedFinalHash { get; init; } = string.Empty;
    public string ActualFinalHash { get; init; } = string.Empty;
    public RunState? FinalState { get; init; }
    public ImmutableArray<string> Errors { get; init; } = [];
}

public interface IRunReplayService
{
    Task<RunSemanticReplayVerification> VerifyAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reexecutes authoritative commands through the normal gateway in an isolated
/// runtime produced by the same factory as live gameplay.
/// </summary>
public sealed class RunSemanticReplayService : IRunReplayService
{
    private readonly IRunCommitReader _commits;
    private readonly IGameplayRuntimeFactory _runtimeFactory;
    private readonly JsonSerializerOptions _jsonOptions;

    public RunSemanticReplayService(
        IRunCommitReader commits,
        IGameplayRuntimeFactory runtimeFactory)
    {
        _commits = commits;
        _runtimeFactory = runtimeFactory;
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<RunSemanticReplayVerification> VerifyAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var commits = (await _commits.LoadCommitsAsync(runId, cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(item => item.Sequence)
            .ToArray();
        if (commits.Length == 0)
            return Failure(runId, $"Run journal not found: {runId}");

        var structural = RunReplayVerifier.Verify(commits);
        if (!structural.IsValid)
            return Failure(runId, string.Join("; ", structural.Errors));

        var registeredTypes = _runtimeFactory.RegisteredCommandTypes;
        var unsupported = commits
            .Skip(1)
            .FirstOrDefault(commit =>
                !registeredTypes.Contains(commit.RootCommand.Type, StringComparer.Ordinal));
        if (unsupported != null)
        {
            return Failure(
                runId,
                $"No gameplay handler is registered for {unsupported.RootCommand.Type}");
        }

        var initial = await ResolveInitialStateAsync(commits[0], cancellationToken).ConfigureAwait(false);
        if (initial.IsFailure)
            return Failure(runId, initial.Error);

        var runtime = _runtimeFactory.Create(new GameplayRuntimeOptions(
            GameplayPersistenceMode.Ephemeral,
            HistoryReader: _commits));
        var errors = ImmutableArray.CreateBuilder<string>();
        RunState? current = null;
        var commandsReplayed = 0;

        for (var index = 0; index < commits.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commit = commits[index];
            var previousHash = current == null ? string.Empty : CanonicalJson.ComputeHash(current);
            if (!string.Equals(commit.PreviousStateHash, previousHash, StringComparison.Ordinal))
            {
                errors.Add($"Previous hash mismatch before sequence {commit.Sequence}");
                break;
            }

            var transition = index == 0
                ? initial.Value.StartOptions != null
                    ? ToReplayTransition(runtime.Runs.StartRun(initial.Value.StartOptions))
                    : ToReplayTransition(runtime.Runs.HydrateForReplay(initial.Value.BranchState!))
                : ReplayCommit(runtime, commit);
            if (transition.IsFailure)
            {
                errors.Add($"Sequence {commit.Sequence} ({commit.RootCommand.Type}) failed: {transition.Error}");
                break;
            }

            current = transition.Value.State;
            commandsReplayed++;
            ValidateResult(commit, current, transition.Value.Frames, errors);
            if (errors.Count > 0)
                break;
        }

        var expectedFinalHash = commits[^1].StateHash;
        var actualFinalHash = current == null ? string.Empty : CanonicalJson.ComputeHash(current);
        return new RunSemanticReplayVerification
        {
            RunId = runId,
            IsValid = errors.Count == 0 && commandsReplayed == commits.Length,
            Reexecuted = true,
            CommandsReplayed = commandsReplayed,
            ExpectedFinalHash = expectedFinalHash,
            ActualFinalHash = actualFinalHash,
            FinalState = current,
            Errors = errors.ToImmutable()
        };
    }

    private async Task<Result<ReplayInitialState>> ResolveInitialStateAsync(
        RunCommit first,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(first.RootCommand.Type, RunCommandTypes.StartRun, StringComparison.Ordinal))
            {
                return Result<ReplayInitialState>.Success(new ReplayInitialState(
                    Deserialize<RunStartOptions>(first.Command),
                    null));
            }
            if (!string.Equals(first.RootCommand.Type, RunCommandTypes.CreateBranchFromHistory, StringComparison.Ordinal))
                return Result<ReplayInitialState>.Failure(
                    $"Journal does not begin with {RunCommandTypes.StartRun} or {RunCommandTypes.CreateBranchFromHistory}");

            var command = Deserialize<RunBranchStartCommand>(first.Command);
            var source = await _commits.LoadStateAsync(
                    command.ParentRunId,
                    command.SourceSequence,
                    cancellationToken)
                .ConfigureAwait(false);
            if (source == null)
                return Result<ReplayInitialState>.Failure("Branch source commit is unavailable");
            var branch = RunBranchTransitions.Create(source, command);
            return branch.IsSuccess
                ? Result<ReplayInitialState>.Success(new ReplayInitialState(null, branch.Value))
                : Result<ReplayInitialState>.Failure(branch.Error);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Result<ReplayInitialState>.Failure(
                $"Invalid initial journal payload: {exception.Message}");
        }
    }

    private Result<ReplayTransition> ReplayCommit(GameplayRuntime runtime, RunCommit commit)
    {
        if (!runtime.RegisteredCommandTypes.Contains(commit.RootCommand.Type, StringComparer.Ordinal))
            return Result<ReplayTransition>.Failure(
                $"No gameplay handler is registered for {commit.RootCommand.Type}");

        var descriptor = GameplayCommandDescriptors.All.Single(item =>
            string.Equals(item.Type, commit.RootCommand.Type, StringComparison.Ordinal));
        var combatId = runtime.Runs.GetRun(commit.RunId).ValueOr(new RunState()).ActiveEncounterId
            ?? commit.Frames.Select(frame => frame.CombatId).FirstOrDefault(id => id.HasValue);
        var executed = runtime.Gateway.Execute(
            commit.RunId,
            new GameplayCommandEnvelope(commit.RootCommand, commit.Command),
            descriptor.Route is GameplayCommandRoute.Combat or GameplayCommandRoute.ResolveEncounter
                ? combatId
                : null);
        return executed.IsSuccess
            ? Result<ReplayTransition>.Success(new ReplayTransition(
                executed.Value.Receipt.State,
                executed.Value.Receipt.Frames))
            : Result<ReplayTransition>.Failure(executed.Error);
    }

    private static void ValidateResult(
        RunCommit expected,
        RunState actual,
        IReadOnlyList<RunCommitFrame> actualFrames,
        ImmutableArray<string>.Builder errors)
    {
        var actualHash = CanonicalJson.ComputeHash(actual);
        if (actual.Sequence != expected.Sequence)
            errors.Add($"Sequence mismatch: journal {expected.Sequence}, replay {actual.Sequence}");
        if (actual.Determinism.Step != expected.AfterStep)
            errors.Add(
                $"Step mismatch at sequence {expected.Sequence}: journal {expected.AfterStep}, replay {actual.Determinism.Step}");
        if (!string.Equals(expected.StateHash, actualHash, StringComparison.Ordinal))
            errors.Add($"State hash mismatch at sequence {expected.Sequence}");

        if (expected.Sequence == 1)
            return;
        if (expected.Frames.Count != actualFrames.Count)
        {
            errors.Add($"Frame count mismatch at sequence {expected.Sequence}");
            return;
        }

        for (var index = 0; index < expected.Frames.Count; index++)
        {
            var journaled = expected.Frames[index];
            var replayed = actualFrames[index];
            if (journaled.Step != replayed.Step ||
                journaled.CombatId != replayed.CombatId ||
                !string.Equals(journaled.Scope, replayed.Scope, StringComparison.Ordinal) ||
                !string.Equals(journaled.Kind, replayed.Kind, StringComparison.Ordinal) ||
                !string.Equals(journaled.ResultHash, replayed.ResultHash, StringComparison.Ordinal))
                errors.Add($"Frame hash mismatch at sequence {expected.Sequence}, frame {index}");
        }
    }

    private T Deserialize<T>(JsonElement element) =>
        element.Deserialize<T>(_jsonOptions)
        ?? throw new JsonException($"Could not deserialize {typeof(T).Name}");

    private static Result<ReplayTransition> ToReplayTransition(Result<RunState> state) =>
        state.IsSuccess
            ? Result<ReplayTransition>.Success(new ReplayTransition(state.Value, []))
            : Result<ReplayTransition>.Failure(state.Error);

    private static RunSemanticReplayVerification Failure(Guid runId, string error) => new()
    {
        RunId = runId,
        IsValid = false,
        Reexecuted = false,
        Errors = [error]
    };

    private sealed record ReplayInitialState(RunStartOptions? StartOptions, RunState? BranchState);
    private sealed record ReplayTransition(RunState State, IReadOnlyList<RunCommitFrame> Frames);
}
