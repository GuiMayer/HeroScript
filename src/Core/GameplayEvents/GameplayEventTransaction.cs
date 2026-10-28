using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Determinism;
using Core.Effects;

namespace Core.GameplayEvents;

/// <summary>Persistable historical edge. Does not retain a whole event tree or a mutable execution context.</summary>
public sealed record GameplayCausalAnchor
{
    public string EventId { get; init; } = string.Empty;
    public string RootEventId { get; init; } = string.Empty;
    public string SettingId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string BranchId { get; init; } = string.Empty;
    public string ActionId { get; init; } = string.Empty;
    public string? SourceActorId { get; init; }
    public string? AttributedActorId { get; init; }
    public Result Validate() => new[] { EventId, RootEventId, SettingId, ContentRevision, RunId, BranchId, ActionId }.All(EventValue.Valid) &&
        EventValue.Optional(SourceActorId) && EventValue.Optional(AttributedActorId)
        ? Result.Success() : Result.Failure("Invalid persisted causal anchor");
    public static GameplayCausalAnchor From(GameplayOccurrence occurrence) => new()
    {
        EventId = occurrence.EventId, RootEventId = occurrence.RootEventId,
        SettingId = occurrence.Scope.SettingId, ContentRevision = occurrence.Scope.ContentRevision,
        RunId = occurrence.Scope.RunId, BranchId = occurrence.Scope.BranchId, ActionId = occurrence.Scope.ActionId,
        SourceActorId = occurrence.SourceActorId, AttributedActorId = occurrence.AttributedActorId
    };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameplayAttribution { Unspecified, Explicit, Source, InheritCause, None }

public sealed record GameplayOccurrenceDraft
{
    public string Kind { get; init; } = string.Empty;
    public string ReasonId { get; init; } = string.Empty;
    public GameplayAttribution Attribution { get; init; }
    public string? SourceActorId { get; init; }
    public string? AttributedActorId { get; init; }
    public string? SubjectActorId { get; init; }
    public GameplayEventOrigin? Origin { get; init; }
    public GameplayCausalAnchor? OriginCause { get; init; }
    public string? ImpactId { get; init; }
    public string? ProcId { get; init; }
    public string? CalculationId { get; init; }
    public ImmutableArray<string> Tags { get; init; } = [];
    public GameplayOccurrencePayload Payload { get; init; } = null!;
}

public sealed record GameplayEventLimits
{
    public int MaximumOccurrences { get; init; } = EffectExecutionLimits.MaximumSteps;
    public int MaximumDepth { get; init; } = EffectExecutionLimits.MaximumDepth;
    public bool IsValid => MaximumOccurrences is > 0 and <= EffectExecutionLimits.MaximumSteps &&
        MaximumDepth is >= 0 and <= EffectExecutionLimits.MaximumDepth;
}

public sealed record GameplayEventAppend(GameplayEventTransaction Transaction, GameplayOccurrence Occurrence);

/// <summary>Pure action-local successor. Failed append cannot consume an ordinal, mutate input or advance RNG.</summary>
public sealed class GameplayEventTransaction
{
    public GameplayEventScope Scope { get; }
    public ImmutableList<GameplayOccurrence> Occurrences { get; }
    private readonly GameplayEventCatalog _catalog;
    private readonly GameplayEventLimits _limits;
    private readonly ImmutableHashSet<string> _originBranches;
    private readonly ImmutableDictionary<string, GameplayOccurrence> _byId;
    private GameplayEventTransaction(GameplayEventScope scope, GameplayEventCatalog catalog, GameplayEventLimits limits,
        ImmutableHashSet<string> originBranches, ImmutableList<GameplayOccurrence> occurrences,
        ImmutableDictionary<string, GameplayOccurrence> byId) =>
        (Scope, _catalog, _limits, _originBranches, Occurrences, _byId) = (scope, catalog, limits, originBranches, occurrences, byId);

