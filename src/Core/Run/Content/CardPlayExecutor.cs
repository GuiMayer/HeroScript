using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.StatusEffects;

namespace Core.Run.Content;

public sealed record CardPlayExecutionRequest
{
    private ImmutableArray<string> _selectedTargetIds = [];

    public RunState Run { get; init; } = null!;
    public CombatState Combat { get; init; } = null!;
    public Guid CardInstanceId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedTargetIds
    {
        get => _selectedTargetIds;
        init => _selectedTargetIds = value?.ToImmutableArray() ?? [];
    }
    public string? CostOptionId { get; init; }
    public bool IgnoreConfiguredCosts { get; init; }
}

public sealed record CardPlayExecutionResult
{
    private ImmutableArray<CalculationResult> _calculations = [];
    private ImmutableArray<EffectApplicationRecord> _applications = [];

    public CombatState Combat { get; init; } = null!;
    public EffectiveCardDefinition Card { get; init; } = null!;
    public CardPlayEvaluation Evaluation { get; init; } = null!;
    public IReadOnlyList<CalculationResult> Calculations
    {
        get => _calculations;
        init => _calculations = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectApplicationRecord> Applications
    {
        get => _applications;
        init => _applications = value?.ToImmutableArray() ?? [];
    }
    public CardConsumeDestination Destination { get; init; }
    public string ResolutionFingerprint { get; init; } = string.Empty;
}

public interface ICardPlayExecutor
{
    Result<CardPlayExecutionResult> Execute(CardPlayExecutionRequest request);
}

/// <summary>
/// Resolves PLAY_CARD without mutating a repository, cache or combat service.
/// The returned snapshot is committed by the run coordinator together with
/// the card-zone transition.
/// </summary>
public sealed class CardPlayExecutor : ICardPlayExecutor
{
    private readonly IContentRuntimeResolver _runtimes;
    private readonly ICardContentCompiler _compiler;
    private readonly IEffectiveCardResolver _effectiveCards;
    private readonly ICardPlayEvaluator _legality;
    private readonly ICalculationEngine _calculations;
    private readonly ICalculationInfluenceProvider _influences;
    private readonly IImmutableEffectProcessor _effects;
    private readonly IRuntimeFormulaEvaluator _formulas;

    public CardPlayExecutor(
        IContentRuntimeResolver runtimes,
        ICardContentCompiler compiler,
        IEffectiveCardResolver effectiveCards,
        ICardPlayEvaluator legality,
        ICalculationEngine calculations,
        ICalculationInfluenceProvider influences,
        IImmutableEffectProcessor effects,
        IRuntimeFormulaEvaluator formulas)
    {
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _effectiveCards = effectiveCards ?? throw new ArgumentNullException(nameof(effectiveCards));
        _legality = legality ?? throw new ArgumentNullException(nameof(legality));
        _calculations = calculations ?? throw new ArgumentNullException(nameof(calculations));
        _influences = influences ?? throw new ArgumentNullException(nameof(influences));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
    }

    public Result<CardPlayExecutionResult> Execute(CardPlayExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Run);
        ArgumentNullException.ThrowIfNull(request.Combat);
        if (request.CardInstanceId == Guid.Empty)
            return Result<CardPlayExecutionResult>.Failure("CardInstanceId is required");
        if (!request.Run.Deck.HandInstanceIds.Contains(request.CardInstanceId))
        {
            return Result<CardPlayExecutionResult>.Failure(
                $"Card instance is not in run hand: {request.CardInstanceId}");
        }
        var instance = request.Run.Deck.GetCard(request.CardInstanceId);
        if (instance == null)
        {
            return Result<CardPlayExecutionResult>.Failure(
                $"Card instance was not found: {request.CardInstanceId}");
        }

        var runtime = _runtimes.Resolve(
            request.Run.Determinism.ContentRevision,
            request.Run.ConfigName);
        if (runtime.IsFailure)
            return Result<CardPlayExecutionResult>.Failure(runtime.Error);
        var compiled = _compiler.Compile(instance.DefinitionId, runtime.Value);
        if (compiled.IsFailure)
            return Result<CardPlayExecutionResult>.Failure(compiled.Error);
        var effective = _effectiveCards.Resolve(compiled.Value, instance);
        if (effective.IsFailure)
            return Result<CardPlayExecutionResult>.Failure(effective.Error);

