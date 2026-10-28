using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;

namespace Core.GameplayEvents.Responses;

public sealed record GameplayResponseContainerSource
{
    public required GameplayEventOrigin Origin { get; init; }
    public required ImmutableArray<GameplayResponseDefinition> Definitions { get; init; }
    public GameplayOwner? Owner { get; init; }
    public string? SourceActorId { get; init; }
    public Guid? CardInstanceId { get; init; }
    public ImmutableArray<GameplayEventOrigin> Contributors { get; init; } = [];
    public GameplayResponseScheduleKey Order { get; init; } = new();
    public int Stacks { get; init; } = 1;
    public int? Duration { get; init; }
}

public static class GameplayResponseSourceCompiler
{
    public static ImmutableArray<CombatActorState> Actors(GameplayObserverCollectionContext context) => context.Combat != null
        ? context.Combat.GetAllActors().OrderBy(actor => actor.InstanceId, StringComparer.Ordinal).ToImmutableArray()
        : context.Run.PlayerEntity is { } player ? [new() { InstanceId = player.InstanceId, DefinitionId = player.DefinitionId,
            ContentRevision = player.ContentRevision, Name = player.Name, Components = player.Components }] : [];

    public static Result<ImmutableArray<GameplayObserverRegistration>> Compile(string providerId, GameplayObserverCollectionContext context,
        IReadOnlyList<GameplayResponseContainerSource> sources, GameplayResponseCapabilities? capabilities = null)
    {
        if (sources.Count > HierarchySelectorIndex.MaximumRegistrations)
            return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("Observer source count exceeds budget");
        if (sources.Count == 0) return Result<ImmutableArray<GameplayObserverRegistration>>.Success([]);
        var result = ImmutableArray.CreateBuilder<GameplayObserverRegistration>();
        var actors = Actors(context);
        foreach (var source in sources)
        {
            if (source != null && !source.Definitions.IsDefault && source.Definitions.IsEmpty) continue;
            if (source == null || source.Origin == null || !EventValue.Valid(source.Origin.ContentRevision) ||
                source.Definitions.IsDefault || source.Definitions.Length > 16 || source.Definitions.Any(definition => definition == null) ||
                source.Definitions.Select(definition => definition.ResponseId).Distinct(StringComparer.Ordinal).Count() != source.Definitions.Length ||
                source.Owner is { } declaredOwner && (!Enum.IsDefined(declaredOwner.Kind) || declaredOwner.Kind != GameplayOwnerKind.Global && !EventValue.Valid(declaredOwner.Id)))
                return Result<ImmutableArray<GameplayObserverRegistration>>.Failure($"Observer source declarations or captured origin are invalid: {providerId}/{source?.Origin?.ContainerId}");
            var owners = source.Owner == null ? new string?[] { null }
                : actors.Where(actor => source.Owner.Includes(actor, context.Combat, context.Run)).Select(actor => (string?)actor.InstanceId).ToArray();
            foreach (var definition in source.Definitions)
            {
                var response = GameplayResponseCompiler.Compile(definition, context.Catalog, capabilities);
                if (response.IsFailure) return Result<ImmutableArray<GameplayObserverRegistration>>.Failure(response.Error);
                var fingerprint = CanonicalJson.ComputeHash(definition);
                foreach (var owner in owners)
                {
                    if (result.Count == HierarchySelectorIndex.MaximumRegistrations)
                        return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("Effective observer registrations exceed budget");
                    var identity = CanonicalJson.ComputeHash(new { providerId, source.Origin, definition.ResponseId, fingerprint, owner,
                        source.SourceActorId, source.CardInstanceId, source.Contributors });
                    result.Add(new() { ProviderId = providerId, RegistrationId = identity, Response = response.Value,
                        Origin = source.Origin, OwnerActorId = owner, SourceActorId = source.SourceActorId,
                        CardInstanceId = source.CardInstanceId, Contributors = source.Contributors, Order = source.Order,
                        Stacks = source.Stacks, Duration = source.Duration });
                }
            }
        }
        return Result<ImmutableArray<GameplayObserverRegistration>>.Success(result.ToImmutable());
    }
}

public abstract class GameplayContainerObserverProvider : IGameplayObserverProvider
{
    public abstract string ProviderId { get; }
    protected abstract Result<ImmutableArray<GameplayResponseContainerSource>> Sources(GameplayObserverCollectionContext context);

    public Result<ImmutableArray<GameplayObserverRegistration>> Collect(GameplayObserverCollectionContext context)
    {
        var sources = Sources(context);
        return sources.IsFailure ? Result<ImmutableArray<GameplayObserverRegistration>>.Failure(sources.Error)
            : GameplayResponseSourceCompiler.Compile(ProviderId, context, sources.Value);
    }

    protected static Result<ImmutableArray<GameplayResponseContainerSource>> Done(IEnumerable<GameplayResponseContainerSource> sources)
    {
        var snapshot = sources.Take(HierarchySelectorIndex.MaximumRegistrations + 1).ToImmutableArray();
        return snapshot.Length > HierarchySelectorIndex.MaximumRegistrations
            ? Result<ImmutableArray<GameplayResponseContainerSource>>.Failure("Observer source scan exceeds its budget")
            : Result<ImmutableArray<GameplayResponseContainerSource>>.Success(snapshot);
    }
}