    public static Result<GameplayEventTransaction> Create(GameplayEventScope scope, GameplayEventCatalog catalog,
        GameplayEventLimits? limits = null, IEnumerable<string>? allowedAncestorBranches = null)
    {
        limits ??= new();
        var branches = (allowedAncestorBranches ?? []).Take(257).ToArray();
        if (scope == null || catalog == null || !limits.IsValid || branches.Length > 256 || branches.Any(id => !EventValue.Valid(id)) ||
            !new[] { scope.SettingId, scope.ContentRevision, scope.RunId, scope.BranchId, scope.ActionId, scope.CommandId }.All(EventValue.Valid) ||
            !EventValue.Optional(scope.CombatId) || catalog.SettingId != scope.SettingId || catalog.ContentRevision != scope.ContentRevision)
            return Result<GameplayEventTransaction>.Failure("Invalid action event scope, catalog, limits or ancestor branches");
        return Result<GameplayEventTransaction>.Success(new(scope, catalog, limits,
            branches.Append(scope.BranchId).ToImmutableHashSet(StringComparer.Ordinal), [],
            ImmutableDictionary<string, GameplayOccurrence>.Empty.WithComparers(StringComparer.Ordinal)));
    }

    public Result<GameplayEventAppend> Append(string publisherId, string producerPath, GameplayOccurrenceDraft draft, string? parentEventId = null)
    {
        Result<GameplayEventAppend> Fail(string message) => Result<GameplayEventAppend>.Failure(message);
        if (draft == null || !EventValue.Valid(producerPath) || draft.Tags.IsDefault || draft.Tags.Length > 64 ||
            draft.Tags.Any(tag => !EventValue.Valid(tag)) || !Enum.IsDefined(draft.Attribution) || draft.Attribution == GameplayAttribution.Unspecified)
            return Fail("Invalid event draft or missing attribution policy");
        if (Occurrences.Count >= _limits.MaximumOccurrences) return Fail("Event occurrence budget exceeded");
        var kind = _catalog.FindKind(draft.Kind);
        var reason = _catalog.FindReason(draft.ReasonId);
        if (kind.IsFailure || reason.IsFailure) return Fail("Event draft references an unknown kind or reason");
        if (!StringComparer.Ordinal.Equals(kind.Value.PublisherId, publisherId)) return Fail("Publisher cannot emit this event kind");
        GameplayOccurrence? parent = null;
        if (parentEventId != null)
        {
            if (!_byId.TryGetValue(parentEventId, out parent)) return Fail("Parent event does not belong to this action transaction");
        }
        else if (!Occurrences.IsEmpty) parent = Occurrences[0];
        var depth = parent == null ? 0 : parent.Depth + 1;
        if (depth > _limits.MaximumDepth) return Fail("Event depth budget exceeded");
        if (draft.OriginCause is { } anchor && (anchor.Validate().IsFailure || anchor.SettingId != Scope.SettingId ||
            anchor.RunId != Scope.RunId || !_originBranches.Contains(anchor.BranchId) ||
            anchor.BranchId == Scope.BranchId && anchor.ActionId == Scope.ActionId))
            return Fail("Historical cause is invalid or outside the authorized run lineage");
        if (draft.Attribution == GameplayAttribution.InheritCause && parent == null && draft.OriginCause == null)
            return Fail("Inherited attribution requires an explicit local or historical cause");
        var id = CanonicalJson.ComputeHash(new { schema = 1, scope = Scope, publisherId, producerPath, ordinal = Occurrences.Count,
            parentEventId = parent?.EventId, kind = kind.Value.Kind });
        var occurrence = new GameplayOccurrence
        {
            EventId = id, RootEventId = parent?.RootEventId ?? id, ParentEventId = parent?.EventId,
            Scope = Scope, Kind = kind.Value.Kind, ReasonId = reason.Value.ReasonId, Ordinal = Occurrences.Count, Depth = depth,
            SourceActorId = draft.SourceActorId, SubjectActorId = draft.SubjectActorId,
            AttributedActorId = draft.Attribution switch
            {
                GameplayAttribution.Source => draft.SourceActorId,
                GameplayAttribution.Explicit => draft.AttributedActorId,
                GameplayAttribution.InheritCause => parent != null ? parent.AttributedActorId : draft.OriginCause!.AttributedActorId,
                _ => null
            },
            Origin = draft.Origin, OriginCause = draft.OriginCause, ImpactId = draft.ImpactId, ProcId = draft.ProcId,
            CalculationId = draft.CalculationId, Tags = draft.Tags.Concat(reason.Value.Tags).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToImmutableArray(), Payload = draft.Payload
        };
        var valid = occurrence.Validate(_catalog);
        if (valid.IsFailure) return Fail(valid.Error);
        return Result<GameplayEventAppend>.Success(new(new(Scope, _catalog, _limits, _originBranches,
            Occurrences.Add(occurrence), _byId.Add(id, occurrence)), occurrence));
    }
}