        var evaluation = _legality.Evaluate(
            effective.Value,
            request.Combat,
            new CardPlayRequest
            {
                ActorId = request.ActorId,
                SelectedTargetIds = request.SelectedTargetIds,
                CostOptionId = request.CostOptionId,
                ContentRevision = request.Run.Determinism.ContentRevision,
                IgnoreConfiguredCosts = request.IgnoreConfiguredCosts
            });
        if (evaluation.IsFailure)
            return Result<CardPlayExecutionResult>.Failure(evaluation.Error);
        if (!evaluation.Value.IsLegal)
        {
            return Result<CardPlayExecutionResult>.Failure(
                string.Join("; ", evaluation.Value.FailureReasons));
        }

        var plan = BuildResolutionPlan(
            request,
            runtime.Value,
            effective.Value,
            evaluation.Value);
        if (plan.IsFailure)
            return Result<CardPlayExecutionResult>.Failure(plan.Error);
        var applied = _effects.Apply(plan.Value.Combat, plan.Value.Commands);
        if (applied.IsFailure)
            return Result<CardPlayExecutionResult>.Failure(applied.Error);

        var appended = CombatTransitions.AppendAction(
            applied.Value.State,
            new CombatAction
            {
                Turn = request.Combat.CurrentTurn,
                ActorId = request.ActorId,
                ActionType = ActionType.PLAY_CARD,
                CardInstanceId = request.CardInstanceId,
                CardDefinitionId = effective.Value.DefinitionId,
                TargetId = evaluation.Value.ResolvedTargetIds.FirstOrDefault(),
                TargetIds = evaluation.Value.ResolvedTargetIds,
                Applications = applied.Value.Records
            });
        var fingerprint = CanonicalJson.ComputeHash(new
        {
            effectiveCard = effective.Value.Fingerprint,
            evaluation = evaluation.Value,
            calculations = plan.Value.Calculations,
            effects = applied.Value.Fingerprint,
            finalState = CanonicalJson.ComputeHash(appended.State)
        });
        return Result<CardPlayExecutionResult>.Success(new CardPlayExecutionResult
        {
            Combat = appended.State,
            Card = effective.Value,
            Evaluation = evaluation.Value,
            Calculations = plan.Value.Calculations,
            Applications = applied.Value.Records,
            Destination = evaluation.Value.Destination,
            ResolutionFingerprint = fingerprint
        });
    }

    private Result<ResolutionPlan> BuildResolutionPlan(
        CardPlayExecutionRequest request,
        ContentRuntime runtime,
        EffectiveCardDefinition card,
        CardPlayEvaluation evaluation)
    {
        var commands = ImmutableArray.CreateBuilder<ResolvedEffectCommand>();
        var calculations = ImmutableArray.CreateBuilder<CalculationResult>();
        if (!request.IgnoreConfiguredCosts)
        {
            foreach (var cost in evaluation.Costs)
            {
                commands.Add(new ResolvedEffectCommand
                {
                    EffectInstanceId = $"{request.CardInstanceId:N}:cost:{cost.ComponentId}:{cost.OptionId ?? "base"}:{cost.ResourceId}",
                    Definition = new EffectDefinition
                    {
                        EffectId = $"card-cost:{cost.ComponentId}",
                        Type = EffectType.MODIFY_RESOURCE,
                        TargetResource = cost.ResourceId,
                        Operation = ResourceEffectOperation.SUBTRACT
                    },
                    SourceEntityId = request.ActorId,
                    TargetEntityIds = [request.ActorId],
                    ResolvedValue = cost.Amount,
                    Provenance = CardProvenance(request.CardInstanceId, cost.ComponentId)
                });
            }
        }

        var combat = request.Combat;
        foreach (var component in card.All<CardEffectComponentDefinition>())
        {
            var expanded = ExpandEffect(
                request,
                runtime,
                card,
                evaluation,
                component.ComponentId,
                component.Effect,
                combat,
                component.ComponentId);
            if (expanded.IsFailure)
                return Result<ResolutionPlan>.Failure(expanded.Error);
            combat = expanded.Value.Combat;
            commands.AddRange(expanded.Value.Commands);
            calculations.AddRange(expanded.Value.Calculations);
        }
        return Result<ResolutionPlan>.Success(new ResolutionPlan(
            combat,
            commands.ToImmutable(),
            calculations.ToImmutable()));
    }

