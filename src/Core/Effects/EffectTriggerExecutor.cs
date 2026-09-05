using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Math;
using Core.StatusEffects;
using Core.Calculations;
using Core.Run;
using Core.Resources;

namespace Core.Effects;

public sealed record EffectTriggerExecutionRequest
{
    private ImmutableDictionary<string, float> _variables =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableArray<string> _selectedTargetEntityIds = [];

    public CombatState Combat { get; init; } = null!;
    public EffectTriggerDefinition Trigger { get; init; } = null!;
    public string OwnerEntityId { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public EffectProvenance Provenance { get; init; } = new();
    public RunState? Run { get; init; }
    public IReadOnlyList<string> SelectedTargetEntityIds
    {
        get => _selectedTargetEntityIds;
        init => _selectedTargetEntityIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyDictionary<string, float> Variables
    {
        get => _variables;
        init => _variables = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public interface IEffectTriggerExecutor
{
    Result<EffectBatchResult> Execute(EffectTriggerExecutionRequest request);
}

/// <summary>
/// Resolves a generic trigger into source-agnostic commands and delegates the
/// atomic state transition to IImmutableEffectProcessor. Source modules only
/// provide ownership, provenance and variables.
/// </summary>
public sealed class EffectTriggerExecutor : IEffectTriggerExecutor
{
    private readonly IRuntimeFormulaEvaluator _formulas;
    private readonly IImmutableEffectProcessor _effects;
    private readonly IContentRuntimeResolver? _contentRuntimes;
    private readonly ICalculationEngine? _calculations;
    private readonly ICalculationInfluenceProvider? _influences;

    public EffectTriggerExecutor(
        IRuntimeFormulaEvaluator formulas,
        IImmutableEffectProcessor effects,
        IContentRuntimeResolver? contentRuntimes = null,
        ICalculationEngine? calculations = null,
        ICalculationInfluenceProvider? influences = null)
    {
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _contentRuntimes = contentRuntimes;
        _calculations = calculations;
        _influences = influences;
    }

    public Result<EffectBatchResult> Execute(EffectTriggerExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Combat);
        ArgumentNullException.ThrowIfNull(request.Trigger);
        if (string.IsNullOrWhiteSpace(request.Trigger.TriggerId))
            return Result<EffectBatchResult>.Failure("TriggerId is required");
        if (request.Combat.GetEntity(request.OwnerEntityId) == null)
            return Result<EffectBatchResult>.Failure($"Trigger owner not found: {request.OwnerEntityId}");

        var expanded = ExpandEffects(request);
        if (expanded.IsFailure)
            return Result<EffectBatchResult>.Failure(expanded.Error);
        var applied = _effects.Apply(expanded.Value.Combat, expanded.Value.Commands);
        return applied.IsFailure
            ? applied
            : Result<EffectBatchResult>.Success(applied.Value with
            {
                Calculations = expanded.Value.Calculations,
                Fingerprint = CanonicalJson.ComputeHash(new
                {
                    effects = applied.Value.Fingerprint,
                    calculations = expanded.Value.Calculations
                })
            });
    }

    private Result<ExpandedTrigger> ExpandEffects(EffectTriggerExecutionRequest request)
    {
        var current = request.Combat;
        var commands = ImmutableArray.CreateBuilder<ResolvedEffectCommand>();
        var calculations = ImmutableArray.CreateBuilder<CalculationResult>();
        foreach (var (effect, index) in request.Trigger.Effects.Select((item, index) => (item, index)))
        {
            var expanded = ExpandEffect(request with { Combat = current }, effect, $"{index}");
            if (expanded.IsFailure)
                return expanded;
            current = expanded.Value.Combat;
            commands.AddRange(expanded.Value.Commands);
            calculations.AddRange(expanded.Value.Calculations);
        }
        return Result<ExpandedTrigger>.Success(new(
            current,
            commands.ToImmutable(),
            calculations.ToImmutable()));
    }

    private Result<ExpandedTrigger> ExpandEffect(
        EffectTriggerExecutionRequest request,
        EffectDefinition effect,
        string path)
    {
        if (effect.Repeat < 1)
            return Result<ExpandedTrigger>.Failure($"Trigger effect {path} repeat must be positive");
        if (effect.Chance is < 0 or > 1)
            return Result<ExpandedTrigger>.Failure($"Trigger effect {path} chance must be between 0 and 1");

        var current = request.Combat;
        var context = current.Determinism;
        var commands = ImmutableArray.CreateBuilder<ResolvedEffectCommand>();
        var calculations = ImmutableArray.CreateBuilder<CalculationResult>();
        for (var repeat = 0; repeat < effect.Repeat; repeat++)
        {
            if (effect.Chance < 1)
            {
                if (effect.Chance <= 0)
                    continue;
                var chance = context.DrawDouble();
                context = chance.Context;
                if (chance.Value >= effect.Chance)
                    continue;
            }
            var targets = ResolveTargets(
                current,
                request.OwnerEntityId,
                request.SelectedTargetEntityIds,
                effect.Target,
                effect.SelectionResourceId,
                context);
            if (targets.IsFailure)
                return Result<ExpandedTrigger>.Failure(targets.Error);
            context = targets.Value.Context;
            foreach (var targetId in targets.Value.TargetIds)
            {
                var variables = BuildVariables(request, current, targetId);
                var condition = EvaluateCondition(effect.Condition, request.ContentRevision, variables);
                if (condition.IsFailure)
                    return Result<ExpandedTrigger>.Failure(condition.Error);
                if (!condition.Value)
                    continue;
                var value = ResolveValue(request, effect, targetId, variables, $"{path}:{repeat}:{targetId}");
                if (value.IsFailure)
                    return Result<ExpandedTrigger>.Failure(value.Error);
                if (value.Value.Calculation != null)
                    calculations.Add(value.Value.Calculation);
                var status = ResolveAppliedStatus(effect, request.ContentRevision);
                if (status.IsFailure)
                    return Result<ExpandedTrigger>.Failure(status.Error);
                commands.Add(new ResolvedEffectCommand
                {
                    EffectInstanceId = $"{request.Provenance.SourceId}:{request.Trigger.TriggerId}:{path}:{repeat}:{targetId}",
                    Definition = effect,
                    SourceEntityId = request.SourceEntityId,
                    TargetEntityIds = [targetId],
                    ResolvedValue = value.Value.Value,
                    StatusDefinition = status.Value,
                    Provenance = request.Provenance with { ComponentId = request.Trigger.TriggerId }
                });
            }
        }
        current = current with { Determinism = context };
        foreach (var (nested, index) in (effect.ChainedEffects ?? [])
                     .Concat(effect.ConditionalEffects ?? [])
                     .Select((item, index) => (item, index)))
        {
            var child = ExpandEffect(request with { Combat = current }, nested, $"{path}.{index}");
            if (child.IsFailure)
                return child;
            current = child.Value.Combat;
            commands.AddRange(child.Value.Commands);
            calculations.AddRange(child.Value.Calculations);
        }
        return Result<ExpandedTrigger>.Success(new(
            current,
            commands.ToImmutable(),
            calculations.ToImmutable()));
    }

    private Result<StatusEffectDefinition?> ResolveAppliedStatus(
        EffectDefinition effect,
        string revision)
    {
        if (effect.Type != EffectType.APPLY_STATUS)
            return Result<StatusEffectDefinition?>.Success(null);
        if (string.IsNullOrWhiteSpace(effect.StatusId))
            return Result<StatusEffectDefinition?>.Failure("APPLY_STATUS requires statusId");
        if (_contentRuntimes == null || string.IsNullOrWhiteSpace(revision))
            return Result<StatusEffectDefinition?>.Failure(
                $"Trigger cannot resolve applied status {effect.StatusId} without pinned content");
        var runtime = _contentRuntimes.Resolve(revision);
        if (runtime.IsFailure)
            return Result<StatusEffectDefinition?>.Failure(runtime.Error);
        var definition = runtime.Value.GetDefinition<StatusEffectDefinition>("status-effects", effect.StatusId);
        return definition.IsFailure
            ? Result<StatusEffectDefinition?>.Failure(definition.Error)
            : Result<StatusEffectDefinition?>.Success(definition.Value);
    }

    private Result<ResolvedAmount> ResolveValue(
        EffectTriggerExecutionRequest request, EffectDefinition effect, string targetId,
        Dictionary<string, float> variables, string calculationSuffix)
    {
        var resolved = new CalculationResolver(_formulas, _contentRuntimes, _calculations, _influences)
            .Resolve(effect, $"{request.Provenance.SourceId}:{request.Trigger.TriggerId}:{calculationSuffix}",
                new CalculationSourceContext
                {
                    ContentRevision = request.ContentRevision, Run = request.Run, Combat = request.Combat,
                    Actor = request.Combat.GetEntity(request.SourceEntityId) ?? request.Combat.GetEntity(request.OwnerEntityId),
                    Target = request.Combat.GetEntity(targetId), Variables = variables,
                    Tags = effect.Tags.ToHashSet(StringComparer.Ordinal)
                });
        return resolved.IsFailure ? Result<ResolvedAmount>.Failure(resolved.Error)
            : Result<ResolvedAmount>.Success(new(resolved.Value.Value, resolved.Value.Calculation));
    }

    private Result<bool> EvaluateCondition(
        string? expression,
        string revision,
        Dictionary<string, float> variables)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return Result<bool>.Success(true);
        var result = Evaluate(expression, revision, variables);
        return result.IsFailure
            ? Result<bool>.Failure(result.Error)
            : Result<bool>.Success(result.Value > 0);
    }

    private Result<float> Evaluate(
        string expression,
        string revision,
        Dictionary<string, float> variables) =>
        !string.IsNullOrWhiteSpace(revision) &&
        _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(expression, revision, variables)
            : _formulas.Evaluate(expression, variables);

    private static Dictionary<string, float> BuildVariables(
        EffectTriggerExecutionRequest request,
        CombatState combat,
        string targetId)
    {
        var source = combat.GetEntity(request.SourceEntityId) ?? combat.GetEntity(request.OwnerEntityId)!;
        return GameplayFormulaContext.Build(source, combat.GetEntity(targetId),
            combat.GetEntity(request.OwnerEntityId)!, request.Run, request.Variables);
    }

    private static void AddResources(
        IDictionary<string, float> variables,
        string prefix,
        CombatEntity entity)
    {
        ResourceFormulaVariables.AddOwner(variables, prefix, entity.ResourceState);
    }

    private static Result<ResolvedTargets> ResolveTargets(
        CombatState combat,
        string ownerId,
        IReadOnlyList<string> selectedTargetIds,
        EffectTarget target,
        string? selectionResourceId,
        DeterministicContext context)
    {
        var owner = combat.GetEntity(ownerId)!;
        var candidates = target switch
        {
            EffectTarget.SELF => [ownerId],
            EffectTarget.TARGET => selectedTargetIds
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            EffectTarget.ALL_ENEMIES or EffectTarget.RANDOM_ENEMY or
                EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY => combat.GetAllEntities()
                .Where(entity => entity.IsAlive && combat.Relationship(owner, entity) == SideRelationship.Enemy)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray(),
            EffectTarget.ALL_ALLIES => combat.GetAllEntities()
                .Where(entity => entity.IsAlive && combat.Relationship(owner, entity) == SideRelationship.Ally)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray(),
            _ => []
        };
        if (candidates.Length == 0)
            return Result<ResolvedTargets>.Failure($"Trigger target {target} resolved no entities");
        if (candidates.Any(id => combat.GetEntity(id)?.IsAlive != true))
            return Result<ResolvedTargets>.Failure("Selection contains an invalid or defeated target");
        if (target is EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY)
        {
            if (string.IsNullOrWhiteSpace(selectionResourceId))
                return Result<ResolvedTargets>.Failure($"Trigger target {target} requires selectionResourceId");
            var withResource = candidates
                .Select(combat.GetEntity)
                .Where(entity => entity?.GetResource(selectionResourceId) != null)
                .Cast<CombatEntity>();
            var selected = target == EffectTarget.LOWEST_RESOURCE_ENEMY
                ? withResource.OrderBy(entity => entity.GetResource(selectionResourceId)!.Current)
                    .ThenBy(entity => entity.EntityId, StringComparer.Ordinal).FirstOrDefault()
                : withResource.OrderByDescending(entity => entity.GetResource(selectionResourceId)!.Current)
                    .ThenBy(entity => entity.EntityId, StringComparer.Ordinal).FirstOrDefault();
            return selected == null
                ? Result<ResolvedTargets>.Failure(
                    $"No candidate exposes selection resource {selectionResourceId}")
                : Result<ResolvedTargets>.Success(new([selected.EntityId], context));
        }
        if (target != EffectTarget.RANDOM_ENEMY)
            return Result<ResolvedTargets>.Success(new(candidates, context));
        var draw = context.DrawInt32(candidates.Length);
        return Result<ResolvedTargets>.Success(new([candidates[draw.Value]], draw.Context));
    }

    private sealed record ExpandedTrigger(
        CombatState Combat,
        ImmutableArray<ResolvedEffectCommand> Commands,
        ImmutableArray<CalculationResult> Calculations);

    private sealed record ResolvedTargets(
        IReadOnlyList<string> TargetIds,
        DeterministicContext Context);

    private sealed record ResolvedAmount(float Value, CalculationResult? Calculation);
}
