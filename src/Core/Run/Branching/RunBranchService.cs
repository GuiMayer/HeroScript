using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Determinism;

namespace Core.Run.Branching;

public sealed record RunBranchStartCommand(
    Guid ParentRunId,
    int SourceSequence,
    string BranchKey,
    string SourceStateHash);

public sealed record RunBranchSummary(
    Guid RunId,
    Guid ParentRunId,
    int SourceSequence,
    string BranchKey,
    int Sequence,
    ulong Step,
    string StateHash);

public interface IRunBranchService
{
    Task<Result<RunState>> CreateAsync(
        Guid parentRunId,
        int sourceSequence,
        string branchKey,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunBranchSummary>> ListAsync(
        Guid parentRunId,
        CancellationToken cancellationToken = default);
}

public sealed class RunBranchService : IRunBranchService
{
    private readonly IRunCheckpointRepository _repository;

    public RunBranchService(IRunCheckpointRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<RunState>> CreateAsync(
        Guid parentRunId,
        int sourceSequence,
        string branchKey,
        CancellationToken cancellationToken = default)
    {
        if (parentRunId == Guid.Empty)
            return Result<RunState>.Failure("Parent run id is required");
        if (sourceSequence < 1)
            return Result<RunState>.Failure("Source sequence must be positive");
        if (string.IsNullOrWhiteSpace(branchKey))
            return Result<RunState>.Failure("Branch key is required");
        if (branchKey.Length > 128)
            return Result<RunState>.Failure("Branch key cannot exceed 128 characters");

        var source = await _repository.LoadAsync(parentRunId, sourceSequence, cancellationToken)
            .ConfigureAwait(false);
        if (source == null)
            return Result<RunState>.Failure($"Run checkpoint not found: {parentRunId}/{sourceSequence}");
        var command = new RunBranchStartCommand(
            parentRunId,
            sourceSequence,
            branchKey.Trim(),
            CanonicalJson.ComputeHash(source));
        var created = RunBranchTransitions.Create(source, command);
        if (created.IsFailure)
            return created;

        var existing = await _repository.LoadLatestAsync(created.Value.RunId, cancellationToken)
            .ConfigureAwait(false);
        if (existing != null)
        {
            return existing.ParentRunId == parentRunId &&
                   existing.BranchFromSequence == sourceSequence &&
                   string.Equals(existing.BranchKey, command.BranchKey, StringComparison.Ordinal)
                ? Result<RunState>.Success(existing)
                : Result<RunState>.Failure($"Run branch identity collision: {created.Value.RunId}");
        }

        var payload = JsonSerializer.SerializeToElement(command).Clone();
        var entry = new RunJournalEntry
        {
            RunId = created.Value.RunId,
            Sequence = created.Value.Sequence,
            Step = created.Value.Determinism.Step,
            CommandType = "run.branch.start",
            Command = payload,
            PreviousStateHash = string.Empty,
            StateHash = CanonicalJson.ComputeHash(created.Value),
            LogicalTimestamp = created.Value.Determinism.LogicalTimestamp.UtcDateTime
        };
        try
        {
            await _repository.SaveCheckpointAsync(new RunCheckpoint(created.Value, entry), cancellationToken)
                .ConfigureAwait(false);
            return created;
        }
        catch (Exception exception)
        {
            return Result<RunState>.Failure($"Failed to persist run branch: {exception.Message}", exception);
        }
    }

    public async Task<IReadOnlyList<RunBranchSummary>> ListAsync(
        Guid parentRunId,
        CancellationToken cancellationToken = default)
    {
        var branches = new List<RunBranchSummary>();
        foreach (var runId in await _repository.ListRunIdsAsync(cancellationToken).ConfigureAwait(false))
        {
            var state = await _repository.LoadLatestAsync(runId, cancellationToken).ConfigureAwait(false);
            if (state?.ParentRunId != parentRunId || state.BranchFromSequence == null || state.BranchKey == null ||
                state.BranchKey.StartsWith("simulation:", StringComparison.Ordinal))
                continue;
            branches.Add(new RunBranchSummary(
                state.RunId,
                parentRunId,
                state.BranchFromSequence.Value,
                state.BranchKey,
                state.Sequence,
                state.Determinism.Step,
                CanonicalJson.ComputeHash(state)));
        }
        return branches.OrderBy(branch => branch.RunId).ToArray();
    }
}

public static class RunBranchTransitions
{
    public static Result<RunState> Create(RunState source, RunBranchStartCommand command)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(command);
        if (source.RunId != command.ParentRunId || source.Sequence != command.SourceSequence)
            return Result<RunState>.Failure("Branch source identity does not match its command");
        if (string.IsNullOrWhiteSpace(command.BranchKey))
            return Result<RunState>.Failure("Branch key is required");
        if (command.BranchKey.Length > 128)
            return Result<RunState>.Failure("Branch key cannot exceed 128 characters");
        if (!string.Equals(CanonicalJson.ComputeHash(source), command.SourceStateHash, StringComparison.Ordinal))
            return Result<RunState>.Failure("Branch source state hash mismatch");
        if (source.ActiveEncounterId != null)
            return Result<RunState>.Failure("Cannot branch while a run encounter is active");

        var allocated = source.Determinism.AllocateId(
            $"branch:{source.RunId:N}:{command.SourceSequence}:{command.BranchKey}");
        var encounters = source.Encounters
            .Select(encounter => encounter with
            {
                Combat = encounter.Combat with { RunId = allocated.Value }
            })
            .ToArray();
        return Result<RunState>.Success(source with
        {
            RunId = allocated.Value,
            ParentRunId = source.RunId,
            BranchFromSequence = source.Sequence,
            BranchKey = command.BranchKey,
            Sequence = 1,
            Encounters = [.. encounters],
            Determinism = allocated.Context.AdvanceStep()
        });
    }
}