    private Result<ExpandedEffect> ExpandEffect(
        CardPlayExecutionRequest request,
        ContentRuntime runtime,
        EffectiveCardDefinition card,
        CardPlayEvaluation evaluation,
        string componentId,
        EffectDefinition definition,
        CombatState combat,
        string path)
    {
        if (definition.Repeat < 1)
            return Result<ExpandedEffect>.Failure($"Effect {path} repeat must be positive");
        if (definition.Chance is < 0 or > 1)
            return Result<ExpandedEffect>.Failure($"Effect {path} chance must be between 0 and 1");
        var commands = ImmutableArray.CreateBuilder<ResolvedEffectCommand>();
        var calculations = ImmutableArray.CreateBuilder<CalculationResult>();
        var context = combat.Determinism;
        for (var repeat = 0; repeat < definition.Repeat; repeat++)
        {
            var targets = ResolveTargets(definition, request, evaluation, combat);
            if (targets.IsFailure)
                return Result<ExpandedEffect>.Failure($"Effect {path}: {targets.Error}");
            if (!ConditionPasses(
                    definition,
                    request,
                    combat,
                    targets.Value.FirstOrDefault(),
                    out var conditionError))
            {
                if (conditionError != null)
                    return Result<ExpandedEffect>.Failure($"Effect {path}: {conditionError}");
                continue;
            }
            if (definition.Chance < 1)
            {
                if (definition.Chance <= 0)
                    continue;
                var draw = context.DrawDouble();
                context = draw.Context;
                if (draw.Value >= definition.Chance)
                    continue;
            }

            foreach (var targetId in targets.Value)
            {
                var value = ResolveValue(
                    request,
                    runtime,
                    card,
                    definition,
                    combat,
                    targetId,
                    $"{request.CardInstanceId:N}:{path}:{repeat}:{targetId}");
                if (value.IsFailure)
                    return Result<ExpandedEffect>.Failure(value.Error);
                if (value.Value.Calculation != null)
                    calculations.Add(value.Value.Calculation);
                StatusEffectDefinition? status = null;
                if (definition.Type == EffectType.APPLY_STATUS)
                {
                    if (string.IsNullOrWhiteSpace(definition.StatusId))
                        return Result<ExpandedEffect>.Failure($"Effect {path} requires statusId");
                    var resolvedStatus = runtime.GetDefinition<StatusEffectDefinition>(
                        "status-effects",
                        definition.StatusId);
                    if (resolvedStatus.IsFailure)
                        return Result<ExpandedEffect>.Failure(resolvedStatus.Error);
                    status = resolvedStatus.Value;
                }
                commands.Add(new ResolvedEffectCommand
                {
                    EffectInstanceId = $"{request.CardInstanceId:N}:{path}:{repeat}:{targetId}",
                    Definition = definition,
                    SourceEntityId = request.ActorId,
                    TargetEntityIds = [targetId],
                    ResolvedValue = value.Value.Value,
                    StatusDefinition = status,
                    Provenance = CardProvenance(request.CardInstanceId, componentId)
                });
            }
        }

        combat = combat with { Determinism = context };
        foreach (var (nested, index) in (definition.ChainedEffects ?? [])
                     .Select((effect, index) => (effect, index)))
        {
            var child = ExpandEffect(
                request,
                runtime,
                card,
                evaluation,
                componentId,
                nested,
                combat,
                $"{path}.chain.{index}");
            if (child.IsFailure)
                return child;
            combat = child.Value.Combat;
            commands.AddRange(child.Value.Commands);
            calculations.AddRange(child.Value.Calculations);
        }
        foreach (var (nested, index) in (definition.ConditionalEffects ?? [])
                     .Select((effect, index) => (effect, index)))
        {
            var child = ExpandEffect(
                request,
                runtime,
                card,
                evaluation,
                componentId,
                nested,
                combat,
                $"{path}.conditional.{index}");
            if (child.IsFailure)
                return child;
            combat = child.Value.Combat;
            commands.AddRange(child.Value.Commands);
            calculations.AddRange(child.Value.Calculations);
        }
        return Result<ExpandedEffect>.Success(new ExpandedEffect(
            combat,
            commands.ToImmutable(),
            calculations.ToImmutable()));
    }

