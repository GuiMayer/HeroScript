using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Determinism;
using Core.Run.Projections;

namespace Core.Run.Sandbox;

public sealed record CombatTimelineItem
{
    public int RunSequence { get; init; }
    public ulong CombatStep { get; init; }
    public int Turn { get; init; }
    public int? Activation { get; init; }
    public string? Phase { get; init; }
    public string? ActorId { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public Guid? CommandId { get; init; }
    public Guid? RootCommandId { get; init; }
    public Guid? ResolutionCommandId { get; init; }
    public string? ResolutionFingerprint { get; init; }
    public string PreviousStateHash { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
    public bool StateAvailable { get; init; }
    public IReadOnlyList<RunCommitFrame> Frames { get; init; } = [];
    public IReadOnlyList<RunCommitFact> Facts { get; init; } = [];
    public JsonElement Summary { get; init; }
}

public sealed record CombatTimelineTurnGroup
{
    private ImmutableArray<CombatTimelineItem> _items = [];
    private ImmutableArray<CombatTimelineActivationGroup> _activations = [];

    public CombatTimelineTurnGroup(
        int turn,
        IReadOnlyList<CombatTimelineItem> items,
        IReadOnlyList<CombatTimelineActivationGroup>? activations = null)
    {
        Turn = turn;
        Items = items;
        Activations = activations ?? [];
    }

    public int Turn { get; init; }
    public IReadOnlyList<CombatTimelineItem> Items
    {
        get => _items;
        init => _items = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CombatTimelineActivationGroup> Activations
    {
        get => _activations;
        init => _activations = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CombatTimelineActivationGroup
{
    public int? Activation { get; init; }
    public string? ActorId { get; init; }
    public IReadOnlyList<CombatTimelinePhaseGroup> Phases { get; init; } = [];
    public IReadOnlyList<CombatTimelineItem> Items { get; init; } = [];
}

public sealed record CombatTimelinePhaseGroup
{
    public string? Phase { get; init; }
    public IReadOnlyList<CombatTimelineItem> Items { get; init; } = [];
}

public sealed record CombatTimelinePage
{
    private ImmutableArray<CombatTimelineItem> _items = [];
    private ImmutableArray<CombatTimelineTurnGroup> _turnGroups = [];

    public Guid RunId { get; init; }
    public Guid CombatId { get; init; }
    public int AfterSequence { get; init; }
    public int NextCursor { get; init; }
    public IReadOnlyList<CombatTimelineItem> Items
    {
        get => _items;
        init => _items = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CombatTimelineTurnGroup> TurnGroups
    {
        get => _turnGroups;
        init => _turnGroups = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CombatTimelineHistoricalState
{
    public Guid RunId { get; init; }
    public Guid CombatId { get; init; }
    public int RunSequence { get; init; }
    public string RunStateHash { get; init; } = string.Empty;
    public string CombatStateHash { get; init; } = string.Empty;
    public RunState Run { get; init; } = new();
    public Core.Combat.Models.CombatState Combat { get; init; } = new();
}

public interface ICombatTimelineProjectionService
{
    Task<Result<CombatTimelinePage>> GetAsync(
        Guid combatId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default);
    Task<Result<CombatTimelineHistoricalState>> GetHistoricalStateAsync(
        Guid combatId,
        int sequence,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Projects the append-only run journal into combat-tooling reads. It never
/// writes snapshots and never changes replay semantics.
/// </summary>
public sealed class CombatTimelineProjectionService : ICombatTimelineProjectionService
{
    private readonly IRunQueryService _runs;
    private readonly IRunCommitReader _commits;
    private readonly IRunCommitProjectionReader _projections;

    public CombatTimelineProjectionService(
        IRunQueryService runs,
        IRunCommitReader commits,
        IRunCommitProjectionReader projections)
    {
        _runs = runs;
        _commits = commits;
        _projections = projections;
    }

    public async Task<Result<CombatTimelinePage>> GetAsync(
        Guid combatId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (afterSequence < 0 || limit < 1)
            return Result<CombatTimelinePage>.Failure("Timeline cursor and limit are invalid");
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return Result<CombatTimelinePage>.Failure(run.Error);
        var access = ValidateTimelineAccess(run.Value, historical: false, limit);
        if (access.IsFailure)
            return Result<CombatTimelinePage>.Failure(access.Error);

        var pageLimit = System.Math.Min(limit, run.Value.ResolvedMode!.TimelinePolicy.MaxItemsPerPage);
        var items = (await _projections.ReadCombatAsync(
                run.Value.RunId,
                combatId,
                afterSequence,
                pageLimit,
                cancellationToken).ConfigureAwait(false))
            .Select(commit => Map(commit, combatId))
            .ToArray();
        var groups = run.Value.ResolvedMode!.TimelinePolicy.GroupByTurn
            ? items.GroupBy(item => item.Turn)
                .OrderBy(group => group.Key)
                .Select(group => new CombatTimelineTurnGroup(
                    group.Key,
                    group.ToArray(),
                    group.GroupBy(item => new { item.Activation, item.ActorId })
                        .OrderBy(activation => activation.Key.Activation)
                        .ThenBy(activation => activation.Key.ActorId, StringComparer.Ordinal)
                        .Select(activation => new CombatTimelineActivationGroup
                        {
                            Activation = activation.Key.Activation,
                            ActorId = activation.Key.ActorId,
                            Items = activation.ToArray(),
                            Phases = activation.GroupBy(item => item.Phase, StringComparer.Ordinal)
                                .OrderBy(phase => phase.Key, StringComparer.Ordinal)
                                .Select(phase => new CombatTimelinePhaseGroup
                                {
                                    Phase = phase.Key,
                                    Items = phase.ToArray()
                                })
                                .ToArray()
                        })
                        .ToArray()))
                .ToArray()
            : [];
        return Result<CombatTimelinePage>.Success(new CombatTimelinePage
        {
            RunId = run.Value.RunId,
            CombatId = combatId,
            AfterSequence = afterSequence,
            NextCursor = items.LastOrDefault()?.RunSequence ?? afterSequence,
            Items = items,
            TurnGroups = groups
        });
    }

    public async Task<Result<CombatTimelineHistoricalState>> GetHistoricalStateAsync(
        Guid combatId,
        int sequence,
        CancellationToken cancellationToken = default)
    {
        if (sequence < 1)
            return Result<CombatTimelineHistoricalState>.Failure("Timeline sequence must be positive");
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return Result<CombatTimelineHistoricalState>.Failure(run.Error);
        var access = ValidateTimelineAccess(run.Value, historical: true, limit: 1);
        if (access.IsFailure)
            return Result<CombatTimelineHistoricalState>.Failure(access.Error);
        var state = await _commits.LoadStateAsync(run.Value.RunId, sequence, cancellationToken).ConfigureAwait(false);
        var combat = state?.GetEncounter(combatId)?.Combat;
        if (state == null || combat == null)
            return Result<CombatTimelineHistoricalState>.Failure($"Combat timeline state not found: {combatId}/{sequence}");
        return Result<CombatTimelineHistoricalState>.Success(new CombatTimelineHistoricalState
        {
            RunId = state.RunId,
            CombatId = combatId,
            RunSequence = state.Sequence,
            RunStateHash = CanonicalJson.ComputeHash(state),
            CombatStateHash = CanonicalJson.ComputeHash(combat),
            Run = state,
            Combat = combat
        });
    }

    private static Result ValidateTimelineAccess(RunState run, bool historical, int limit)
    {
        var mode = run.ResolvedMode;
        if (mode == null || !mode.TimelinePolicy.Enabled ||
            mode.ReplayPolicy.TimelineAccess == TimelineAccessLevel.None)
        {
            return Result.Failure("Game mode does not expose a combat timeline");
        }
        if (limit > mode.TimelinePolicy.MaxItemsPerPage)
            return Result.Failure($"Timeline limit exceeds the game mode maximum: {mode.TimelinePolicy.MaxItemsPerPage}");
        if (historical && !mode.ReplayPolicy.AllowHistoricalInspection)
            return Result.Failure("Game mode does not allow historical combat inspection");
        return Result.Success();
    }

    private static CombatTimelineItem Map(
        RunCommit commit,
        Guid combatId)
    {
        var combat = commit.StateAfter.GetEncounter(combatId)!.Combat;
        var resolutionCommandId = commit.RootCommand.CommandId;
        var resolution = CombatResolutionProjection.FromCommit(commit);
        return new CombatTimelineItem
        {
            RunSequence = commit.Sequence,
            CombatStep = combat.Determinism.Step,
            Turn = combat.CurrentTurn,
            Activation = combat.ActivationState?.ActivationNumber,
            Phase = combat.PhaseState?.Cursor,
            ActorId = combat.ActivationState?.ActiveActorId,
            CommandType = commit.RootCommand.Type,
            CommandId = commit.RootCommand.CommandId,
            RootCommandId = commit.RootCommand.CommandId,
            ResolutionCommandId = resolution?.CommandId,
            ResolutionFingerprint = resolution?.ResolutionFingerprint,
            PreviousStateHash = commit.PreviousStateHash,
            StateHash = commit.StateHash,
            StateAvailable = true,
            Frames = commit.Frames
                .Where(frame => frame.CombatId == combatId)
                .OrderBy(frame => frame.FrameIndex)
                .ToArray(),
            Facts = commit.Facts
                .Where(fact => fact.CombatId == combatId)
                .OrderBy(fact => fact.FactIndex)
                .ToArray(),
            Summary = Summary(commit.Command)
        };
    }

    private static JsonElement Summary(JsonElement command)
    {
        if (command.ValueKind != JsonValueKind.Object)
            return JsonSerializer.SerializeToElement(new { });

        var selected = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var name in new[]
        {
            "combatId", "actorId", "actionType", "actionId", "cardInstanceId",
            "targetIds", "costOptionId", "consumedCardId", "destination",
            "participants", "initialResourceValues"
        })
        {
            if (TryGetProperty(command, name, out var value))
                selected[name] = value.Clone();
        }
        if (TryGetProperty(command, "command", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[]
            {
                "actorId", "actionType", "actionId", "cardInstanceId", "targetIds", "costOptionId"
            })
            {
                if (TryGetProperty(nested, name, out var value))
                    selected[name] = value.Clone();
            }
        }
        return JsonSerializer.SerializeToElement(selected);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
