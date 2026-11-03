using Core.CardZones;
using Core.Common;

namespace Core.Run;

/// <summary>Executes authored startup flows once against the fully materialized candidate.</summary>
internal static class RunCreationFlows
{
    internal static Result<RunState> Execute(RunState state, RunDefinition definition, int playableCount,
        CompiledCardZoneSystem system, ICardZoneFlowExecutor flows)
    {
        var owner = definition.InitialCardOwner switch
        {
            CardZoneOwnerBinding.Global => "$global",
            CardZoneOwnerBinding.RunOwner => "$run",
            CardZoneOwnerBinding.ActiveActor => state.PlayerEntityId,
            _ => string.Empty
        };
        var result = flows.ExecuteBoundary(system, state.Deck.Topology, state.Determinism, "run.started", new()
        {
            FlowOwnerId = owner, RunOwnerId = "$run", ActiveActorId = state.PlayerEntityId,
            RunPlayerActorId = state.PlayerEntityId, ContentRevision = state.Determinism.ContentRevision,
            ConfigName = state.ConfigName, Eligibility = state.Eligibility, RunSnapshot = state,
            InstanceSets = DeckLoadouts.DeckLoadoutZoneSets.Resolve(state),
            Variables = new Dictionary<string, double> { ["initialPlayableCardCount"] = playableCount }
        });
        return result.IsFailure ? Result<RunState>.Failure(result.Error)
            : Result<RunState>.Success(result.Value.ResponsesState ?? state with
            { Deck = state.Deck with { Topology = result.Value.State }, Determinism = result.Value.Context });
    }
}
