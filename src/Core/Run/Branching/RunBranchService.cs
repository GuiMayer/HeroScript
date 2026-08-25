using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Determinism;

namespace Core.Run.Branching;

public sealed record RunBranchStartCommand(
    Guid ParentRunId,
    int SourceSequence,
    string BranchKey,
    string SourceStateHash,
    Guid? SourceCombatId = null);

public sealed record RunBranchSummary(
    Guid RunId,
    Guid ParentRunId,
    int SourceSequence,
    string BranchKey,
    int Sequence,
    ulong Step,
    string StateHash,
    Guid? SourceCombatId);

public sealed record RunBranchTreeNode
{
    public Guid RunId { get; init; }
    public Guid? ParentRunId { get; init; }
    public Guid? ParentCombatId { get; init; }
    public int? SourceSequence { get; init; }
    public string? BranchKey { get; init; }
    public int Sequence { get; init; }
    public ulong Step { get; init; }
    public string StateHash { get; init; } = string.Empty;
    public IReadOnlyList<RunBranchTreeNode> Children { get; init; } = [];
}

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
    Task<Result<RunBranchTreeNode>> GetTreeAsync(
        Guid runId,
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
        var policy = ValidateBranchPolicy(source);
        if (policy.IsFailure)
            return Result<RunState>.Failure(policy.Error);
        var root = await ResolveRootIdAsync(source, cancellationToken).ConfigureAwait(false);
        var branchLimit = source.ResolvedMode?.CapabilityPolicy.MaxBranchesPerRoot;
        if (branchLimit.HasValue)
        {
            var branchCount = await CountBranchesForRootAsync(root, cancellationToken).ConfigureAwait(false);
            if (branchCount >= branchLimit.Value)
                return Result<RunState>.Failure($"Game mode branch limit reached: {branchLimit.Value}");
        }
        var command = new RunBranchStartCommand(
            parentRunId,
            sourceSequence,
            branchKey.Trim(),
            CanonicalJson.ComputeHash(source),
            source.ActiveEncounterId);
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
                CanonicalJson.ComputeHash(state),
                state.ParentCombatId));
        }
        return branches.OrderBy(branch => branch.RunId).ToArray();
    }

    public async Task<Result<RunBranchTreeNode>> GetTreeAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var selected = await _repository.LoadLatestAsync(runId, cancellationToken).ConfigureAwait(false);
        if (selected == null)
            return Result<RunBranchTreeNode>.Failure($"Run not found: {runId}");
        var states = new Dictionary<Guid, RunState>();
        foreach (var id in await _repository.ListRunIdsAsync(cancellationToken).ConfigureAwait(false))
        {
            var state = await _repository.LoadLatestAsync(id, cancellationToken).ConfigureAwait(false);
            if (state != null)
                states[state.RunId] = state;
        }
        var rootId = selected.RunId;
        while (states.TryGetValue(rootId, out var state) && state.ParentRunId is { } parent)
            rootId = parent;
        if (!states.TryGetValue(rootId, out var root))
            return Result<RunBranchTreeNode>.Failure($"Branch root not found: {rootId}");

        RunBranchTreeNode Build(RunState node) => new()
        {
            RunId = node.RunId,
            ParentRunId = node.ParentRunId,
            ParentCombatId = node.ParentCombatId,
            SourceSequence = node.BranchFromSequence,
            BranchKey = node.BranchKey,
            Sequence = node.Sequence,
            Step = node.Determinism.Step,
            StateHash = CanonicalJson.ComputeHash(node),
            Children = states.Values
                .Where(child => child.ParentRunId == node.RunId)
                .OrderBy(child => child.BranchFromSequence)
                .ThenBy(child => child.BranchKey, StringComparer.Ordinal)
                .ThenBy(child => child.RunId)
                .Select(Build)
                .ToArray()
        };
        return Result<RunBranchTreeNode>.Success(Build(root));
    }

    private async Task<Guid> ResolveRootIdAsync(RunState source, CancellationToken cancellationToken)
    {
        var current = source;
        while (current.ParentRunId is { } parent)
        {
            var parentState = await _repository.LoadLatestAsync(parent, cancellationToken).ConfigureAwait(false);
            if (parentState == null)
                break;
            current = parentState;
        }
        return current.RunId;
    }

    private async Task<int> CountBranchesForRootAsync(Guid rootId, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var runId in await _repository.ListRunIdsAsync(cancellationToken).ConfigureAwait(false))
        {
            var state = await _repository.LoadLatestAsync(runId, cancellationToken).ConfigureAwait(false);
            if (state?.ParentRunId == null)
                continue;
            if (await ResolveRootIdAsync(state, cancellationToken).ConfigureAwait(false) == rootId)
                count++;
        }
        return count;
    }

    private static Result ValidateBranchPolicy(RunState source)
    {
        var mode = source.ResolvedMode;
        if (mode == null)
            return Result.Success(); // Compatibility for pre-policy persisted runs only.
        if (!mode.CapabilityPolicy.AllowTimelineFork || !mode.ReplayPolicy.AllowForkFromHistory)
            return Result.Failure($"Game mode does not allow timeline branches: {source.ModeId}");
        return Result.Success();
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
        var allocated = source.Determinism.AllocateId(
            $"branch:{source.RunId:N}:{command.SourceSequence}:{command.BranchKey}");
        var branchContext = allocated.Context;
        Guid? childCombatId = null;
        var encounters = source.Encounters
            .Select(encounter => encounter with
            {
                Combat = encounter.Combat with { RunId = allocated.Value }
            })
            .ToArray();
        if (source.ActiveEncounterId is { } activeCombatId)
        {
            if (command.SourceCombatId != activeCombatId)
                return Result<RunState>.Failure("Branch combat anchor does not match the source checkpoint");
            var index = Array.FindIndex(encounters, encounter => encounter.Combat.CombatId == activeCombatId);
            if (index < 0)
                return Result<RunState>.Failure("Active branch combat is missing from the source checkpoint");
            var combat = encounters[index].Combat;
            var allocatedCombat = combat.Determinism.AllocateId(
                $"branch:{allocated.Value:N}:{command.SourceSequence}:{command.BranchKey}");
            childCombatId = allocatedCombat.Value;
            encounters[index] = encounters[index] with
            {
                Combat = combat with
                {
                    CombatId = childCombatId.Value,
                    RunId = allocated.Value,
                    Determinism = allocatedCombat.Context
                }
            };
            branchContext = branchContext.AllocateId($"branch-active-combat:{childCombatId.Value:N}").Context;
        }
        return Result<RunState>.Success(source with
        {
            RunId = allocated.Value,
            ParentRunId = source.RunId,
            ParentCombatId = source.ActiveEncounterId,
            BranchFromSequence = source.Sequence,
            BranchKey = command.BranchKey,
            Sequence = 1,
            ActiveEncounterId = childCombatId,
            Encounters = [.. encounters],
            Determinism = branchContext.AdvanceStep()
        });
    }
}
