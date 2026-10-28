using Core.CardZones;
using Core.Common;
using Core.Determinism;
using Core.GameplayEvents;

namespace Core.Run;

/// <summary>Only installed on an isolated run-command engine. Observes final primitive steps, never resolves them twice.</summary>
internal sealed class ObservedRunCardZoneFlows(ICardZoneFlowExecutor inner, GameplayRunObservation cursor) : ICardZoneFlowExecutor
{
    public Result<CardZoneFlowResult> Execute(CompiledCardZoneSystem system, CardZoneTopologyState state,
        DeterministicContext deterministicContext, string flowId, CardZoneFlowContext context) =>
        Observe(inner.Execute(system, state, deterministicContext, flowId, context), state, context);

    public Result<CardZoneFlowResult> ExecuteBoundary(CompiledCardZoneSystem system, CardZoneTopologyState state,
        DeterministicContext deterministicContext, string boundary, CardZoneFlowContext context) =>
        Observe(inner.ExecuteBoundary(system, state, deterministicContext, boundary, context), state, context);

    private Result<CardZoneFlowResult> Observe(Result<CardZoneFlowResult> result,
        CardZoneTopologyState before, CardZoneFlowContext context)
    {
        if (result.IsFailure || result.Value.Steps.IsEmpty) return result;
        if (context.RunSnapshot is not { } run)
            return Result<CardZoneFlowResult>.Failure("Observed run flow requires its captured run snapshot");
        var original = run with { Deck = new() { Topology = before } };
        var candidate = run with { Deck = new() { Topology = result.Value.State }, Determinism = result.Value.Context };
        var observed = GameplayCardFlowObservation.Append(cursor.Current, result.Value.Steps, original, candidate, context.ActiveActorId);
        if (observed.IsFailure) return Result<CardZoneFlowResult>.Failure(observed.Error);
        cursor.Accept(observed.Value);
        return result;
    }
}
