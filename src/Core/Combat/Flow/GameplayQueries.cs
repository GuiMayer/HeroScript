using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.CardZones;
using Core.Combat.Models;
using Core.Common;
using Core.Run;

namespace Core.Combat.Flow;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameplayQueryKind { ZoneCount, SumZoneCounts, ParticipantCount, ActivationCommandCount, ActivationMovedCount, HasBaseLegalCard }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QueryActor { Source, Target, Active, Next, Previous }
public sealed record QueryZone
{
    public string ZoneId { get; init; } = "";
    public CardZoneOwnerScope OwnerScope { get; init; }
    public QueryActor Actor { get; init; }
    public string? ScopeId { get; init; }
}
public sealed record GameplayQuery
{
    public string Variable { get; init; } = "";
    public GameplayQueryKind Kind { get; init; }
    public ImmutableArray<QueryZone> Zones { get; init; } = [];
    public string? ActionId { get; init; }
}

public static class GameplayQueries
{
    public static Result<Dictionary<string, float>> Evaluate(IEnumerable<GameplayQuery> queries, RunState run,
        CombatState? combat, string source, string? target = null, Func<Result<bool>>? hasBaseLegalCard = null)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var query in queries)
        {
            if (string.IsNullOrWhiteSpace(query.Variable) || !Enum.IsDefined(query.Kind) || result.ContainsKey(query.Variable))
                return Result<Dictionary<string, float>>.Failure("Invalid or duplicate gameplay query variable");
            long count = 0;
            if (query.Kind is GameplayQueryKind.ZoneCount or GameplayQueryKind.SumZoneCounts)
            {
                if (query.Zones.IsEmpty || query.Kind == GameplayQueryKind.ZoneCount && query.Zones.Length != 1)
                    return Result<Dictionary<string, float>>.Failure("Zone count requires explicit zone addresses");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var zone in query.Zones)
                {
                    string? owner = zone.OwnerScope switch {
                        CardZoneOwnerScope.RunOwner => "$run", CardZoneOwnerScope.Global => "$global",
                        CardZoneOwnerScope.Actor => zone.Actor switch {
                            QueryActor.Source => source, QueryActor.Target => target, QueryActor.Active => combat?.ActivationState?.ActiveActorId,
                            QueryActor.Next or QueryActor.Previous => combat == null ? null :
                                RelativeParticipantSelector.Resolve(combat, source, zone.Actor == QueryActor.Next ? 1 : -1) is { IsSuccess: true } next ? next.Value : null,
                            _ => null }, _ => null };
                    if (owner == null) return Result<Dictionary<string, float>>.Failure("Query actor is unavailable");
                    var address = new CardZoneAddress { ZoneId = zone.ZoneId, OwnerId = owner, ScopeId = zone.ScopeId };
                    var state = run.Deck.Topology.GetZone(zone.ZoneId, owner, zone.ScopeId);
                    if (state == null) return Result<Dictionary<string, float>>.Failure($"Query zone not found: {address.Key}");
                    if (seen.Add(address.Key)) count += state.InstanceIds.Count;
                }
            }
            else if (query.Kind == GameplayQueryKind.ParticipantCount) count = combat?.GetAllActors().Count(actor => actor.IsAlive) ?? 0;
            else if (query.Kind == GameplayQueryKind.HasBaseLegalCard)
            {
                if (hasBaseLegalCard == null) return Result<Dictionary<string, float>>.Failure("HasBaseLegalCard is only available to command conditions");
                var legal = hasBaseLegalCard(); if (legal.IsFailure) return Result<Dictionary<string, float>>.Failure(legal.Error);
                count = legal.Value ? 1 : 0;
            }
            else {
                if (string.IsNullOrWhiteSpace(query.ActionId)) return Result<Dictionary<string, float>>.Failure("Activation query requires actionId");
                count = combat?.ActivationState?.Facts.GetValueOrDefault(
                    (query.Kind == GameplayQueryKind.ActivationMovedCount ? "moved:" : "command:") + query.ActionId) ?? 0;
            }
            if (count > 16777216) return Result<Dictionary<string, float>>.Failure("Query count exceeds exact formula integer range");
            result.Add(query.Variable, count);
        }
        return Result<Dictionary<string, float>>.Success(result);
    }
}