    private Result<ResolvedNumericValue> ResolveValue(
        CardPlayExecutionRequest request,
        ContentRuntime runtime,
        EffectiveCardDefinition card,
        EffectDefinition effect,
        CombatState combat,
        string targetId,
        string calculationId)
    {
        if (effect.Type is not (EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE))
            return Result<ResolvedNumericValue>.Success(new ResolvedNumericValue(0, null));
        var actor = combat.GetEntity(request.ActorId)!;
        var target = combat.GetEntity(targetId)!;
        var variables = BuildVariables(actor, target);
        var baseValue = effect.FlatValue ?? 0;
        if (!string.IsNullOrWhiteSpace(effect.FormulaValue))
        {
            var formula = EvaluateFormula(
                effect.FormulaValue,
                request.Run.Determinism.ContentRevision,
                variables);
            if (formula.IsFailure)
                return Result<ResolvedNumericValue>.Failure(formula.Error);
            baseValue += formula.Value;
        }
        var pipeline = ResolvePipeline(effect, request.Run, runtime);
        if (pipeline.IsFailure)
            return Result<ResolvedNumericValue>.Failure(pipeline.Error);
        var influences = _influences.Collect(new CalculationSourceContext
        {
            ContentRevision = request.Run.Determinism.ContentRevision,
            Card = card,
            Run = request.Run,
            Combat = combat,
            Actor = actor,
            Target = target,
            Tags = effect.Tags.ToHashSet(StringComparer.Ordinal),
            Variables = variables
        });
        if (influences.IsFailure)
            return Result<ResolvedNumericValue>.Failure(influences.Error);
        var calculated = _calculations.Calculate(new CalculationRequest
        {
            CalculationId = calculationId,
            Channel = effect.CalculationChannel,
            BaseValue = baseValue,
            Influences = influences.Value
                .Where(item => string.Equals(
                    item.Channel,
                    effect.CalculationChannel,
                    StringComparison.Ordinal))
                .ToArray(),
            Tags = effect.Tags.ToHashSet(StringComparer.Ordinal)
        }, pipeline.Value);
        return calculated.IsFailure
            ? Result<ResolvedNumericValue>.Failure(calculated.Error)
            : Result<ResolvedNumericValue>.Success(
                new ResolvedNumericValue(calculated.Value.Value, calculated.Value));
    }

    private static Result<CalculationPipelineDefinition> ResolvePipeline(
        EffectDefinition effect,
        RunState run,
        ContentRuntime runtime)
    {
        var enabled = run.ResolvedMode?.Definition.CalculationPipelineIds ?? [];
        if (!string.IsNullOrWhiteSpace(effect.CalculationPipelineId))
        {
            if (!enabled.Contains(effect.CalculationPipelineId, StringComparer.Ordinal))
            {
                return Result<CalculationPipelineDefinition>.Failure(
                    $"Calculation pipeline is not enabled by mode: {effect.CalculationPipelineId}");
            }
            return runtime.GetDefinition<CalculationPipelineDefinition>(
                "calculation-pipelines",
                effect.CalculationPipelineId);
        }
        var compatible = new List<CalculationPipelineDefinition>();
        foreach (var pipelineId in enabled.OrderBy(id => id, StringComparer.Ordinal))
        {
            var pipeline = runtime.GetDefinition<CalculationPipelineDefinition>(
                "calculation-pipelines",
                pipelineId);
            if (pipeline.IsFailure)
                return Result<CalculationPipelineDefinition>.Failure(pipeline.Error);
            if (string.Equals(pipeline.Value.Channel, effect.CalculationChannel, StringComparison.Ordinal))
                compatible.Add(pipeline.Value);
        }
        return compatible.Count == 1
            ? Result<CalculationPipelineDefinition>.Success(compatible[0])
            : Result<CalculationPipelineDefinition>.Failure(
                $"Effect channel {effect.CalculationChannel} requires exactly one enabled pipeline; found {compatible.Count}");
    }

