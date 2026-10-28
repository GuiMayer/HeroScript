using System.Collections.Immutable;
using Core.Common;

namespace Core.GameplayEvents;

/// <summary>Trusted producer capability supplied by composition code, never defined by equip/command JSON.</summary>
public sealed record GameplayPublisherContract(string PublisherId, HierarchySelector AllowedKinds,
    ImmutableArray<GameplayPayloadKind> Payloads, bool CanOpenResponseWindow = false);

public sealed record GameplayEventKindDefinition
{
    public string Kind { get; init; } = string.Empty;
    public int SchemaVersion { get; init; } = 1;
    public string PublisherId { get; init; } = string.Empty;
    public GameplayPayloadKind Payload { get; init; }
    public bool CanOpenResponseWindow { get; init; }
}

public sealed record GameplayReasonDefinition
{
    public string ReasonId { get; init; } = string.Empty;
    public string? MessageKey { get; init; }
    public ImmutableArray<string> Tags { get; init; } = [];
}

public sealed record GameplayCatalogAlias(string From, string To);

/// <summary>Revision-local publication result. No mutable global catalogue and no runtime alias emission.</summary>
public sealed class GameplayEventCatalog
{
    public string SettingId { get; }
    public string ContentRevision { get; }
    public ImmutableDictionary<string, GameplayEventKindDefinition> Kinds { get; }
    public ImmutableDictionary<string, GameplayReasonDefinition> Reasons { get; }
    private readonly ImmutableDictionary<string, string> _kindAliases;
    private readonly ImmutableDictionary<string, string> _reasonAliases;
    private GameplayEventCatalog(string setting, string revision, ImmutableDictionary<string, GameplayEventKindDefinition> kinds,
        ImmutableDictionary<string, GameplayReasonDefinition> reasons, ImmutableDictionary<string, string> kindAliases,
        ImmutableDictionary<string, string> reasonAliases) =>
        (SettingId, ContentRevision, Kinds, Reasons, _kindAliases, _reasonAliases) = (setting, revision, kinds, reasons, kindAliases, reasonAliases);

    public static Result<GameplayEventCatalog> Create(string setting, string revision,
        IEnumerable<GameplayPublisherContract> publishers, IEnumerable<GameplayEventKindDefinition> kinds,
        IEnumerable<GameplayReasonDefinition> reasons, IEnumerable<GameplayCatalogAlias>? kindAliases = null,
        IEnumerable<GameplayCatalogAlias>? reasonAliases = null)
    {
        if (!EventValue.Valid(setting) || !EventValue.Valid(revision) || publishers == null || kinds == null || reasons == null)
            return Result<GameplayEventCatalog>.Failure("Catalog scope and declarations are required");
        var sources = publishers.Take(1025).ToArray();
        var topics = kinds.Take(1025).ToArray();
        var motives = reasons.Take(4097).ToArray();
        if (sources.Length > 1024 || topics.Length > 1024 || motives.Length > 4096 ||
            sources.Any(item => item == null || HierarchicalName.Parse(item.PublisherId).IsFailure || item.AllowedKinds == null ||
                item.Payloads.IsDefaultOrEmpty || item.Payloads.Any(payload => !Enum.IsDefined(payload))) ||
            sources.Select(item => item.PublisherId).Distinct(StringComparer.Ordinal).Count() != sources.Length ||
            topics.Any(item => item == null || HierarchicalName.Parse(item.Kind).IsFailure || item.SchemaVersion != 1 || !Enum.IsDefined(item.Payload)) ||
            topics.Select(item => item.Kind).Distinct(StringComparer.Ordinal).Count() != topics.Length ||
            motives.Any(item => item == null || HierarchicalName.Parse(item.ReasonId).IsFailure || !EventValue.Optional(item.MessageKey) ||
                item.Tags.IsDefault || item.Tags.Length > 64 || item.Tags.Any(tag => !EventValue.Valid(tag)) ||
                item.Tags.Distinct(StringComparer.Ordinal).Count() != item.Tags.Length) ||
            motives.Select(item => item.ReasonId).Distinct(StringComparer.Ordinal).Count() != motives.Length)
            return Result<GameplayEventCatalog>.Failure("Catalog declarations are invalid, duplicate or exceed structural bounds");
        var trusted = sources.ToImmutableDictionary(item => item.PublisherId, StringComparer.Ordinal);
        foreach (var topic in topics)
            if (!trusted.TryGetValue(topic.PublisherId, out var publisher) ||
                !publisher.AllowedKinds.Matches(HierarchicalName.Parse(topic.Kind).Value) || !publisher.Payloads.Contains(topic.Payload) ||
                topic.CanOpenResponseWindow && !publisher.CanOpenResponseWindow)
                return Result<GameplayEventCatalog>.Failure("Event kind exceeds its trusted publisher capability");
        var topicMap = topics.ToImmutableDictionary(item => item.Kind, StringComparer.Ordinal);
        var motiveMap = motives.ToImmutableDictionary(item => item.ReasonId, StringComparer.Ordinal);
        var topicAliases = ResolveAliases(kindAliases ?? [], topicMap.Keys.ToHashSet(StringComparer.Ordinal));
        var motiveAliases = ResolveAliases(reasonAliases ?? [], motiveMap.Keys.ToHashSet(StringComparer.Ordinal));
        if (topicAliases.IsFailure || motiveAliases.IsFailure)
            return Result<GameplayEventCatalog>.Failure(topicAliases.IsFailure ? topicAliases.Error : motiveAliases.Error);
        return Result<GameplayEventCatalog>.Success(new(setting, revision, topicMap, motiveMap, topicAliases.Value, motiveAliases.Value));
    }

    public Result<GameplayEventKindDefinition> FindKind(string id) =>
        Kinds.TryGetValue(_kindAliases.GetValueOrDefault(id, id), out var value)
            ? Result<GameplayEventKindDefinition>.Success(value) : Result<GameplayEventKindDefinition>.Failure("Unknown event kind");
    public Result<GameplayReasonDefinition> FindReason(string id) =>
        Reasons.TryGetValue(_reasonAliases.GetValueOrDefault(id, id), out var value)
            ? Result<GameplayReasonDefinition>.Success(value) : Result<GameplayReasonDefinition>.Failure("Unknown event reason");

    private static Result<ImmutableDictionary<string, string>> ResolveAliases(IEnumerable<GameplayCatalogAlias> aliases, HashSet<string> canonical)
    {
        var items = aliases.Take(65).ToArray();
        if (items.Length > 64 || items.Any(item => item == null || HierarchicalName.Parse(item.From).IsFailure ||
                HierarchicalName.Parse(item.To).IsFailure || canonical.Contains(item.From)) ||
            items.Select(item => item.From).Distinct(StringComparer.Ordinal).Count() != items.Length)
            return Result<ImmutableDictionary<string, string>>.Failure("Invalid or conflicting catalog alias");
        var map = items.ToDictionary(item => item.From, item => item.To, StringComparer.Ordinal);
        var resolved = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var alias in items)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { alias.From };
            var target = alias.To;
            while (map.TryGetValue(target, out var next))
            {
                if (!visited.Add(target)) return Result<ImmutableDictionary<string, string>>.Failure("Cyclic catalog alias");
                target = next;
            }
            if (!canonical.Contains(target)) return Result<ImmutableDictionary<string, string>>.Failure("Alias target is not declared");
            resolved.Add(alias.From, target);
        }
        return Result<ImmutableDictionary<string, string>>.Success(resolved.ToImmutable());
    }
}
