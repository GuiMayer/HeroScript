using System.Collections.Immutable;
using Core.Common;
using Core.Resources;
using Core.Run;

namespace Core.GameplayEvents;

/// <summary>Candidate-local cursor. Stored values are immutable successors; this object is never placed in gameplay state or persistence.</summary>
internal sealed class GameplayRunObservation(GameplayEffectObservation current)
{
    public GameplayEffectObservation Current { get; private set; } = current;

    internal void Accept(GameplayEffectObservation successor) => Current = successor;

    internal IRunActivityEffectExecutor Wrap(IRunActivityEffectExecutor executor) => new ActivityAdapter(executor, this);

    private sealed class ActivityAdapter(IRunActivityEffectExecutor executor, GameplayRunObservation cursor) : IRunActivityEffectExecutor
    {
        public Result<RunActivityEffectResult> Execute(RunState run, RunMapNodeState node, RunActivityBoundary boundary)
        {
            var result = executor.ExecuteObserved(run, node, boundary, cursor.Current);
            if (result.IsFailure) return Result<RunActivityEffectResult>.Failure(result.Error);
            cursor.Accept(result.Value.Observation);
            return Result<RunActivityEffectResult>.Success(result.Value.Execution);
        }
    }

    public Result RecordWallet(RunState before, ResourceSetMutationResult result, IReadOnlyList<ResolvedResourceMutation> inputs, string? reasonId = null)
    {
        var observed = GameplayResourceFactProducer.Append(Current.Events, inputs, result.Records,
            new() { Kind = "run", Id = before.ResourceState.OwnerId, ScopeId = Current.Events.Scope.BranchId }, new()
            {
                ReasonId = reasonId ?? Current.ReasonId, Attribution = GameplayAttribution.Source,
                SourceActorId = before.PlayerEntityId,
                Origin = new() { ContainerKind = "rule", ContainerId = "run.resources", ComponentId = "wallet", ContentRevision = before.Determinism.ContentRevision }
            }, Current.PublisherId, $"wallet/{Current.Events.Occurrences.Count}", Current.ParentEventId);
        if (observed.IsFailure) return Result.Failure(observed.Error);
        Current = Current with { Events = observed.Value };
        return Result.Success();
    }
}
