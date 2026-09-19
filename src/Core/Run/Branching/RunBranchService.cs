using System.Text.Json;
using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Determinism;

namespace Core.Run.Branching;

public sealed record RunBranchStartCommand(
    Guid ParentRunId,
    int SourceSequence,
    string BranchKey,
    string SourceStateHash,
    Guid? SourceCombatId = null,
    Guid RootRunId = default);

public sealed record RunBranchSummary(
    Guid RunId,
    Guid RootRunId,
    Guid ParentRunId,
    int SourceSequence,
    string SourceStateHash,
    string BranchKey,
    int Sequence,
    ulong Step,
    string StateHash,
    Guid? SourceCombatId);

public sealed record RunBranchTreeNode
{
    private ImmutableArray<RunBranchTreeNode> _children = [];

    public Guid RunId { get; init; }
    public Guid RootRunId { get; init; }
    public Guid? ParentRunId { get; init; }
    public Guid? SourceCombatId { get; init; }
    public int? SourceSequence { get; init; }
    public string? SourceStateHash { get; init; }
    public string? BranchKey { get; init; }
    public int Sequence { get; init; }
    public ulong Step { get; init; }
    public string StateHash { get; init; } = string.Empty;
    public IReadOnlyList<RunBranchTreeNode> Children
    {
        get => _children;
        init => _children = value?.ToImmutableArray() ?? [];
    }
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
    private readonly IRunCommitStore _repository;
    private readonly IRunLineageIndex _lineage;

