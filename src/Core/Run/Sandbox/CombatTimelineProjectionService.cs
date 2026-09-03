using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Determinism;

namespace Core.Run.Sandbox;

public sealed record CombatTimelineItem
{
    public int RunSequence { get; init; }
    public ulong CombatStep { get; init; }
    public int Turn { get; init; }
    public string? Phase { get; init; }
    public string? ActorId { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public Guid? CommandId { get; init; }
    public string PreviousStateHash { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
    public bool SnapshotAvailable { get; init; }
    public JsonElement Summary { get; init; }
}

public sealed record CombatTimelineTurnGroup
{
    private ImmutableArray<CombatTimelineItem> _items = [];

    public CombatTimelineTurnGroup(int turn, IReadOnlyList<CombatTimelineItem> items)
    {
        Turn = turn;
        Items = items;
    }

    public int Turn { get; init; }
    public IReadOnlyList<CombatTimelineItem> Items
    {
        get => _items;
        init => _items = value?.ToImmutableArray() ?? [];
    }
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
    private readonly IRunManager _runs;
    private readonly IRunCheckpointRepository _repository;

    public CombatTimelineProjectionService(IRunManager runs, IRunCheckpointRepository repository)
    {
        _runs = runs;
        _repository = repository;
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

        var retained = (await _repository.ListSnapshotsAsync(run.Value.RunId, cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();
        var pageLimit = System.Math.Min(limit, run.Value.ResolvedMode!.TimelinePolicy.MaxItemsPerPage);
        var items = (await _repository.LoadCheckpointsAsync(run.Value.RunId, cancellationToken)
                .ConfigureAwait(false))
            .Where(checkpoint => checkpoint.State.Sequence > afterSequence)
            .Where(checkpoint => checkpoint.State.GetEncounter(combatId) != null)
            .Where(checkpoint => IsCombatTimelineEntry(checkpoint.JournalEntry.CommandType))
            .OrderBy(checkpoint => checkpoint.State.Sequence)
            .Take(pageLimit)
            .Select(checkpoint => Map(checkpoint, combatId, retained.Contains(checkpoint.State.Sequence)))
            .ToArray();
        var groups = run.Value.ResolvedMode!.TimelinePolicy.GroupByTurn
            ? items.GroupBy(item => item.Turn)
                .OrderBy(group => group.Key)
                .Select(group => new CombatTimelineTurnGroup(group.Key, group.ToArray()))
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
        var state = await _repository.LoadAsync(run.Value.RunId, sequence, cancellationToken).ConfigureAwait(false);
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
            string.Equals(mode.ReplayPolicy.TimelineAccess, "none", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("Game mode does not expose a combat timeline");
        }
        if (limit > mode.TimelinePolicy.MaxItemsPerPage)
            return Result.Failure($"Timeline limit exceeds the game mode maximum: {mode.TimelinePolicy.MaxItemsPerPage}");
        if (historical && !mode.ReplayPolicy.AllowHistoricalInspection)
            return Result.Failure("Game mode does not allow historical combat inspection");
        return Result.Success();
    }

    private static bool IsCombatTimelineEntry(string commandType) => commandType is
        RunCommandTypes.StartEncounter or
        RunCommandTypes.ResolveCombat or
        "COMBAT_ACTION" or
        "PLAY_CARD" or
        "EXECUTE_ACTION" or
        "END_TURN";

    private static CombatTimelineItem Map(RunCheckpoint checkpoint, Guid combatId, bool snapshotAvailable)
    {
        var combat = checkpoint.State.GetEncounter(combatId)!.Combat;
        return new CombatTimelineItem
        {
            RunSequence = checkpoint.State.Sequence,
            CombatStep = combat.Determinism.Step,
            Turn = combat.CurrentTurn,
            Phase = combat.PhaseState?.CurrentPhaseId,
            ActorId = combat.ActivationState?.ActiveActorId,
            CommandType = checkpoint.JournalEntry.CommandType,
            CommandId = checkpoint.JournalEntry.CommandId,
            PreviousStateHash = checkpoint.JournalEntry.PreviousStateHash,
            StateHash = checkpoint.JournalEntry.StateHash,
            SnapshotAvailable = snapshotAvailable,
            Summary = Summary(checkpoint.JournalEntry.Command)
        };
    }

    private static JsonElement Summary(JsonElement command)
    {
        if (command.ValueKind != JsonValueKind.Object)
            return JsonSerializer.SerializeToElement(new { });

        var selected = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var name in new[] { "combatId", "cardId", "consumedCardId", "destination", "heroId", "enemyIds" })
        {
            if (TryGetProperty(command, name, out var value))
                selected[name] = value.Clone();
        }
        if (TryGetProperty(command, "command", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "actorId", "actionType", "powerId", "targetId", "cardId" })
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