    private bool ConditionPasses(
        EffectDefinition effect,
        CardPlayExecutionRequest request,
        CombatState combat,
        string? targetId,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(effect.Condition))
            return true;
        var actor = combat.GetEntity(request.ActorId)!;
        var target = targetId == null ? actor : combat.GetEntity(targetId)!;
        var evaluated = EvaluateFormula(
            effect.Condition,
            request.Run.Determinism.ContentRevision,
            BuildVariables(actor, target));
        if (evaluated.IsFailure)
        {
            error = evaluated.Error;
            return false;
        }
        return evaluated.Value > 0;
    }

    private static Result<IReadOnlyList<string>> ResolveTargets(
        EffectDefinition effect,
        CardPlayExecutionRequest request,
        CardPlayEvaluation evaluation,
        CombatState combat)
    {
        var actor = combat.GetEntity(request.ActorId)!;
        IReadOnlyList<string> targets = effect.Target switch
        {
            EffectTarget.SELF => [actor.EntityId],
            EffectTarget.TARGET => evaluation.ResolvedTargetIds,
            EffectTarget.ALL_ENEMIES => combat.GetAllEntities()
                .Where(entity => entity.IsAlive && entity.IsHero != actor.IsHero)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray(),
            EffectTarget.ALL_ALLIES => combat.GetAllEntities()
                .Where(entity => entity.IsAlive && entity.IsHero == actor.IsHero)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray(),
            EffectTarget.RANDOM_ENEMY or EffectTarget.LOWEST_HP_ENEMY or EffectTarget.HIGHEST_HP_ENEMY =>
                evaluation.ResolvedTargetIds,
            _ => []
        };
        return targets.Count == 0
            ? Result<IReadOnlyList<string>>.Failure("Effect resolved no targets")
            : Result<IReadOnlyList<string>>.Success(targets);
    }

    private Result<float> EvaluateFormula(
        string expression,
        string revision,
        Dictionary<string, float> variables) =>
        _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(expression, revision, variables)
            : _formulas.Evaluate(expression, variables);

    private static Dictionary<string, float> BuildVariables(
        CombatEntity actor,
        CombatEntity target)
    {
        var variables = new Dictionary<string, float>(StringComparer.Ordinal);
        AddEntityVariables(variables, "actor", actor);
        AddEntityVariables(variables, "target", target);
        return variables;
    }

    private static void AddEntityVariables(
        IDictionary<string, float> variables,
        string prefix,
        CombatEntity entity)
    {
        foreach (var (resourceId, pool) in entity.ResourceState.Resources
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            variables[$"{prefix}_{resourceId}_current"] = pool.Current;
            variables[$"{prefix}_{resourceId}_max"] = pool.Maximum;
            variables[$"{prefix}_{resourceId}_min"] = pool.Minimum;
        }
    }

    private static EffectProvenance CardProvenance(Guid cardInstanceId, string componentId) => new()
    {
        Kind = EffectProvenanceKind.Card,
        SourceId = cardInstanceId.ToString(),
        ComponentId = componentId
    };

    private sealed record ResolutionPlan(
        CombatState Combat,
        ImmutableArray<ResolvedEffectCommand> Commands,
        ImmutableArray<CalculationResult> Calculations);

    private sealed record ExpandedEffect(
        CombatState Combat,
        ImmutableArray<ResolvedEffectCommand> Commands,
        ImmutableArray<CalculationResult> Calculations);

    private sealed record ResolvedNumericValue(float Value, CalculationResult? Calculation);
}
