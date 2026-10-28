using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;

namespace Core.GameplayEvents.Responses;

/// <summary>Frozen eligibility, not an executed response. Conditions and quotas belong to dispatch.</summary>
public sealed record GameplayResponseCandidate(GameplayOccurrence Occurrence, GameplayObserverRegistration Registration,
    GameplayObserverCollectionContext Snapshot, string OwnerActorId, string SourceActorId, string TargetActorId);

/// <summary>Action-local immutable index. Never enumerates live providers during an invocation.</summary>
public sealed class GameplayResponseMatcher
{
    private readonly ImmutableDictionary<string, GameplayObserverRegistration> _registrations;
    private readonly HierarchySelectorIndex _index;
    private GameplayResponseMatcher(ImmutableDictionary<string, GameplayObserverRegistration> registrations, HierarchySelectorIndex index)
        => (_registrations, _index) = (registrations, index);

    public static Result<GameplayResponseMatcher> Create(ImmutableArray<GameplayObserverRegistration> registrations)
    {
        if (registrations.IsDefault || registrations.Any(item => item?.Response == null))
            return Result<GameplayResponseMatcher>.Failure("Collected response registrations are required");
        var index = HierarchySelectorIndex.Build(registrations.Select(item => new HierarchyIndexRegistration(item.RegistrationId, item.Response.Kinds)));
        return index.IsFailure ? Result<GameplayResponseMatcher>.Failure(index.Error)
            : Result<GameplayResponseMatcher>.Success(new(registrations.ToImmutableDictionary(item => item.RegistrationId, StringComparer.Ordinal), index.Value));
    }

    public Result<ImmutableArray<GameplayResponseCandidate>> Match(GameplayOccurrence occurrence, GameplayObserverCollectionContext snapshot)
    {
        Result<ImmutableArray<GameplayResponseCandidate>> Fail(string error) => Result<ImmutableArray<GameplayResponseCandidate>>.Failure(error);
        if (snapshot?.Run == null || snapshot.Catalog == null || occurrence?.Validate(snapshot.Catalog).IsSuccess != true ||
            snapshot.Run.SettingId != occurrence.Scope.SettingId || snapshot.Run.Determinism.ContentRevision != occurrence.Scope.ContentRevision ||
            (snapshot.Run.Lineage?.RootRunId ?? snapshot.Run.RunId).ToString("D") != occurrence.Scope.RunId ||
            snapshot.Run.RunId.ToString("D") != occurrence.Scope.BranchId ||
            occurrence.Scope.CombatId != null && snapshot.Combat?.CombatId.ToString("D") != occurrence.Scope.CombatId)
            return Fail("Response matching requires a valid occurrence and its pinned snapshot");
        var indexed = _index.Match(occurrence.Kind);
        if (indexed.IsFailure) return Fail(indexed.Error);
        var reason = HierarchicalName.Parse(occurrence.ReasonId).Value;
        var result = ImmutableArray.CreateBuilder<GameplayResponseCandidate>();
        foreach (var registration in indexed.Value.Select(id => _registrations[id]).OrderBy(item => item.Order))
        {
            var response = registration.Response;
            var definition = response.Definition;
            if (response.SettingId != snapshot.Catalog.SettingId || response.ContentRevision != snapshot.Catalog.ContentRevision)
                return Fail("Response registration belongs to another action catalog");
            if (!response.HasKnownTopics || definition.Availability.Snapshot != snapshot.Snapshot || !response.Reasons.Matches(reason)) continue;
            var filter = definition.Observe;
            if (!filter.RequiredTags.All(occurrence.Tags.Contains) || filter.ExcludedTags.Any(occurrence.Tags.Contains) ||
                !ResourceMatches(filter.Resource, occurrence.Payload)) continue;
            var owner = Bind(definition.Bindings.Owner, null);
            // A logical owner is distinct from the physical owner of a container.
            if (!ActorMatches(filter.Source, occurrence.SourceActorId, owner, snapshot) ||
                !ActorMatches(filter.Subject, occurrence.SubjectActorId, owner, snapshot) ||
                !ActorMatches(filter.Attributed, occurrence.AttributedActorId, owner, snapshot) ||
                filter.SourceSubjectRelationship is { } relation && !Related(occurrence.SourceActorId, occurrence.SubjectActorId, relation, snapshot)) continue;
            var source = Bind(definition.Bindings.Source, owner);
            var target = Bind(definition.Bindings.Target, owner);
            if (owner == null || source == null || target == null || !snapshot.ContainsActor(owner) || !snapshot.ContainsActor(source) || !snapshot.ContainsActor(target))
            {
                if (definition.Bindings.Missing == MissingResponseBinding.RejectAction) return Fail($"Response binding is absent: {definition.ResponseId}");
                continue;
            }
            if (definition.Availability.RequireLivingOwner && !Alive(owner, snapshot)) continue;
            result.Add(new(occurrence, registration, snapshot, owner, source, target));

            string? Bind(ResponseActorBinding binding, string? logicalOwner) => binding.Kind switch
            {
                ResponseActorBindingKind.ObserverOwner => registration.OwnerActorId,
                ResponseActorBindingKind.ObserverSource => registration.SourceActorId,
                ResponseActorBindingKind.Owner => logicalOwner,
                ResponseActorBindingKind.EventSource => occurrence.SourceActorId,
                ResponseActorBindingKind.EventSubject => occurrence.SubjectActorId,
                ResponseActorBindingKind.EventAttributed => occurrence.AttributedActorId,
                ResponseActorBindingKind.ExplicitActor => binding.ActorId,
                _ => null
            };
        }
        return Result<ImmutableArray<GameplayResponseCandidate>>.Success(result.ToImmutable());
    }