    public RunBranchService(IRunCommitStore repository, IRunLineageIndex lineage)
    {
        _repository = repository;
        _lineage = lineage;
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

        var sourceCommit = await _repository.LoadCommitAsync(parentRunId, sourceSequence, cancellationToken)
            .ConfigureAwait(false);
        if (sourceCommit == null)
            return Result<RunState>.Failure($"Run commit not found: {parentRunId}/{sourceSequence}");
        var source = sourceCommit.RequireState();
        var policy = ValidateBranchPolicy(source);
        if (policy.IsFailure)
            return Result<RunState>.Failure(policy.Error);
        var parentLineage = await _lineage.GetAsync(parentRunId, cancellationToken).ConfigureAwait(false);
        if (parentLineage == null)
            return Result<RunState>.Failure($"Run lineage not found: {parentRunId}");
        var root = parentLineage.Lineage.RootRunId;
        var command = new RunBranchStartCommand(
            parentRunId,
            sourceSequence,
            branchKey.Trim(),
            sourceCommit.StateHash,
            source.ActiveEncounterId,
            root);
        var created = RunBranchTransitions.Create(source, command);
        if (created.IsFailure)
            return created;

        var existing = await _repository.LoadLatestStateAsync(created.Value.RunId, cancellationToken)
            .ConfigureAwait(false);
        if (existing != null)
        {
            return existing.Lineage?.ParentRunId == parentRunId &&
                   existing.Lineage.SourceSequence == sourceSequence &&
                   string.Equals(existing.Lineage.BranchKey, command.BranchKey, StringComparison.Ordinal)
                ? Result<RunState>.Success(existing)
                : Result<RunState>.Failure($"Run branch identity collision: {created.Value.RunId}");
        }

        var branchLimit = source.ResolvedMode?.CapabilityPolicy.MaxBranchesPerRoot;
        if (branchLimit.HasValue)
        {
            var branchCount = await _lineage.CountDescendantsAsync(root, cancellationToken).ConfigureAwait(false);
            if (branchCount >= branchLimit.Value)
                return Result<RunState>.Failure($"Game mode branch limit reached: {branchLimit.Value}");
        }

        var payload = JsonSerializer.SerializeToElement(command).Clone();
        var payloadHash = CanonicalJson.ComputeHash(payload);
        var identity = new RunCommandIdentity(
            DeterministicId.Create(
                created.Value.Determinism.Seed,
                created.Value.Determinism.Step,
                $"branch-command:{created.Value.RunId:N}:{payloadHash}"),
            RunCommandTypes.CreateBranchFromHistory,
            0,
            source.Determinism.Step,
            payloadHash);
        var activeCombat = created.Value.GetActiveEncounter()?.Combat;
        var frames = new[]
        {
            new RunCommitFrame
            {
                FrameIndex = 0,
                Step = created.Value.Determinism.Step,
                Scope = activeCombat == null ? "run" : "combat",
                Kind = RunCommandTypes.CreateBranchFromHistory,
                CombatId = activeCombat?.CombatId,
                ActorId = activeCombat?.ActivationState?.ActiveActorId,
                PhaseId = activeCombat?.PhaseState?.Cursor,
                Round = activeCombat?.ActivationState?.Round ?? activeCombat?.CurrentTurn,
                Activation = activeCombat?.ActivationState?.ActivationNumber,
                ResultHash = CanonicalJson.ComputeHash(created.Value),
                Resolution = payload
            }
        };
        var commit = new RunCommit
        {
            RunId = created.Value.RunId,
            Sequence = created.Value.Sequence,
            RootCommand = identity,
            Command = payload,
            PreviousStateHash = string.Empty,
            StateHash = CanonicalJson.ComputeHash(created.Value),
            BeforeStep = source.Determinism.Step,
            AfterStep = created.Value.Determinism.Step,
            LogicalTimestamp = created.Value.Determinism.LogicalTimestamp.UtcDateTime,
            StateAfter = created.Value,
            Lineage = created.Value.Lineage,
            Frames = frames,
            Facts = RunCommitFacts.FromFrames(frames)
        };
        try
        {
            await _repository.AppendAsync(commit, cancellationToken)
                .ConfigureAwait(false);
            await _lineage.IndexAsync(commit, cancellationToken).ConfigureAwait(false);
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
        foreach (var entry in await _lineage.GetChildrenAsync(
                     parentRunId,
                     includeInternalSimulations: false,
                     cancellationToken).ConfigureAwait(false))
        {
            var state = await _repository.LoadLatestStateAsync(entry.RunId, cancellationToken).ConfigureAwait(false);
            if (state == null)
                continue;
            branches.Add(new RunBranchSummary(
                state.RunId,
                entry.Lineage.RootRunId,
                parentRunId,
                entry.Lineage.SourceSequence!.Value,
                entry.Lineage.SourceStateHash!,
                entry.Lineage.BranchKey!,
                state.Sequence,
                state.Determinism.Step,
                CanonicalJson.ComputeHash(state),
                entry.Lineage.SourceCombatId));
        }
        return branches
            .OrderBy(branch => branch.SourceSequence)
            .ThenBy(branch => branch.BranchKey, StringComparer.Ordinal)
            .ThenBy(branch => branch.RunId)
            .ToArray();
    }

    public async Task<Result<RunBranchTreeNode>> GetTreeAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var selected = await _lineage.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (selected == null)
            return Result<RunBranchTreeNode>.Failure($"Run not found: {runId}");
        var rootId = selected.Lineage.RootRunId;
        var indexed = new[] { await _lineage.GetAsync(rootId, cancellationToken).ConfigureAwait(false) }
            .Where(entry => entry != null)
            .Cast<RunLineageEntry>()
            .Concat(await _lineage.GetDescendantsAsync(
                rootId,
                includeInternalSimulations: false,
                cancellationToken).ConfigureAwait(false))
            .ToDictionary(entry => entry.RunId);
        var states = new Dictionary<Guid, RunState>();
        foreach (var id in indexed.Keys.OrderBy(id => id))
        {
            var state = await _repository.LoadLatestStateAsync(id, cancellationToken).ConfigureAwait(false);
            if (state != null)
                states[state.RunId] = state;
        }
        if (!states.TryGetValue(rootId, out var root))
            return Result<RunBranchTreeNode>.Failure($"Branch root not found: {rootId}");

        RunBranchTreeNode Build(RunState node) => new()
        {
            RunId = node.RunId,
            RootRunId = node.Lineage!.RootRunId,
            ParentRunId = node.Lineage.ParentRunId,
            SourceCombatId = node.Lineage.SourceCombatId,
            SourceSequence = node.Lineage.SourceSequence,
            SourceStateHash = node.Lineage.SourceStateHash,
            BranchKey = node.Lineage.BranchKey,
            Sequence = node.Sequence,
            Step = node.Determinism.Step,
            StateHash = CanonicalJson.ComputeHash(node),
            Children = states.Values
                .Where(child => child.Lineage?.ParentRunId == node.RunId)
                .OrderBy(child => child.Lineage?.SourceSequence)
                .ThenBy(child => child.Lineage?.BranchKey, StringComparer.Ordinal)
                .ThenBy(child => child.RunId)
                .Select(Build)
                .ToArray()
        };
        return Result<RunBranchTreeNode>.Success(Build(root));
    }

    private static Result ValidateBranchPolicy(RunState source)
    {
        var mode = source.ResolvedMode;
        if (mode == null)
            return Result.Failure("Timeline branching requires a resolved game mode");
        if (!mode.CapabilityPolicy.AllowTimelineFork || !mode.ReplayPolicy.AllowForkFromHistory)
            return Result.Failure($"Game mode does not allow timeline branches: {source.ModeId}");
        return Result.Success();
    }
}

