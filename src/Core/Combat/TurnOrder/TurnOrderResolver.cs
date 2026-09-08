using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Entity.Definitions;
using Core.Math;

namespace Core.Combat.TurnOrder;

public interface ITurnOrderResolver
{
    Result<TurnOrderTransition> Initialize(CombatState combat, TurnOrderPolicyDefinition policy);
    Result<TurnOrderTransition> Recalculate(
        CombatState combat,
        TurnOrderPolicyDefinition policy,
        TurnOrderRecalculationBoundary boundary);
    Result<TurnOrderTransition> CompleteActivation(
        CombatState combat,
        TurnOrderPolicyDefinition policy,
        string actorId,
        bool startsNewRound);
}

public sealed record TurnOrderTransition(CombatState State, IReadOnlyList<string> Order);

/// <summary>
/// Stateless turn-order interpreter. Authored policy enters with the command;
/// all evolving data and the successor RNG context leave inside CombatState.
/// </summary>
public sealed class TurnOrderResolver : ITurnOrderResolver
{
    private readonly IRuntimeFormulaEvaluator _formulas;

    public TurnOrderResolver(IRuntimeFormulaEvaluator formulas) =>
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));

    public Result<TurnOrderTransition> Initialize(
        CombatState combat,
        TurnOrderPolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(combat);
        var validation = TurnOrderPolicyValidator.Validate(policy);
        if (validation.IsFailure)
            return Result<TurnOrderTransition>.Failure(validation.Error);

        var fingerprint = CanonicalJson.ComputeHash(policy);
        var initial = combat with
        {
            TurnOrderState = new TurnOrderState
            {
                Strategy = policy.Strategy,
                PolicyFingerprint = fingerprint,
                TieBreakEpoch = policy.TieBreak.Epoch
            }
        };
        return Calculate(initial, policy, TurnOrderRecalculationBoundary.CombatStart);
    }

    public Result<TurnOrderTransition> Recalculate(
        CombatState combat,
        TurnOrderPolicyDefinition policy,
        TurnOrderRecalculationBoundary boundary)
    {
        ArgumentNullException.ThrowIfNull(combat);
        var stateValidation = ValidateState(combat, policy);
        if (stateValidation.IsFailure)
            return Result<TurnOrderTransition>.Failure(stateValidation.Error);
        if (boundary == TurnOrderRecalculationBoundary.Unspecified)
            return Result<TurnOrderTransition>.Failure("Turn-order recalculation boundary is required");
        if (policy.RecalculateAt != boundary)
        {
            return Result<TurnOrderTransition>.Success(
                new TurnOrderTransition(combat, combat.TurnOrderState.Order));
        }
        return Calculate(combat, policy, boundary);
    }

    public Result<TurnOrderTransition> CompleteActivation(
        CombatState combat,
        TurnOrderPolicyDefinition policy,
        string actorId,
        bool startsNewRound)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var stateValidation = ValidateState(combat, policy);
        if (stateValidation.IsFailure)
            return Result<TurnOrderTransition>.Failure(stateValidation.Error);
        if (combat.GetActor(actorId) == null)
            return Result<TurnOrderTransition>.Failure($"Turn-order actor not found: {actorId}");

        var current = combat;
        if (policy.Strategy == TurnOrderStrategy.Atb)
        {
            current = current with
            {
                TurnOrderState = current.TurnOrderState with
                {
                    AtbGauges = current.TurnOrderState.AtbGauges
                        .ToImmutableSortedDictionary(StringComparer.Ordinal)
                        .SetItem(actorId, 0f)
                }
            };
        }

        var boundary = policy.RecalculateAt switch
        {
            TurnOrderRecalculationBoundary.RoundStart when startsNewRound =>
                TurnOrderRecalculationBoundary.RoundStart,
            TurnOrderRecalculationBoundary.ActivationEnd =>
                TurnOrderRecalculationBoundary.ActivationEnd,
            TurnOrderRecalculationBoundary.ContinuousTick =>
                TurnOrderRecalculationBoundary.ContinuousTick,
            _ => TurnOrderRecalculationBoundary.Unspecified
        };
        return boundary == TurnOrderRecalculationBoundary.Unspecified
            ? Result<TurnOrderTransition>.Success(
                new TurnOrderTransition(current, current.TurnOrderState.Order))
            : Calculate(current, policy, boundary);
    }

    private Result<TurnOrderTransition> Calculate(
        CombatState combat,
        TurnOrderPolicyDefinition policy,
        TurnOrderRecalculationBoundary boundary)
    {
        var actors = combat.GetAllActors()
            .Where(actor => actor.IsAlive)
            .OrderBy(actor => actor.InstanceId, StringComparer.Ordinal)
            .ToArray();
        if (actors.Length == 0)
            return Result<TurnOrderTransition>.Failure("No alive actors are available for turn order");

        var context = combat.Determinism;
        var current = combat.TurnOrderState;
        var scores = ImmutableSortedDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
        var rolls = current.InitiativeRolls.ToImmutableSortedDictionary(StringComparer.Ordinal);
        var gauges = current.AtbGauges.ToImmutableSortedDictionary(StringComparer.Ordinal);
        ulong tickAdvance = 0;

        switch (policy.Strategy)
        {
            case TurnOrderStrategy.Fixed:
            {
                var actorIndex = combat.GetAllActors()
                    .Select((actor, index) => (actor.InstanceId, index))
                    .ToDictionary(item => item.InstanceId, item => item.index, StringComparer.Ordinal);
                scores = actors.ToImmutableSortedDictionary(
                    actor => actor.InstanceId,
                    actor => (float)actorIndex[actor.InstanceId],
                    StringComparer.Ordinal);
                rolls = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
                gauges = ImmutableSortedDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
                break;
            }
            case TurnOrderStrategy.Resource:
            {
                foreach (var actor in actors)
                {
                    var value = ResourceValue(actor, policy.Resource!.ResourceId, policy.Resource.MissingValue);
                    if (value.IsFailure)
                        return Result<TurnOrderTransition>.Failure(value.Error);
                    scores = scores.SetItem(actor.InstanceId, value.Value);
                }
                rolls = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
                gauges = ImmutableSortedDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
                break;
            }
            case TurnOrderStrategy.Initiative:
            {
                rolls = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
                foreach (var actor in actors)
                {
                    var value = ResourceValue(
                        actor,
                        policy.Initiative!.ModifierResourceId,
                        policy.Initiative.MissingValue);
                    if (value.IsFailure)
                        return Result<TurnOrderTransition>.Failure(value.Error);
                    var draw = context.DrawInt32(policy.Initiative.DieSides);
                    context = draw.Context;
                    var roll = draw.Value + 1;
                    rolls = rolls.SetItem(actor.InstanceId, roll);
                    scores = scores.SetItem(
                        actor.InstanceId,
                        roll + MathF.Floor(value.Value / policy.Initiative.ResourcePerModifier));
                }
                gauges = ImmutableSortedDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
                break;
            }
            case TurnOrderStrategy.Atb:
            {
                var atb = CalculateAtb(actors, gauges, policy.Atb!);
                if (atb.IsFailure)
                    return Result<TurnOrderTransition>.Failure(atb.Error);
                gauges = atb.Value.Gauges;
                tickAdvance = atb.Value.Ticks;
                scores = gauges;
                rolls = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
                break;
            }
            case TurnOrderStrategy.Conditional:
            {
                foreach (var actor in actors)
                {
                    var evaluated = EvaluateConditional(combat, actor, policy.Conditional!);
                    if (evaluated.IsFailure)
                        return Result<TurnOrderTransition>.Failure(evaluated.Error);
                    scores = scores.SetItem(actor.InstanceId, evaluated.Value);
                }
                rolls = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
                gauges = ImmutableSortedDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
                break;
            }
            default:
                return Result<TurnOrderTransition>.Failure(
                    $"Unsupported turn order strategy: {policy.Strategy}");
        }

        var tieRanks = current.TieBreakRanks.ToImmutableSortedDictionary(StringComparer.Ordinal);
        var refreshTieRanks = policy.TieBreak.Strategy == TurnOrderTieBreakStrategy.SeededRandom &&
            (current.Epoch == 0 || MatchesEpoch(policy.TieBreak.Epoch, boundary));
        var generation = current.TieBreakGeneration;
        if (refreshTieRanks)
        {
            tieRanks = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
            foreach (var actor in actors)
            {
                var draw = context.DrawInt32(int.MaxValue);
                context = draw.Context;
                tieRanks = tieRanks.SetItem(actor.InstanceId, draw.Value);
            }
            generation = checked(generation + 1);
        }
        else if (policy.TieBreak.Strategy != TurnOrderTieBreakStrategy.SeededRandom)
        {
            tieRanks = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
        }

        IEnumerable<CombatActorState> eligible = actors;
        if (policy.Strategy == TurnOrderStrategy.Atb)
            eligible = eligible.Where(actor => gauges[actor.InstanceId] >= policy.Atb!.ReadyThreshold);
        var direction = Direction(policy);
        IOrderedEnumerable<CombatActorState> scoredOrder = direction == TurnOrderDirection.Ascending
            ? eligible.OrderBy(actor => scores[actor.InstanceId])
            : eligible.OrderByDescending(actor => scores[actor.InstanceId]);
        var order = scoredOrder
            .ThenBy(actor => ControllerBias(actor, policy.TieBreak.Strategy))
            .ThenBy(actor => tieRanks.GetValueOrDefault(actor.InstanceId))
            .ThenBy(actor => actor.InstanceId, StringComparer.Ordinal)
            .Select(actor => actor.InstanceId)
            .ToArray();
        if (order.Length == 0)
            return Result<TurnOrderTransition>.Failure("Turn-order strategy produced no ready actors");

        var state = new TurnOrderState
        {
            Strategy = policy.Strategy,
            PolicyFingerprint = CanonicalJson.ComputeHash(policy),
            LastBoundary = boundary,
            Epoch = checked(current.Epoch + 1),
            ContinuousTick = checked(current.ContinuousTick + tickAdvance),
            TieBreakGeneration = generation,
            TieBreakEpoch = policy.TieBreak.Epoch,
            Order = order,
            Scores = scores,
            InitiativeRolls = rolls,
            AtbGauges = gauges,
            TieBreakRanks = tieRanks
        };
        var next = combat with { Determinism = context, TurnOrderState = state };
        return Result<TurnOrderTransition>.Success(new TurnOrderTransition(next, order));
    }

    private Result<float> EvaluateConditional(
        CombatState combat,
        CombatActorState actor,
        ConditionalTurnOrderDefinition definition)
    {
        var variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["turn"] = combat.CurrentTurn,
            ["round"] = combat.ActivationState?.Round ?? 1,
            ["activation"] = combat.ActivationState?.ActivationNumber ?? 0,
            ["actor_index"] = combat.GetAllActors().Select(item => item.InstanceId)
                .ToList().FindIndex(id => string.Equals(id, actor.InstanceId, StringComparison.Ordinal))
        };
        foreach (var (id, resource) in actor.ResourceState.Resources)
        {
            variables[$"actor_resource_{id}_current"] = resource.Current;
            variables[$"actor_resource_{id}_maximum"] = resource.Maximum;
            variables[$"actor_resource_{id}_percent"] = resource.Maximum == 0
                ? 0
                : resource.Current / resource.Maximum;
        }
        foreach (var (id, value) in actor.Component<StatEntityComponentState>()?.Values ??
                 new Dictionary<string, float>())
            variables[$"actor_stat_{id}"] = value;

        var result = _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(
                definition.ScoreExpression,
                combat.Determinism.ContentRevision,
                variables)
            : _formulas.Evaluate(definition.ScoreExpression, variables);
        if (result.IsFailure)
        {
            return Result<float>.Failure(
                $"Conditional turn-order expression '{definition.ScoreExpression}' failed for " +
                $"actor '{actor.InstanceId}': {result.Error}");
        }
        return float.IsFinite(result.Value)
            ? result
            : Result<float>.Failure("Conditional turn-order expression produced a non-finite score");
    }

    private static Result<(ImmutableSortedDictionary<string, float> Gauges, ulong Ticks)> CalculateAtb(
        IReadOnlyList<CombatActorState> actors,
        ImmutableSortedDictionary<string, float> existing,
        AtbTurnOrderDefinition definition)
    {
        var gauges = actors.ToImmutableSortedDictionary(
            actor => actor.InstanceId,
            actor => existing.GetValueOrDefault(actor.InstanceId),
            StringComparer.Ordinal);
        if (gauges.Values.Any(value => value >= definition.ReadyThreshold))
            return Result<(ImmutableSortedDictionary<string, float>, ulong)>.Success((gauges, 0));

        var rates = new Dictionary<string, float>(StringComparer.Ordinal);
        ulong? requiredTicks = null;
        foreach (var actor in actors)
        {
            var resource = ResourceValue(actor, definition.RateResourceId, definition.MissingValue);
            if (resource.IsFailure)
                return Result<(ImmutableSortedDictionary<string, float>, ulong)>.Failure(resource.Error);
            var increment = definition.FillRate * (resource.Value / definition.ReferenceResourceValue);
            if (!float.IsFinite(increment) || increment < 0)
                return Result<(ImmutableSortedDictionary<string, float>, ulong)>.Failure(
                    $"ATB rate for actor '{actor.InstanceId}' must be finite and non-negative");
            rates[actor.InstanceId] = increment;
            if (increment <= 0)
                continue;
            var ticks = (ulong)System.Math.Ceiling(
                System.Math.Max(0d, definition.ReadyThreshold - gauges[actor.InstanceId]) / increment);
            ticks = System.Math.Max(1UL, ticks);
            requiredTicks = requiredTicks.HasValue ? System.Math.Min(requiredTicks.Value, ticks) : ticks;
        }
        if (!requiredTicks.HasValue)
            return Result<(ImmutableSortedDictionary<string, float>, ulong)>.Failure(
                "ATB cannot advance because every alive actor has a zero rate");

        foreach (var actor in actors)
            gauges = gauges.SetItem(
                actor.InstanceId,
                gauges[actor.InstanceId] + rates[actor.InstanceId] * requiredTicks.Value);
        return Result<(ImmutableSortedDictionary<string, float>, ulong)>.Success(
            (gauges, requiredTicks.Value));
    }

    private static Result<float> ResourceValue(
        CombatActorState actor,
        string resourceId,
        float? missingValue)
    {
        if (actor.ResourceState.Resources.TryGetValue(resourceId, out var resource))
            return Result<float>.Success(resource.Current);
        return missingValue.HasValue
            ? Result<float>.Success(missingValue.Value)
            : Result<float>.Failure(
                $"Turn-order resource '{resourceId}' is missing from actor '{actor.InstanceId}'");
    }

    private static TurnOrderDirection Direction(TurnOrderPolicyDefinition policy) =>
        policy.Strategy switch
        {
            TurnOrderStrategy.Fixed => TurnOrderDirection.Ascending,
            TurnOrderStrategy.Resource => policy.Resource!.Direction,
            TurnOrderStrategy.Initiative => TurnOrderDirection.Descending,
            TurnOrderStrategy.Atb => TurnOrderDirection.Descending,
            TurnOrderStrategy.Conditional => policy.Conditional!.Direction,
            _ => TurnOrderDirection.Ascending
        };

    private static int ControllerBias(
        CombatActorState actor,
        TurnOrderTieBreakStrategy strategy) => strategy switch
    {
        TurnOrderTieBreakStrategy.PlayerControlledFirst =>
            actor.ControllerBinding.Kind == ControllerKind.Player ? 0 : 1,
        TurnOrderTieBreakStrategy.AiControlledFirst =>
            actor.ControllerBinding.Kind == ControllerKind.AI ? 0 : 1,
        _ => 0
    };

    private static bool MatchesEpoch(
        TurnOrderTieBreakEpoch epoch,
        TurnOrderRecalculationBoundary boundary) => (epoch, boundary) switch
    {
        (TurnOrderTieBreakEpoch.CombatStart, TurnOrderRecalculationBoundary.CombatStart) => true,
        (TurnOrderTieBreakEpoch.RoundStart, TurnOrderRecalculationBoundary.RoundStart) => true,
        (TurnOrderTieBreakEpoch.ActivationEnd, TurnOrderRecalculationBoundary.ActivationEnd) => true,
        (TurnOrderTieBreakEpoch.ContinuousTick, TurnOrderRecalculationBoundary.ContinuousTick) => true,
        _ => false
    };

    private static Result ValidateState(CombatState combat, TurnOrderPolicyDefinition policy)
    {
        var policyValidation = TurnOrderPolicyValidator.Validate(policy);
        if (policyValidation.IsFailure)
            return policyValidation;
        if (combat.TurnOrderState.Strategy == TurnOrderStrategy.Unspecified ||
            combat.TurnOrderState.Order.Count == 0)
            return Result.Failure("Combat turn order has not been initialized");
        if (combat.TurnOrderState.Strategy != policy.Strategy ||
            !string.Equals(
                combat.TurnOrderState.PolicyFingerprint,
                CanonicalJson.ComputeHash(policy),
                StringComparison.Ordinal))
            return Result.Failure("Combat turn-order state does not match the pinned policy");
        return Result.Success();
    }
}