    internal static bool Alive(string actorId, GameplayObserverCollectionContext context) =>
        GameplayResponseSourceCompiler.Actors(context).FirstOrDefault(actor => actor.InstanceId == actorId)?.IsAlive == true;

    private static bool Related(string? from, string? to, SideRelationship relationship, GameplayObserverCollectionContext context)
    {
        var first = from == null ? null : context.Combat?.GetActor(from);
        var second = to == null ? null : context.Combat?.GetActor(to);
        return first != null && second != null && context.Combat!.Relationship(first, second) == relationship;
    }

    private static bool ActorMatches(ResponseActorFilter filter, string? actor, string? owner, GameplayObserverCollectionContext context) =>
        (filter.ActorIds.IsEmpty || actor != null && filter.ActorIds.Contains(actor, StringComparer.Ordinal)) &&
        (filter.MatchesOwner == null || actor != null && owner != null && (actor == owner) == filter.MatchesOwner) &&
        (filter.RelationshipToOwner is not { } relationship || Related(owner, actor, relationship, context));

    private static bool ResourceMatches(ResponseResourceFilter? filter, GameplayOccurrencePayload payload)
    {
        if (filter == null) return true;
        return payload is ResourceChangePayload resource &&
            (filter.ResourceIds.IsEmpty || filter.ResourceIds.Contains(resource.ResourceId, StringComparer.OrdinalIgnoreCase)) &&
            (filter.CalculationChannels.IsEmpty || resource.CalculationChannel != null && filter.CalculationChannels.Contains(resource.CalculationChannel, StringComparer.Ordinal)) &&
            (filter.Field == null || filter.Field == resource.Field) && (filter.Operation == null || filter.Operation == resource.Operation) &&
            (filter.Role == null || filter.Role == resource.Role) && (filter.Direction switch
            {
                ResourceChangeDirection.Any => true,
                ResourceChangeDirection.Increase => resource.After > resource.Before,
                ResourceChangeDirection.Decrease => resource.After < resource.Before,
                ResourceChangeDirection.Unchanged => resource.After == resource.Before,
                _ => false
            });
    }
}