public static class RunBranchTransitions
{
    public static Result<RunState> Create(
        RunState source,
        RunBranchStartCommand command,
        RunBranchRebasePolicy? policy = null)
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
        policy ??= RunBranchRebasePolicy.Canonical;
        if (!policy.PreserveCardInstanceIds || !policy.PreserveRelicInstanceIds ||
            !policy.PreserveCompletedCombatIds || !policy.RebaseActiveCombatId ||
            !policy.RebaseRunScopedOwners || !policy.ClearInheritedCommandResolutions)
        {
            return Result<RunState>.Failure("Unsupported branch rebase policy");
        }
        var rootRunId = command.RootRunId != Guid.Empty
            ? command.RootRunId
            : source.Lineage?.RootRunId ?? source.RunId;
        if (source.Lineage != null && source.Lineage.RootRunId != rootRunId)
            return Result<RunState>.Failure("Branch root does not match the source lineage");
        var allocated = source.Determinism.AllocateId(
            $"branch:{source.RunId:N}:{command.SourceSequence}:{command.BranchKey}");
        var branchContext = allocated.Context;
        Guid? childCombatId = null;
        var encounters = source.Encounters
            .Select(encounter => encounter with
            {
                Combat = RebaseCombat(encounter.Combat, allocated.Value)
            })
            .ToArray();
        if (source.ActiveEncounterId is { } activeCombatId)
        {
            if (command.SourceCombatId != activeCombatId)
                return Result<RunState>.Failure("Branch combat anchor does not match the source commit");
            var index = Array.FindIndex(encounters, encounter => encounter.Combat.CombatId == activeCombatId);
            if (index < 0)
                return Result<RunState>.Failure("Active branch combat is missing from the source commit");
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
                    Determinism = allocatedCombat.Context,
                    ActivationState = combat.ActivationState is null
                        ? null
                        : combat.ActivationState with { RunId = allocated.Value }
                }
            };
            branchContext = branchContext.AllocateId($"branch-active-combat:{childCombatId.Value:N}").Context;
        }
        var lineage = RunLineage.Branch(
            rootRunId,
            source.RunId,
            source.Sequence,
            command.SourceStateHash,
            source.ActiveEncounterId,
            command.BranchKey);
        return Result<RunState>.Success(source with
        {
            RunId = allocated.Value,
            Lineage = lineage,
            Sequence = 1,
            ActiveEncounterId = childCombatId,
            Encounters = [.. encounters],
            ResourceState = source.ResourceState with { OwnerId = $"run:{allocated.Value}" },
            CardSelections = source.CardSelections
                .Select(selection => selection with { RunId = allocated.Value })
                .ToImmutableArray(),
            Shops = source.Shops
                .Select(shop => shop with { RunId = allocated.Value })
                .ToImmutableArray(),
            Preparations = source.Preparations
                .Select(preparation => RebasePreparation(preparation, source.RunId, allocated.Value))
                .ToImmutableArray(),
            Relics = source.Relics
                .Select(relic => relic.Owner.Kind == GameplayOwnerKind.Run
                    ? relic with { Owner = relic.Owner with { Id = allocated.Value.ToString() } }
                    : relic)
                .ToImmutableArray(),
            Modifiers = source.Modifiers
                .Select(modifier => RebaseModifier(modifier, source.RunId, allocated.Value))
                .ToImmutableArray(),
            Determinism = branchContext.AdvanceStep()
        });
    }

    private static CombatState RebaseCombat(CombatState combat, Guid runId) => combat with
    {
        RunId = runId,
        ActivationState = combat.ActivationState is null
            ? null
            : combat.ActivationState with { RunId = runId }
    };

    private static PreparationState RebasePreparation(
        PreparationState preparation,
        Guid sourceRunId,
        Guid branchRunId) => preparation with
        {
            RunId = branchRunId,
            Options = preparation.Options.Select(option => option with
            {
                ApplyModifiers = option.ApplyModifiers
                    .Select(grant => grant with
                    {
                        OwnerId = RebaseRunOwnerId(grant.OwnerId, sourceRunId, branchRunId)
                    })
                    .ToArray()
            }).ToArray()
        };

    private static ScriptModifierInstance RebaseModifier(
        ScriptModifierInstance modifier,
        Guid sourceRunId,
        Guid branchRunId)
    {
        if (modifier.Owner.Kind != GameplayOwnerKind.Run &&
            !string.Equals(modifier.OwnerId, $"run:{sourceRunId}", StringComparison.Ordinal))
            return modifier;
        return modifier with
        {
            Owner = modifier.Owner with { Kind = GameplayOwnerKind.Run, Id = branchRunId.ToString() },
            OwnerId = $"run:{branchRunId}"
        };
    }

    private static string RebaseRunOwnerId(string ownerId, Guid sourceRunId, Guid branchRunId) =>
        string.Equals(ownerId, "run", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ownerId, $"run:{sourceRunId}", StringComparison.Ordinal)
            ? $"run:{branchRunId}"
            : ownerId;
}
