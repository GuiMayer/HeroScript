using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Run;

namespace Core.CardZones;

public sealed record CardZoneFlowContext
{
    private ImmutableArray<Guid> _cardInstanceIds = [];
    private ImmutableDictionary<string, double> _variables =
        ImmutableDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal);

    public CardZoneFlowInvocation Invocation { get; init; }
    public string? Trigger { get; init; }
    public string FlowOwnerId { get; init; } = string.Empty;
    public string RunOwnerId { get; init; } = string.Empty;
    public string? ActiveActorId { get; init; }
    public string? SourceActorId { get; init; }
    public string? TargetActorId { get; init; }
    public string? ExplicitOwnerId { get; init; }
    public string? ScopeId { get; init; }
    public IReadOnlyList<Guid> CardInstanceIds
    {
        get => _cardInstanceIds;
        init => _cardInstanceIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyDictionary<string, double> Variables
    {
        get => _variables;
        init => _variables = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public sealed record CardZoneFlowStepRecord
{
    public string FlowId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public CardZoneOperation Operation { get; init; }
    public string? SourceAddress { get; init; }
    public string? TargetAddress { get; init; }
    public ImmutableArray<Guid> InstanceIds { get; init; } = [];
    public ImmutableArray<Guid> CreatedInstanceIds { get; init; } = [];
    public ImmutableArray<Guid> DestroyedInstanceIds { get; init; } = [];
    public ImmutableArray<Guid> SourceOrderBefore { get; init; } = [];
    public ImmutableArray<Guid> SourceOrderAfter { get; init; } = [];
    public ImmutableArray<Guid> TargetOrderBefore { get; init; } = [];
    public ImmutableArray<Guid> TargetOrderAfter { get; init; } = [];
    public ulong RandomDrawStart { get; init; }
    public ulong RandomDrawEnd { get; init; }
    public string PreviousStateHash { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
}

public sealed record CardZoneFlowResult
{
    public CardZoneTopologyState State { get; init; } = new();
    public DeterministicContext Context { get; init; } = null!;
    public ImmutableArray<CardZoneFlowStepRecord> Steps { get; init; } = [];
    public string Fingerprint { get; init; } = string.Empty;
}

public interface ICardZoneRuleEvaluator
{
    Result<bool> EvaluateCondition(string expression, CardZoneFlowContext context);
    Result<int> EvaluateCount(string expression, CardZoneFlowContext context);
    Result<bool> Matches(CardInstanceState instance, CardZoneSelectionDefinition selection, CardZoneFlowContext context);
}

public sealed class StrictCardZoneRuleEvaluator : ICardZoneRuleEvaluator
{
    public Result<bool> EvaluateCondition(string expression, CardZoneFlowContext context) =>
        bool.TryParse(expression, out var value)
            ? Result<bool>.Success(value)
            : Result<bool>.Failure("A runtime card-zone condition evaluator is required");

    public Result<int> EvaluateCount(string expression, CardZoneFlowContext context) =>
        int.TryParse(expression, out var value)
            ? Result<int>.Success(value)
            : Result<int>.Failure("A runtime card-zone count evaluator is required");

    public Result<bool> Matches(CardInstanceState instance, CardZoneSelectionDefinition selection, CardZoneFlowContext context) =>
        Result<bool>.Failure("A runtime card-zone card predicate evaluator is required");
}

public interface ICardZoneFlowExecutor
{
    Result<CardZoneFlowResult> Execute(
        CompiledCardZoneSystem system,
        CardZoneTopologyState state,
        DeterministicContext deterministicContext,
        string flowId,
        CardZoneFlowContext context);

    Result<CardZoneFlowResult> ExecuteBoundary(
        CompiledCardZoneSystem system,
        CardZoneTopologyState state,
        DeterministicContext deterministicContext,
        string boundary,
        CardZoneFlowContext context);
}

public sealed class CardZoneFlowExecutor : ICardZoneFlowExecutor
{
    public const int MaximumSteps = 1_000;
    private readonly ICardZoneRuleEvaluator _rules;

    public CardZoneFlowExecutor(ICardZoneRuleEvaluator? rules = null) =>
        _rules = rules ?? new StrictCardZoneRuleEvaluator();

    public Result<CardZoneFlowResult> Execute(
        CompiledCardZoneSystem system,
        CardZoneTopologyState state,
        DeterministicContext deterministicContext,
        string flowId,
        CardZoneFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(deterministicContext);
        ArgumentNullException.ThrowIfNull(context);
        var records = ImmutableArray.CreateBuilder<CardZoneFlowStepRecord>();
        var result = ExecuteInternal(system, state, deterministicContext, flowId, context, records, 0);
        if (result.IsFailure) return Result<CardZoneFlowResult>.Failure(result.Error);
        return Result<CardZoneFlowResult>.Success(new CardZoneFlowResult
        {
            State = result.Value.State,
            Context = result.Value.Context,
            Steps = records.ToImmutable(),
            Fingerprint = CanonicalJson.ComputeHash(new
            {
                system.SystemId,
                flowId,
                context.Invocation,
                context.Trigger,
                steps = records.ToImmutable(),
                state = CanonicalJson.ComputeHash(result.Value.State)
            })
        });
    }

    public Result<CardZoneFlowResult> ExecuteBoundary(
        CompiledCardZoneSystem system,
        CardZoneTopologyState state,
        DeterministicContext deterministicContext,
        string boundary,
        CardZoneFlowContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        var currentState = state;
        var currentContext = deterministicContext;
        var allSteps = ImmutableArray.CreateBuilder<CardZoneFlowStepRecord>();

        var expired = currentState.Instances.Values
            .Where(instance => instance.Lifetime.Strategy == CardInstanceLifetimeStrategy.UntilBoundary &&
                               string.Equals(instance.Lifetime.Boundary, boundary, StringComparison.Ordinal))
            .Select(instance => instance.CardInstanceId)
            .OrderBy(id => id)
            .ToArray();
        if (expired.Length > 0)
        {
            var before = CanonicalJson.ComputeHash(currentState);
            var destroyed = CardZoneTransitions.Destroy(currentState, expired, CardZoneOrdering.Ordered, currentContext);
            if (destroyed.IsFailure) return Result<CardZoneFlowResult>.Failure(destroyed.Error);
            currentState = destroyed.Value.State;
            allSteps.Add(new CardZoneFlowStepRecord
            {
                FlowId = "$lifetime",
                StepId = boundary,
                Operation = CardZoneOperation.Destroy,
                InstanceIds = expired.ToImmutableArray(),
                DestroyedInstanceIds = expired.ToImmutableArray(),
                RandomDrawStart = currentContext.RandomState.DrawCount,
                RandomDrawEnd = currentContext.RandomState.DrawCount,
                PreviousStateHash = before,
                StateHash = CanonicalJson.ComputeHash(currentState)
            });
        }

        foreach (var flow in system.FlowsByTrigger.GetValueOrDefault(boundary, []))
        {
            var executed = Execute(system, currentState, currentContext, flow.FlowId, context with
            {
                Invocation = CardZoneFlowInvocation.Boundary,
                Trigger = boundary
            });
            if (executed.IsFailure) return Result<CardZoneFlowResult>.Failure(executed.Error);
            currentState = executed.Value.State;
            currentContext = executed.Value.Context;
            allSteps.AddRange(executed.Value.Steps);
        }
        return Result<CardZoneFlowResult>.Success(new CardZoneFlowResult
        {
            State = currentState,
            Context = currentContext,
            Steps = allSteps.ToImmutable(),
            Fingerprint = CanonicalJson.ComputeHash(new
            {
                system.SystemId,
                boundary,
                steps = allSteps.ToImmutable(),
                state = CanonicalJson.ComputeHash(currentState)
            })
        });
    }

    private Result<(CardZoneTopologyState State, DeterministicContext Context)> ExecuteInternal(
        CompiledCardZoneSystem system,
        CardZoneTopologyState state,
        DeterministicContext deterministicContext,
        string flowId,
        CardZoneFlowContext context,
        ImmutableArray<CardZoneFlowStepRecord>.Builder records,
        int depth)
    {
        if (depth > MaximumSteps || records.Count > MaximumSteps)
            return Result<(CardZoneTopologyState, DeterministicContext)>.Failure("Card-zone flow execution limit exceeded");
        var flowResult = system.GetFlow(flowId);
        if (flowResult.IsFailure) return Result<(CardZoneTopologyState, DeterministicContext)>.Failure(flowResult.Error);
        var flow = flowResult.Value;
        if (!flow.AllowedInvocations.Contains(context.Invocation))
            return Result<(CardZoneTopologyState, DeterministicContext)>.Failure(
                $"Card-zone flow {flowId} does not allow invocation {context.Invocation}");
        var currentState = state;
        var currentContext = deterministicContext;
        foreach (var step in flow.Steps)
        {
            if (!string.IsNullOrWhiteSpace(step.Condition))
            {
                var condition = _rules.EvaluateCondition(step.Condition, context);
                if (condition.IsFailure) return Result<(CardZoneTopologyState, DeterministicContext)>.Failure(condition.Error);
                if (!condition.Value) continue;
            }
            var before = currentState;
            var drawStart = currentContext.RandomState.DrawCount;
            var applied = ApplyStep(system, currentState, currentContext, flow, step, context, records, depth);
            if (applied.IsFailure) return applied;
            currentState = applied.Value.State;
            currentContext = applied.Value.Context;
            if (!ReferenceEquals(before, currentState))
            {
                var sourceAddress = ResolveAddress(step.SourceZoneId, step.SourceOwner, context);
                var targetAddress = ResolveAddress(step.TargetZoneId, step.TargetOwner, context);
                var sourceBefore = sourceAddress == null ? ImmutableArray<Guid>.Empty
                    : before.GetZone(sourceAddress)?.InstanceIds.ToImmutableArray() ?? [];
                var sourceAfter = sourceAddress == null ? ImmutableArray<Guid>.Empty
                    : currentState.GetZone(sourceAddress)?.InstanceIds.ToImmutableArray() ?? [];
                var targetBefore = targetAddress == null ? ImmutableArray<Guid>.Empty
                    : before.GetZone(targetAddress)?.InstanceIds.ToImmutableArray() ?? [];
                var targetAfter = targetAddress == null ? ImmutableArray<Guid>.Empty
                    : currentState.GetZone(targetAddress)?.InstanceIds.ToImmutableArray() ?? [];
                var affected = step.Operation switch
                {
                    CardZoneOperation.Move =>
                        targetAfter.Where(id => !targetBefore.Contains(id)).ToImmutableArray(),
                    CardZoneOperation.Destroy =>
                        sourceBefore.Where(id => !sourceAfter.Contains(id)).ToImmutableArray(),
                    CardZoneOperation.Create =>
                        targetAfter.Where(id => !targetBefore.Contains(id)).ToImmutableArray(),
                    CardZoneOperation.Shuffle or CardZoneOperation.Reorder => sourceAfter,
                    _ => ImmutableArray<Guid>.Empty
                };
                records.Add(new CardZoneFlowStepRecord
                {
                    FlowId = flow.FlowId,
                    StepId = step.StepId,
                    Operation = step.Operation,
                    SourceAddress = sourceAddress?.Key,
                    TargetAddress = targetAddress?.Key,
                    InstanceIds = affected,
                    SourceOrderBefore = sourceBefore,
                    SourceOrderAfter = sourceAfter,
                    TargetOrderBefore = targetBefore,
                    TargetOrderAfter = targetAfter,
                    CreatedInstanceIds = currentState.Instances.Keys.Except(before.Instances.Keys).OrderBy(id => id).ToImmutableArray(),
                    DestroyedInstanceIds = before.Instances.Keys.Except(currentState.Instances.Keys).OrderBy(id => id).ToImmutableArray(),
                    RandomDrawStart = drawStart,
                    RandomDrawEnd = currentContext.RandomState.DrawCount,
                    PreviousStateHash = CanonicalJson.ComputeHash(before),
                    StateHash = CanonicalJson.ComputeHash(currentState)
                });
            }
        }
        return Result<(CardZoneTopologyState, DeterministicContext)>.Success((currentState, currentContext));
    }

    private Result<(CardZoneTopologyState State, DeterministicContext Context)> ApplyStep(
        CompiledCardZoneSystem system,
        CardZoneTopologyState state,
        DeterministicContext deterministicContext,
        CardZoneFlowDefinition flow,
        CardZoneFlowStepDefinition step,
        CardZoneFlowContext context,
        ImmutableArray<CardZoneFlowStepRecord>.Builder records,
        int depth)
    {
        if (step.Operation == CardZoneOperation.ExecuteFlow)
            return ExecuteInternal(system, state, deterministicContext, step.NestedFlowId!, context, records, depth + 1);
        var source = ResolveAddress(step.SourceZoneId, step.SourceOwner, context);
        var target = ResolveAddress(step.TargetZoneId, step.TargetOwner, context);
        if (step.Operation == CardZoneOperation.Shuffle)
        {
            var shuffled = CardZoneTransitions.Shuffle(state, source!, deterministicContext);
            return Convert(shuffled);
        }
        if (step.Operation == CardZoneOperation.Create)
        {
            var createTargetDefinition = system.GetZone(step.TargetZoneId!);
            if (createTargetDefinition.IsFailure) return Fail(createTargetDefinition.Error);
            var count = ResolveCount(step.Selection, context);
            if (count.IsFailure) return Fail(count.Error);
            var created = CardZoneTransitions.CreateInstances(state, target!,
                Enumerable.Repeat(step.CardDefinitionId!, count.Value).ToArray(), step.Lifetime,
                step.Insertion, createTargetDefinition.Value.Capacity, createTargetDefinition.Value.Ordering, deterministicContext);
            return Convert(created);
        }

        var sourceDefinition = system.GetZone(step.SourceZoneId!);
        if (sourceDefinition.IsFailure) return Fail(sourceDefinition.Error);
        var requested = ResolveCount(step.Selection, context);
        if (requested.IsFailure) return Fail(requested.Error);
        if (step.Selection.Strategy == CardZoneSelectionStrategy.All &&
            step.Selection.Count is null && string.IsNullOrWhiteSpace(step.Selection.CountFormula))
            requested = Result<int>.Success(state.GetZone(source!)!.InstanceIds.Count);
        var effectiveSelection = step.Selection with
        {
            Count = requested.Value,
            InstanceIds = step.Selection.Strategy == CardZoneSelectionStrategy.Explicit && context.CardInstanceIds.Count > 0
                ? context.CardInstanceIds
                : step.Selection.InstanceIds
        };
        Func<CardInstanceState, bool>? predicate = null;
        if (step.Selection.Strategy is CardZoneSelectionStrategy.ByTags or CardZoneSelectionStrategy.ByCondition)
        {
            var matches = new Dictionary<Guid, bool>();
            foreach (var instanceId in state.GetZone(source!)!.InstanceIds)
            {
                var instance = state.Instances[instanceId];
                var match = _rules.Matches(instance, step.Selection, context);
                if (match.IsFailure) return Fail(match.Error);
                matches[instanceId] = match.Value;
            }
            predicate = instance => matches.GetValueOrDefault(instance.CardInstanceId);
        }
        var selected = CardZoneTransitions.Select(state, source!, effectiveSelection, deterministicContext, predicate);
        if (selected.IsFailure) return Fail(selected.Error);
        if (selected.Value.InstanceIds.Length < requested.Value && step.OnInsufficient == CardZoneInsufficientPolicy.ExecuteFallbackAndRetry)
        {
            var fallback = ExecuteInternal(system, state, selected.Value.Context, step.FallbackFlowId!, context, records, depth + 1);
            if (fallback.IsFailure) return fallback;
            if (!step.RetryAfterFallback)
                return fallback;
            return ApplyStep(system, fallback.Value.State, fallback.Value.Context, flow,
                step with { OnInsufficient = CardZoneInsufficientPolicy.AllowPartial }, context, records, depth + 1);
        }
        if (selected.Value.InstanceIds.Length < requested.Value && step.OnInsufficient == CardZoneInsufficientPolicy.RejectTransaction)
            return Fail($"Card-zone flow {flow.FlowId}/{step.StepId} selected {selected.Value.InstanceIds.Length} of {requested.Value} required cards");

        var ids = selected.Value.InstanceIds;
        if (step.Operation == CardZoneOperation.Destroy)
            return Convert(CardZoneTransitions.Destroy(state, ids, sourceDefinition.Value.Ordering, selected.Value.Context));
        if (step.Operation == CardZoneOperation.Reorder)
            return Convert(CardZoneTransitions.Reorder(state, source!, ids, selected.Value.Context));
        if (step.Operation != CardZoneOperation.Move)
            return Fail($"Unsupported card-zone flow operation: {step.Operation}");
        var targetDefinition = system.GetZone(step.TargetZoneId!);
        if (targetDefinition.IsFailure) return Fail(targetDefinition.Error);
        var capacity = targetDefinition.Value.Capacity;
        var available = capacity.HasValue
            ? System.Math.Max(0, capacity.Value - state.GetZone(target!)!.InstanceIds.Count)
            : ids.Length;
        if (available < ids.Length && step.OnOverflow == CardZoneOverflowPolicy.AllowPartial)
            ids = ids.Take(available).ToImmutableArray();
        else if (available < ids.Length && step.OnOverflow == CardZoneOverflowPolicy.RedirectOverflow)
        {
            var accepted = ids.Take(available).ToImmutableArray();
            var overflow = ids.Skip(available).ToImmutableArray();
            var movedAccepted = accepted.IsEmpty
                ? Result<CardZoneTransition>.Success(new(state, selected.Value.Context, [], [], []))
                : CardZoneTransitions.Move(state, source!, target!, accepted, step.Insertion, capacity,
                    sourceDefinition.Value.Ordering, targetDefinition.Value.Ordering, selected.Value.Context);
            if (movedAccepted.IsFailure) return Fail(movedAccepted.Error);
            return ExecuteInternal(system, movedAccepted.Value.State, movedAccepted.Value.Context, step.OverflowFlowId!,
                context with { CardInstanceIds = overflow }, records, depth + 1);
        }
        return Convert(CardZoneTransitions.Move(state, source!, target!, ids, step.Insertion, capacity,
            sourceDefinition.Value.Ordering, targetDefinition.Value.Ordering, selected.Value.Context));

        static Result<(CardZoneTopologyState, DeterministicContext)> Convert(Result<CardZoneTransition> result) =>
            result.IsFailure ? Fail(result.Error) : Result<(CardZoneTopologyState, DeterministicContext)>.Success((result.Value.State, result.Value.Context));
        static Result<(CardZoneTopologyState, DeterministicContext)> Fail(string error) =>
            Result<(CardZoneTopologyState, DeterministicContext)>.Failure(error);
    }

    private Result<int> ResolveCount(CardZoneSelectionDefinition selection, CardZoneFlowContext context)
    {
        if (!string.IsNullOrWhiteSpace(selection.CountFormula)) return _rules.EvaluateCount(selection.CountFormula, context);
        if (selection.Strategy == CardZoneSelectionStrategy.Explicit && context.CardInstanceIds.Count > 0)
            return Result<int>.Success(context.CardInstanceIds.Count);
        return Result<int>.Success(selection.Count ?? 1);
    }

    private static CardZoneAddress? ResolveAddress(string? zoneId, CardZoneOwnerBinding binding, CardZoneFlowContext context)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return null;
        var owner = binding switch
        {
            CardZoneOwnerBinding.Global => "$global",
            CardZoneOwnerBinding.RunOwner => context.RunOwnerId,
            CardZoneOwnerBinding.FlowOwner => context.FlowOwnerId,
            CardZoneOwnerBinding.ActiveActor => context.ActiveActorId,
            CardZoneOwnerBinding.SourceActor => context.SourceActorId,
            CardZoneOwnerBinding.TargetActor => context.TargetActorId,
            CardZoneOwnerBinding.Explicit => context.ExplicitOwnerId,
            _ => null
        };
        return string.IsNullOrWhiteSpace(owner) ? null : new CardZoneAddress
        {
            ZoneId = zoneId,
            OwnerId = owner,
            ScopeId = context.ScopeId
        };
    }
}
