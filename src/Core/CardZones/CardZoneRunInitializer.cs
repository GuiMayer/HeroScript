using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Run;

namespace Core.CardZones;

public sealed record CardZoneRunInitializationResult(
    CardZoneTopologyState State,
    DeterministicContext Context,
    ImmutableArray<Guid> CreatedInstanceIds,
    ImmutableArray<CardZoneFlowStepRecord> InitialFlowSteps,
    string Fingerprint);

/// <summary>
/// Applies the run's authored initial placement, then emits the generic
/// run.started boundary. Any draw, prepare or other initial behavior belongs
/// to the selected graph's flows, not to this initializer.
/// </summary>
public static class CardZoneRunInitializer
{
    public static Result<CardZoneRunInitializationResult> Initialize(
        CompiledCardZoneSystem system,
        RunDefinition definition,
        IReadOnlyList<RunStartingCard> startingCards,
        string runOwnerId,
        string playerActorId,
        IReadOnlyList<string> actorIds,
        int initialPlayableCardCount,
        string contentRevision,
        string configName,
        DeterministicContext context,
        ICardZoneFlowExecutor flows)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(startingCards);
        ArgumentNullException.ThrowIfNull(actorIds);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(flows);
        if (initialPlayableCardCount < 0)
            return Result<CardZoneRunInitializationResult>.Failure("Initial playable card count cannot be negative");
        if (string.IsNullOrWhiteSpace(contentRevision) || string.IsNullOrWhiteSpace(configName))
            return Result<CardZoneRunInitializationResult>.Failure("Run card zones require pinned content identity");
        if (string.IsNullOrWhiteSpace(definition.InitialCardZoneId) ||
            !system.Zones.TryGetValue(definition.InitialCardZoneId, out var initialZone))
            return Result<CardZoneRunInitializationResult>.Failure("Run initial card zone is not in the selected graph");
        var requiredScope = definition.InitialCardOwner switch
        {
            CardZoneOwnerBinding.Global => CardZoneOwnerScope.Global,
            CardZoneOwnerBinding.RunOwner => CardZoneOwnerScope.RunOwner,
            CardZoneOwnerBinding.ActiveActor => CardZoneOwnerScope.Actor,
            _ => CardZoneOwnerScope.Unspecified
        };
        if (requiredScope == CardZoneOwnerScope.Unspecified || initialZone.OwnerScope != requiredScope)
            return Result<CardZoneRunInitializationResult>.Failure("Run initial card owner does not match the selected zone");
        var ownerId = definition.InitialCardOwner switch
        {
            CardZoneOwnerBinding.Global => "$global",
            CardZoneOwnerBinding.RunOwner => runOwnerId,
            CardZoneOwnerBinding.ActiveActor => playerActorId,
            _ => string.Empty
        };
        var plan = new CardZoneBootstrapPlan
        {
            RunOwnerId = runOwnerId,
            ActorIds = actorIds.Append(playerActorId).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal).ToArray(),
            Batches = [new CardZoneInitialBatch
            {
                ZoneId = definition.InitialCardZoneId,
                OwnerId = ownerId,
                Cards = startingCards.Select(card => new CardZoneCardCreation
                {
                    DefinitionId = card.DefinitionId,
                    Upgrades = card.Upgrades
                }).ToArray()
            }]
        };
        var bootstrapped = CardZoneBootstrapper.Create(system, plan, context);
        if (bootstrapped.IsFailure) return Result<CardZoneRunInitializationResult>.Failure(bootstrapped.Error);
        var executed = flows.ExecuteBoundary(system, bootstrapped.Value.State, bootstrapped.Value.Context,
            "run.started", new CardZoneFlowContext
            {
                FlowOwnerId = ownerId,
                RunOwnerId = runOwnerId,
                ActiveActorId = playerActorId,
                ContentRevision = contentRevision,
                ConfigName = configName,
                Variables = new Dictionary<string, double>
                {
                    ["initialPlayableCardCount"] = initialPlayableCardCount
                }
            });
        if (executed.IsFailure) return Result<CardZoneRunInitializationResult>.Failure(executed.Error);
        return Result<CardZoneRunInitializationResult>.Success(new CardZoneRunInitializationResult(
            executed.Value.State,
            executed.Value.Context,
            bootstrapped.Value.CreatedInstanceIds,
            executed.Value.Steps,
            CanonicalJson.ComputeHash(new
            {
                BootstrapFingerprint = bootstrapped.Value.Fingerprint,
                FlowFingerprint = executed.Value.Fingerprint,
                contentRevision,
                configName
            })));
    }
}
