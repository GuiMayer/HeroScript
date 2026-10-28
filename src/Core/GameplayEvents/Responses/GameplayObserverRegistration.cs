using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Run;
using Core.CardZones;

namespace Core.GameplayEvents.Responses;

/// <summary>All tie breaks are explicit and ordinal; pattern specificity is never part of scheduling.</summary>
public sealed record GameplayResponseScheduleKey : IComparable<GameplayResponseScheduleKey>
{
    public long Lane { get; init; }
    public long Group { get; init; }
    public long ContainerPriority { get; init; }
    public string ContainerInstanceId { get; init; } = string.Empty;
    public long Priority { get; init; }
    public string ResponseId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public string RegistrationId { get; init; } = string.Empty;

    public int CompareTo(GameplayResponseScheduleKey? other)
    {
        if (other == null) return 1;
        var numbers = Lane.CompareTo(other.Lane); if (numbers != 0) return numbers;
        numbers = Group.CompareTo(other.Group); if (numbers != 0) return numbers;
        numbers = ContainerPriority.CompareTo(other.ContainerPriority); if (numbers != 0) return numbers;
        var text = StringComparer.Ordinal.Compare(ContainerInstanceId, other.ContainerInstanceId); if (text != 0) return text;
        numbers = Priority.CompareTo(other.Priority); if (numbers != 0) return numbers;
        text = StringComparer.Ordinal.Compare(ResponseId, other.ResponseId); if (text != 0) return text;
        text = StringComparer.Ordinal.Compare(OwnerId, other.OwnerId); if (text != 0) return text;
        return StringComparer.Ordinal.Compare(RegistrationId, other.RegistrationId);
    }
}

/// <summary>An effective registration, distinct from authored content and from a frozen invocation.</summary>
public sealed record GameplayObserverRegistration
{
    internal LegacyResourceResponseExecution? LegacyResourceExecution { get; init; }
    internal GameplayBoundaryInvocation? BoundaryExecution { get; init; }
    public required string RegistrationId { get; init; }
    public required string ProviderId { get; init; }
    public required CompiledGameplayResponse Response { get; init; }
    public required GameplayEventOrigin Origin { get; init; }
    public string? OwnerActorId { get; init; }
    public string? SourceActorId { get; init; }
    public Guid? CardInstanceId { get; init; }
    public ImmutableArray<GameplayEventOrigin> Contributors { get; init; } = [];
    public GameplayResponseScheduleKey Order { get; init; } = new();
    public int Stacks { get; init; } = 1;
    public int? Duration { get; init; }
}

internal sealed record LegacyResourceResponseExecution(Core.Effects.EffectReactionDefinition Definition, Guid RelicInstanceId, int Stacks);

public sealed record GameplayObserverCollectionContext
{
    public required RunState Run { get; init; }
    public required GameplayEventCatalog Catalog { get; init; }
    public CombatState? Combat { get; init; }
    public ResponseSnapshot Snapshot { get; init; }
    public Guid? SelectedCardId { get; init; }
    public Guid? ResolvingCardId { get; init; }

    public bool ContainsActor(string actorId) => Combat != null ? Combat.GetActor(actorId) != null : Run.PlayerEntity?.InstanceId == actorId;
}

public interface IGameplayObserverProvider
{
    string ProviderId { get; }
    Result<ImmutableArray<GameplayObserverRegistration>> Collect(GameplayObserverCollectionContext context);
}

