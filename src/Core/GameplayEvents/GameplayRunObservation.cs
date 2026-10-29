using System.Collections.Immutable;
using Core.Common;
using Core.Resources;
using Core.Run;

namespace Core.GameplayEvents;

/// <summary>Candidate-local cursor. Stored values are immutable successors; this object is never placed in gameplay state or persistence.</summary>
internal sealed class GameplayRunObservation(GameplayEffectObservation current)
{
    public GameplayEffectObservation Current { get; private set; } = current;
    private IRunGameplayOccurrenceExecutor? _executor;
    internal void Bind(IRunGameplayOccurrenceExecutor executor)
    {
        if (_executor != null) throw new InvalidOperationException("Occurrence executor is already bound to this candidate");
        _executor = executor;
    }

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

    public Result<RunState> RecordWallet(RunState before, ResourceSetMutationResult result, IReadOnlyList<ResolvedResourceMutation> inputs, string? reasonId = null)
    {
        var previous = Current;
        var observed = GameplayResourceFactProducer.Append(Current.Events, inputs, result.Records,
            new() { Kind = "run", Id = before.ResourceState.OwnerId, ScopeId = Current.Events.Scope.BranchId }, new()
            {
                ReasonId = reasonId ?? Current.ReasonId, Attribution = GameplayAttribution.Source,
                SourceActorId = before.PlayerEntityId,
                Origin = new() { ContainerKind = "rule", ContainerId = "run.resources", ComponentId = "wallet", ContentRevision = before.Determinism.ContentRevision }
            }, Current.PublisherId, $"wallet/{Current.Events.Occurrences.Count}", Current.ParentEventId);
        if (observed.IsFailure) return Result<RunState>.Failure(observed.Error);
        Current = Current with { Events = observed.Value };
        var after = before with { ResourceState = result.State };
        if (_executor == null) return Result<RunState>.Success(after);
        var dispatched = _executor.Dispatch(before, after, previous, Current);
        if (dispatched.IsFailure) return Result<RunState>.Failure(dispatched.Error);
        Current = dispatched.Value.Observation;
        return Result<RunState>.Success(dispatched.Value.Execution);
    }
}
