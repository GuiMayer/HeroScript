using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Math;
using Core.Resources;

namespace Core.Run.Content;

public sealed record CardPlayRequest
{
    private ImmutableArray<string> _selectedTargetIds = [];
    private ImmutableDictionary<string, float> _variables =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);

    public string ActorId { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedTargetIds
    {
        get => _selectedTargetIds;
        init => _selectedTargetIds = value?.ToImmutableArray() ?? [];
    }
    public string? CostOptionId { get; init; }
    public bool IgnoreConfiguredCosts { get; init; }
    public string ContentRevision { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, float> Variables
    {
        get => _variables;
        init => _variables = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public sealed record CardConditionTrace
{
    public string ComponentId { get; init; } = string.Empty;
    public string Expression { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string FailureReason { get; init; } = string.Empty;
}

public sealed record ResolvedCardCost
{
    public string ComponentId { get; init; } = string.Empty;
    public string? OptionId { get; init; }
    public string ResourceId { get; init; } = string.Empty;
    public float Amount { get; init; }
    public bool AllowOverdraft { get; init; }
    public bool Affordable { get; init; }
}

public sealed record CardPlayEvaluation
{
    private ImmutableArray<CardConditionTrace> _conditions = [];
    private ImmutableArray<ResolvedCardCost> _costs = [];
    private ImmutableArray<string> _affordableCostOptionIds = [];
    private ImmutableArray<string> _legalTargetIds = [];
    private ImmutableArray<string> _resolvedTargetIds = [];
    private ImmutableArray<string> _failureReasons = [];

    public Guid CardInstanceId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public bool IsLegal { get; init; }
    public IReadOnlyList<CardConditionTrace> Conditions
    {
        get => _conditions;
        init => _conditions = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ResolvedCardCost> Costs
    {
        get => _costs;
        init => _costs = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> AffordableCostOptionIds
    {
        get => _affordableCostOptionIds;
        init => _affordableCostOptionIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> LegalTargetIds
    {
        get => _legalTargetIds;
        init => _legalTargetIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> ResolvedTargetIds
    {
        get => _resolvedTargetIds;
        init => _resolvedTargetIds = value?.ToImmutableArray() ?? [];
    }
    public CardConsumeDestination Destination { get; init; }
    public string? CardZoneResolutionFlowId { get; init; }
    public IReadOnlyList<string> FailureReasons
    {
        get => _failureReasons;
        init => _failureReasons = value?.ToImmutableArray() ?? [];
    }
}

public interface ICardPlayEvaluator
{
    Result<CardPlayEvaluation> Evaluate(
        EffectiveCardDefinition card,
        CombatState combat,
        CardPlayRequest request);
}

/// <summary>
/// Single read-only authority for card conditions, costs and targets. Both
/// command execution and inspection APIs consume this result.
/// </summary>
public sealed class CardPlayEvaluator : ICardPlayEvaluator
{
    private readonly IActionCostEvaluator _costs;
    private readonly IRuntimeFormulaEvaluator _formulas;

    public CardPlayEvaluator(
        IActionCostEvaluator costs,
        IRuntimeFormulaEvaluator formulas)
    {
        _costs = costs ?? throw new ArgumentNullException(nameof(costs));
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
    }

    public Result<CardPlayEvaluation> Evaluate(
        EffectiveCardDefinition card,
        CombatState combat,
        CardPlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(request);
        var actor = combat.GetActor(request.ActorId);
        if (actor == null)
            return Result<CardPlayEvaluation>.Failure($"Actor not found: {request.ActorId}");

        var disposition = card.SingleOrDefault<CardDispositionComponentDefinition>();
        if (disposition == null)
            return Result<CardPlayEvaluation>.Failure(
                $"Card {card.DefinitionId} requires one disposition component");
        var failures = ImmutableArray.CreateBuilder<string>();
        var constraints = Core.StatusEffects.StatusActionConstraints.Evaluate(combat, actor,
            card.Tags.Append("action").ToHashSet(StringComparer.Ordinal), _formulas, request.ContentRevision);
        if (constraints.IsFailure) return Result<CardPlayEvaluation>.Failure(constraints.Error);
        failures.AddRange(constraints.Value);
        var conditionTarget = request.SelectedTargetIds.Count > 0
            ? combat.GetActor(request.SelectedTargetIds[0])
            : null;
        var variables = BuildVariables(actor, conditionTarget, request.Variables);

        var conditionTraces = ImmutableArray.CreateBuilder<CardConditionTrace>();
        foreach (var condition in card.All<CardConditionComponentDefinition>())
        {
            var evaluated = EvaluateFormula(
                condition.Expression,
                variables,
                request.ContentRevision);
            if (evaluated.IsFailure)
                return Result<CardPlayEvaluation>.Failure(
                    $"Condition {condition.ComponentId}: {evaluated.Error}");
            var passed = evaluated.Value > 0;
            conditionTraces.Add(new CardConditionTrace
            {
                ComponentId = condition.ComponentId,
                Expression = condition.Expression,
                Passed = passed,
                FailureReason = passed ? string.Empty : condition.FailureReason
            });
            if (!passed)
                failures.Add(condition.FailureReason);
        }

        var resolvedCosts = ImmutableArray.CreateBuilder<ResolvedCardCost>();
        var affordableOptions = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var component in card.All<CardCostComponentDefinition>())
        {
            var normal = ResolveCosts(
                component.ComponentId,
                optionId: null,
                component.Costs.Costs,
                actor,
                request.ContentRevision);
            if (normal.IsFailure)
                return Result<CardPlayEvaluation>.Failure(normal.Error);
            resolvedCosts.AddRange(normal.Value);
            if (!request.IgnoreConfiguredCosts && normal.Value.Any(cost => !cost.Affordable))
                failures.Add($"Costs for {component.ComponentId} cannot be paid");

            if (component.Costs.AlternativeCosts.Count == 0)
                continue;
            foreach (var option in component.Costs.AlternativeCosts
                         .OrderBy(item => item.OptionId, StringComparer.Ordinal))
            {
                var quote = ResolveCosts(
                    component.ComponentId,
                    option.OptionId,
                    option.Costs,
                    actor,
                    request.ContentRevision);
                if (quote.IsFailure)
                    return Result<CardPlayEvaluation>.Failure(quote.Error);
                if (quote.Value.All(cost => cost.Affordable))
                    affordableOptions.Add(option.OptionId);
                if (string.Equals(option.OptionId, request.CostOptionId, StringComparison.Ordinal))
                    resolvedCosts.AddRange(quote.Value);
            }
            if (request.IgnoreConfiguredCosts)
                continue;
            if (string.IsNullOrWhiteSpace(request.CostOptionId))
                failures.Add($"Cost option is required for {component.ComponentId}");
            else if (!affordableOptions.Contains(request.CostOptionId))
                failures.Add($"Cost option cannot be paid: {request.CostOptionId}");
        }

        if (!request.IgnoreConfiguredCosts && resolvedCosts.Count > 0)
        {
            var aggregate = ResourceCostTransitions.Spend(
                actor.ResourceState,
                resolvedCosts.Select(cost => new ResolvedResourceCost
                {
                    ResourceId = cost.ResourceId,
                    Amount = cost.Amount,
                    AllowOverdraft = cost.AllowOverdraft
                }).ToArray(),
                $"card-cost-quote:{card.CardInstanceId:N}");
            if (aggregate.IsFailure)
                failures.Add($"Selected card costs cannot be paid: {aggregate.Error}");
        }

        var targets = ResolveTargets(card, combat, actor, request.SelectedTargetIds);
        if (targets.IsFailure)
            return Result<CardPlayEvaluation>.Failure(targets.Error);
        failures.AddRange(targets.Value.Failures);

        var failureArray = failures.Distinct(StringComparer.Ordinal).ToImmutableArray();
        return Result<CardPlayEvaluation>.Success(new CardPlayEvaluation
        {
            CardInstanceId = card.CardInstanceId,
            ActorId = actor.InstanceId,
            IsLegal = failureArray.IsEmpty,
            Conditions = conditionTraces.ToImmutable(),
            Costs = resolvedCosts.ToImmutable(),
            AffordableCostOptionIds = affordableOptions.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            LegalTargetIds = targets.Value.LegalTargetIds,
            ResolvedTargetIds = targets.Value.ResolvedTargetIds,
            Destination = disposition.Destination,
            CardZoneResolutionFlowId = disposition.CardZoneResolutionFlowId,
            FailureReasons = failureArray
        });
    }

    private Result<ImmutableArray<ResolvedCardCost>> ResolveCosts(
        string componentId,
        string? optionId,
        IReadOnlyList<ResourceCost> costs,
        CombatActorState actor,
        string contentRevision)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedCardCost>();
        foreach (var cost in costs)
        {
            if (!actor.ResourceState.Resources.TryGetValue(cost.ResourceId, out var pool))
                return Result<ImmutableArray<ResolvedCardCost>>.Failure(
                    $"Resource not found: {cost.ResourceId}");
            var amount = _costs.CalculateCost(cost, actor.ResourceState.Resources, contentRevision);
            if (amount.IsFailure)
                return Result<ImmutableArray<ResolvedCardCost>>.Failure(amount.Error);
            result.Add(new ResolvedCardCost
            {
                ComponentId = componentId,
                OptionId = optionId,
                ResourceId = cost.ResourceId,
                Amount = amount.Value,
                AllowOverdraft = cost.AllowOverdraft,
                Affordable = cost.AllowOverdraft || pool.CanAfford(amount.Value)
            });
        }
        return Result<ImmutableArray<ResolvedCardCost>>.Success(result.ToImmutable());
    }

    private Result<TargetResolution> ResolveTargets(
        EffectiveCardDefinition card,
        CombatState combat,
        CombatActorState actor,
        IReadOnlyList<string> selectedTargetIds)
    {
        var targeting = card.SingleOrDefault<CardTargetingComponentDefinition>();
        if (targeting == null)
        {
            return selectedTargetIds.Count == 0
                ? Result<TargetResolution>.Success(new TargetResolution([], [], []))
                : Result<TargetResolution>.Success(new TargetResolution(
                    [],
                    [],
                    ["Card does not accept targets"]));
        }

        var entities = combat.GetAllActors().Where(entity => entity.IsAlive).ToArray();
        var enemies = entities.Where(entity => combat.Relationship(actor, entity) == SideRelationship.Enemy);
        var allies = entities.Where(entity => combat.Relationship(actor, entity) == SideRelationship.Ally);
        IEnumerable<CombatActorState> legal = targeting.Target switch
        {
            EffectTarget.SELF => [actor],
            EffectTarget.TARGET => targeting.AllowSelf
                ? entities
                : entities.Where(entity => entity.InstanceId != actor.InstanceId),
            EffectTarget.ALL_ENEMIES or EffectTarget.RANDOM_ENEMY or
                EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY => enemies,
            EffectTarget.ALL_ALLIES => targeting.AllowSelf
                ? allies
                : allies.Where(entity => entity.InstanceId != actor.InstanceId),
            _ => []
        };
        var legalEntities = legal.OrderBy(entity => entity.InstanceId, StringComparer.Ordinal).ToArray();
        if (targeting.Target is EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY)
        {
            if (string.IsNullOrWhiteSpace(targeting.SelectionResourceId))
            {
                return Result<TargetResolution>.Failure(
                    $"Targeting {targeting.ComponentId} requires selectionResourceId");
            }
            legalEntities = legalEntities
                .Where(entity => entity.GetResource(targeting.SelectionResourceId) != null)
                .ToArray();
            legalEntities = targeting.Target == EffectTarget.LOWEST_RESOURCE_ENEMY
                ? legalEntities.OrderBy(entity => entity.GetResource(targeting.SelectionResourceId)!.Current)
                    .ThenBy(entity => entity.InstanceId, StringComparer.Ordinal).Take(1).ToArray()
                : legalEntities.OrderByDescending(entity => entity.GetResource(targeting.SelectionResourceId)!.Current)
                    .ThenBy(entity => entity.InstanceId, StringComparer.Ordinal).Take(1).ToArray();
        }

        var legalIds = legalEntities.Select(entity => entity.InstanceId).ToImmutableArray();
        if (targeting.Target == EffectTarget.SELF)
            return Result<TargetResolution>.Success(new TargetResolution(legalIds, [actor.InstanceId], []));
        if (targeting.Target is EffectTarget.ALL_ENEMIES or EffectTarget.ALL_ALLIES or
            EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY)
        {
            return Result<TargetResolution>.Success(new TargetResolution(legalIds, legalIds, []));
        }

        var selected = selectedTargetIds.Distinct(StringComparer.Ordinal).ToImmutableArray();
        var failures = ImmutableArray.CreateBuilder<string>();
        if (selected.Length != selectedTargetIds.Count)
            failures.Add("A target was selected more than once");
        if (selected.Length < targeting.MinimumTargets || selected.Length > targeting.MaximumTargets)
        {
            failures.Add(
                $"Target count must be between {targeting.MinimumTargets} and {targeting.MaximumTargets}");
        }
        if (selected.Any(id => !legalIds.Contains(id, StringComparer.Ordinal)))
            failures.Add("Selection contains an illegal target");
        return Result<TargetResolution>.Success(new TargetResolution(
            legalIds,
            selected,
            failures.ToImmutable()));
    }

    private Result<float> EvaluateFormula(
        string expression,
        Dictionary<string, float> variables,
        string revision) =>
        !string.IsNullOrWhiteSpace(revision) && _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(expression, revision, variables)
            : _formulas.Evaluate(expression, variables);

    private static Dictionary<string, float> BuildVariables(
        CombatActorState actor,
        CombatActorState? target,
        IReadOnlyDictionary<string, float> supplied)
    {
        var variables = supplied
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        ResourceFormulaVariables.AddOwner(variables, "source", actor.ResourceState);
        ResourceFormulaVariables.AddOwner(variables, "owner", actor.ResourceState);
        if (target != null)
            ResourceFormulaVariables.AddOwner(variables, "target", target.ResourceState);
        return variables;
    }

    private sealed record TargetResolution(
        ImmutableArray<string> LegalTargetIds,
        ImmutableArray<string> ResolvedTargetIds,
        ImmutableArray<string> Failures);
}