/// <summary>Composition only. No occurrence matching, formula evaluation, RNG, effects or mutation.</summary>
public static class GameplayObserverCollector
{
    public static Result<ImmutableArray<GameplayObserverRegistration>> Collect(GameplayObserverCollectionContext context,
        IEnumerable<IGameplayObserverProvider> providers)
    {
        if (context?.Run == null || context.Catalog == null || providers == null || !Enum.IsDefined(context.Snapshot) ||
            context.Catalog.SettingId != context.Run.SettingId || context.Catalog.ContentRevision != context.Run.Determinism.ContentRevision)
            return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("Observer context must use the run's pinned catalog");
        var sources = providers.Take(65).ToArray();
        if (sources.Length > 64 || sources.Any(source => source == null || !EventValue.Valid(source.ProviderId)) ||
            sources.Select(source => source.ProviderId).Distinct(StringComparer.Ordinal).Count() != sources.Length)
            return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("Observer providers require bounded unique identities");
        var result = ImmutableArray.CreateBuilder<GameplayObserverRegistration>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in sources.OrderBy(source => source.ProviderId, StringComparer.Ordinal))
        {
            var collected = provider.Collect(context);
            if (collected.IsFailure) return Result<ImmutableArray<GameplayObserverRegistration>>.Failure(collected.Error);
            if (collected.Value.IsDefault || collected.Value.Length > HierarchySelectorIndex.MaximumRegistrations)
                return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("Observer provider exceeded its budget");
            foreach (var registration in collected.Value)
            {
                if (registration == null || !EventValue.Valid(registration.RegistrationId) || registration.ProviderId != provider.ProviderId ||
                    registration.Response == null || registration.Response.Definition == null || registration.Response.Kinds == null ||
                    registration.Response.Reasons == null || registration.Order == null || registration.Origin == null || !EventValue.Valid(registration.Origin.ContainerId) ||
                    !EventValue.Valid(registration.Origin.ComponentId) || !EventValue.Valid(registration.Origin.ContentRevision) ||
                    HierarchicalName.Parse(registration.Origin.ContainerKind).IsFailure ||
                    registration.Response.SettingId != context.Catalog.SettingId || registration.Response.ContentRevision != context.Catalog.ContentRevision ||
                    registration.Stacks < 0 || registration.Duration is < -1 || !EventValue.Optional(registration.SourceActorId) ||
                    !EventValue.Optional(registration.OwnerActorId) || registration.OwnerActorId != null && !context.ContainsActor(registration.OwnerActorId) ||
                    registration.Contributors.IsDefault || registration.Contributors.Length > 64 || registration.Contributors.Any(origin => origin == null ||
                        HierarchicalName.Parse(origin.ContainerKind).IsFailure || !EventValue.Valid(origin.ContainerId) ||
                        !EventValue.Valid(origin.ComponentId) || !EventValue.Valid(origin.ContentRevision)) || !identities.Add(registration.RegistrationId))
                    return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("Observer registration is invalid, duplicated or outside its captured scope");
                if (!registration.Response.HasKnownTopics) continue;
                var definition = registration.Response.Definition;
                if (definition.Availability.Snapshot != context.Snapshot) continue;
                if (registration.OwnerActorId == null && definition.Bindings.Owner.Kind == ResponseActorBindingKind.ObserverOwner)
                    return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("An ownerless observer requires an explicit owner binding");
                var available = Available(registration, context);
                if (available.IsFailure) return Result<ImmutableArray<GameplayObserverRegistration>>.Failure(available.Error);
                if (!available.Value) continue;
                if (result.Count == HierarchySelectorIndex.MaximumRegistrations)
                    return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("Observer collection exceeded its action budget");
                result.Add(registration with { Order = registration.Order with { Priority = registration.Response.NormalizedPriority,
                    ResponseId = definition.ResponseId, OwnerId = registration.OwnerActorId ?? string.Empty, RegistrationId = registration.RegistrationId } });
            }
        }
        return Result<ImmutableArray<GameplayObserverRegistration>>.Success(result.OrderBy(item => item.Order).ToImmutableArray());
    }

    private static Result<bool> Available(GameplayObserverRegistration registration, GameplayObserverCollectionContext context)
    {
        var availability = registration.Response.Definition.Availability;
        if (registration.CardInstanceId is not { } cardId)
            return availability.Kind == ResponseAvailabilityKind.ContainerActive ? Result<bool>.Success(true)
                : Result<bool>.Failure("Card availability requires an effective card registration");
        if (availability.Kind == ResponseAvailabilityKind.ContainerActive)
            return Result<bool>.Failure("Cards require explicit zone, selection or resolution availability");
        var topology = context.Run.Deck.Topology;
        var zone = topology.FindZone(cardId);
        var definitions = context.Run.ResolvedMode?.CardZoneSystem?.Zones;
        var definition = definitions?.SingleOrDefault(item => item.ZoneId == zone?.Address.ZoneId);
        if (topology.GetCard(cardId) == null || zone == null || definition == null)
            return Result<bool>.Failure("Card observer requires a live instance and its pinned zone definition");
        var owned = definition.OwnerScope switch
        {
            CardZoneOwnerScope.Actor => registration.OwnerActorId == zone.Address.OwnerId,
            CardZoneOwnerScope.RunOwner => zone.Address.OwnerId == "$run" && registration.OwnerActorId == context.Run.PlayerEntityId,
            CardZoneOwnerScope.Global => zone.Address.OwnerId == "$global" && registration.OwnerActorId == null,
            _ => false
        };
        if (!owned) return Result<bool>.Failure("Card observer does not belong to its physical zone owner");
        if (availability.Kind == ResponseAvailabilityKind.CardZones && availability.ZoneIds.Any(id => !definitions!.Any(zone => zone.ZoneId == id)))
            return Result<bool>.Failure("Card observer references an unknown pinned zone");
        return Result<bool>.Success(availability.Kind switch
        {
            ResponseAvailabilityKind.CardZones => availability.ZoneIds.Contains(zone.Address.ZoneId, StringComparer.Ordinal),
            ResponseAvailabilityKind.SelectedCard => context.SelectedCardId == cardId,
            ResponseAvailabilityKind.ResolvingCard => context.ResolvingCardId == cardId,
            _ => false
        });
    }
}
