using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.CardZones;

public sealed record CardZoneInitialBatch
{
    private ImmutableArray<string> _definitionIds = [];
    private ImmutableArray<CardZoneCardCreation> _cards = [];

    public string ZoneId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public IReadOnlyList<string> DefinitionIds
    {
        get => _definitionIds;
        init => _definitionIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardZoneCardCreation> Cards
    {
        get => _cards;
        init => _cards = value?.ToImmutableArray() ?? [];
    }
    public CardInstanceLifetimeDefinition Lifetime { get; init; } = new();
    public CardZoneInsertionDefinition Insertion { get; init; } = new();
}

public sealed record CardZoneBootstrapPlan
{
    private ImmutableArray<string> _actorIds = [];
    private ImmutableArray<CardZoneInitialBatch> _batches = [];

    public string RunOwnerId { get; init; } = string.Empty;
    public string? ScopeId { get; init; }
    public IReadOnlyList<string> ActorIds
    {
        get => _actorIds;
        init => _actorIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardZoneInitialBatch> Batches
    {
        get => _batches;
        init => _batches = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CardZoneBootstrapResult(
    CardZoneTopologyState State,
    DeterministicContext Context,
    ImmutableArray<Guid> CreatedInstanceIds,
    string Fingerprint);

/// <summary>
/// Builds addressed zones and physical instances from a configured graph and
/// initial placement plan. No zone id has a built-in gameplay meaning.
/// </summary>
public static class CardZoneBootstrapper
{
    public static Result<CardZoneBootstrapResult> Create(
        CompiledCardZoneSystem system,
        CardZoneBootstrapPlan plan,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(plan.RunOwnerId) || plan.RunOwnerId == "$global")
            return Result<CardZoneBootstrapResult>.Failure("Card-zone bootstrap requires a distinct run owner");
        if (plan.ActorIds.Any(string.IsNullOrWhiteSpace) ||
            plan.ActorIds.Distinct(StringComparer.Ordinal).Count() != plan.ActorIds.Count ||
            plan.ActorIds.Contains("$global", StringComparer.Ordinal))
            return Result<CardZoneBootstrapResult>.Failure("Card-zone bootstrap actor identities must be distinct and nonempty");

        var actors = plan.ActorIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var addresses = system.Zones.Values.OrderBy(zone => zone.ZoneId, StringComparer.Ordinal)
            .SelectMany(zone => zone.OwnerScope switch
            {
                CardZoneOwnerScope.Global => new[] { Address(zone.ZoneId, "$global") },
                CardZoneOwnerScope.RunOwner => new[] { Address(zone.ZoneId, plan.RunOwnerId) },
                CardZoneOwnerScope.Actor => actors.Select(actor => Address(zone.ZoneId, actor)),
                _ => []
            }).ToArray();
        var created = CardZoneTransitions.CreateEmpty(addresses);
        if (created.IsFailure) return Result<CardZoneBootstrapResult>.Failure(created.Error);
        var state = created.Value;
        var current = context;
        var instanceIds = ImmutableArray.CreateBuilder<Guid>();

        foreach (var batch in plan.Batches)
        {
            if (batch.Cards.Count > 0 && batch.DefinitionIds.Count > 0)
                return Result<CardZoneBootstrapResult>.Failure("Initial card batch cannot mix cards and definitionIds");
            if (!system.Zones.TryGetValue(batch.ZoneId, out var zone))
                return Result<CardZoneBootstrapResult>.Failure($"Initial card zone not found: {batch.ZoneId}");
            if (zone.OwnerScope == CardZoneOwnerScope.Actor && !actors.Contains(batch.OwnerId, StringComparer.Ordinal))
                return Result<CardZoneBootstrapResult>.Failure($"Initial card-zone actor not found: {batch.OwnerId}");
            var target = Address(batch.ZoneId, batch.OwnerId);
            if (!state.ZoneItems.ContainsKey(target.Key))
                return Result<CardZoneBootstrapResult>.Failure($"Initial card-zone address not found: {target.Key}");
            var cards = batch.Cards.Count > 0
                ? batch.Cards
                : batch.DefinitionIds.Select(id => new CardZoneCardCreation { DefinitionId = id }).ToArray();
            var initialized = CardZoneTransitions.CreateInstances(state, target, cards,
                batch.Lifetime, batch.Insertion, zone.Capacity, zone.Ordering, current);
            if (initialized.IsFailure) return Result<CardZoneBootstrapResult>.Failure(initialized.Error);
            state = initialized.Value.State;
            current = initialized.Value.Context;
            instanceIds.AddRange(initialized.Value.CreatedInstanceIds);
        }

        var valid = CardZoneTopologyValidator.ValidateAgainstSystem(state, system, plan.RunOwnerId);
        if (valid.IsFailure) return Result<CardZoneBootstrapResult>.Failure(valid.Error);
        var ids = instanceIds.ToImmutable();
        return Result<CardZoneBootstrapResult>.Success(new CardZoneBootstrapResult(
            state, current, ids,
            CanonicalJson.ComputeHash(new
            {
                system.SystemId,
                plan.RunOwnerId,
                plan.ScopeId,
                CreatedInstanceIds = ids,
                StateHash = CanonicalJson.ComputeHash(state)
            })));

        CardZoneAddress Address(string zoneId, string ownerId) => new()
        {
            ZoneId = zoneId,
            OwnerId = ownerId,
            ScopeId = plan.ScopeId
        };
    }
}
