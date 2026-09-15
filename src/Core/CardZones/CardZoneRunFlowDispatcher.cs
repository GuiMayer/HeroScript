using Core.Common;
using Core.Determinism;
using Core.Run;

namespace Core.CardZones;

/// <summary>
/// Resolves a run's pinned zone graph for one authored lifecycle boundary.
/// Combat and run orchestration share this dispatcher; zone purpose remains
/// entirely in the selected content graph.
/// </summary>
public static class CardZoneRunFlowDispatcher
{
    public static Result<CardZoneFlowResult> Execute(
        ICardZoneFlowExecutor? flows,
        RunState run,
        DeckState deck,
        DeterministicContext context,
        string trigger,
        string? activeActorId = null,
        IReadOnlyList<Guid>? cardInstanceIds = null,
        IReadOnlyDictionary<string, double>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(context);
        if (flows == null || run.ResolvedMode?.CardZoneSystem is not { } definition)
            return Result<CardZoneFlowResult>.Failure("Run has no configured card-zone executor");
        var compiled = CardZoneSystemCompiler.Compile(definition);
        if (compiled.IsFailure) return Result<CardZoneFlowResult>.Failure(compiled.Error);
        if (!compiled.Value.FlowsByTrigger.ContainsKey(trigger))
            return Result<CardZoneFlowResult>.Failure($"Card-zone boundary is not configured: {trigger}");
        return flows.ExecuteBoundary(compiled.Value, deck.Topology, context, trigger,
            new CardZoneFlowContext
            {
                FlowOwnerId = "$run",
                RunOwnerId = "$run",
                ActiveActorId = activeActorId ?? run.PlayerEntityId,
                ContentRevision = context.ContentRevision,
                ConfigName = run.ConfigName,
                CardInstanceIds = cardInstanceIds ?? [],
                Variables = variables ?? new Dictionary<string, double>()
            });
    }

    public static Result<CardZoneFlowResult> ResolveCard(
        ICardZoneFlowExecutor? flows,
        RunState run,
        DeckState deck,
        DeterministicContext context,
        string flowId,
        string actorId,
        Guid cardInstanceId)
    {
        if (flows == null || run.ResolvedMode?.CardZoneSystem is not { } definition)
            return Result<CardZoneFlowResult>.Failure("Run has no configured card-zone executor");
        var compiled = CardZoneSystemCompiler.Compile(definition);
        if (compiled.IsFailure) return Result<CardZoneFlowResult>.Failure(compiled.Error);
        if (!compiled.Value.Flows.ContainsKey(flowId))
            return Result<CardZoneFlowResult>.Failure($"Card-resolution flow is not configured: {flowId}");
        return flows.Execute(compiled.Value, deck.Topology, context, flowId,
            new CardZoneFlowContext
            {
                Invocation = CardZoneFlowInvocation.CardResolution,
                FlowOwnerId = "$run",
                RunOwnerId = "$run",
                ActiveActorId = actorId,
                ContentRevision = context.ContentRevision,
                ConfigName = run.ConfigName,
                CardInstanceIds = [cardInstanceId]
            });
    }

    public static Result<CardZoneFlowResult> InvokeTool(
        ICardZoneFlowExecutor? flows,
        RunState run,
        string flowId,
        IReadOnlyList<Guid> cardInstanceIds,
        IReadOnlyList<string> cardDefinitionIds,
        string? actorId = null)
    {
        if (flows == null || run.ResolvedMode?.CardZoneSystem is not { } definition)
            return Result<CardZoneFlowResult>.Failure("Run has no configured card-zone executor");
        var compiled = CardZoneSystemCompiler.Compile(definition);
        if (compiled.IsFailure) return Result<CardZoneFlowResult>.Failure(compiled.Error);
        if (!compiled.Value.Flows.ContainsKey(flowId))
            return Result<CardZoneFlowResult>.Failure($"Card-zone tool flow is not configured: {flowId}");
        return flows.Execute(compiled.Value, run.Deck.Topology, run.Determinism,
            flowId, new CardZoneFlowContext
            {
                Invocation = CardZoneFlowInvocation.Tool,
                FlowOwnerId = "$run",
                RunOwnerId = "$run",
                ActiveActorId = actorId ?? run.PlayerEntityId,
                ContentRevision = run.Determinism.ContentRevision,
                ConfigName = run.ConfigName,
                CardInstanceIds = cardInstanceIds,
                CardDefinitionIds = cardDefinitionIds,
                Variables = new Dictionary<string, double>
                {
                    ["requestedCardCount"] = cardDefinitionIds.Count
                }
            });
    }
}
