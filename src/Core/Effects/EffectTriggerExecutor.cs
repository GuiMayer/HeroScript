using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Math;
using Core.StatusEffects;

namespace Core.Effects;

public sealed record EffectTriggerExecutionRequest
{
    private ImmutableDictionary<string, float> _variables =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);

    public CombatState Combat { get; init; } = null!;
    public EffectTriggerDefinition Trigger { get; init; } = null!;
    public string OwnerEntityId { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public EffectProvenance Provenance { get; init; } = new();
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

    public EffectTriggerExecutor(
        IRuntimeFormulaEvaluator formulas,
        IImmutableEffectProcessor effects,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _contentRuntimes = contentRuntimes;
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
        return _effects.Apply(expanded.Value.Combat, expanded.Value.Commands);
    }

    private Result<ExpandedTrigger> ExpandEffects(EffectTriggerExecutionRequest request)
    {
        var current = request.Combat;
        var commands = ImmutableArray.CreateBuilder<ResolvedEffectCommand>();
        foreach (var (effect, index) in request.Trigger.Effects.Select((item, index) => (item, index)))
        {
            var expanded = ExpandEffect(request with { Combat = current }, effect, $"{index}");
            if (expanded.IsFailure)
                return expanded;
            current = expanded.Value.Combat;
            commands.AddRange(expanded.Value.Commands);
        }
        return Result<ExpandedTrigger>.Success(new(current, commands.ToImmutable()));
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
            var targets = ResolveTargets(current, request.OwnerEntityId, effect.Target, context);
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
                var value = ResolveValue(effect, request.ContentRevision, variables);
                if (value.IsFailure)
                    return Result<ExpandedTrigger>.Failure(value.Error);
                var status = ResolveAppliedStatus(effect, request.ContentRevision);
                if (status.IsFailure)
                    return Result<ExpandedTrigger>.Failure(status.Error);
                commands.Add(new ResolvedEffectCommand
                {
                    EffectInstanceId = $"{request.Provenance.SourceId}:{request.Trigger.TriggerId}:{path}:{repeat}:{targetId}",
                    Definition = effect,
                    SourceEntityId = request.SourceEntityId,
                    TargetEntityIds = [targetId],
                    ResolvedValue = value.Value,
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
        }
        return Result<ExpandedTrigger>.Success(new(current, commands.ToImmutable()));
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

    private Result<float> ResolveValue(
        EffectDefinition effect,
        string revision,
        Dictionary<string, float> variables)
    {
        if (effect.Type is not (EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE))
            return Result<float>.Success(0);
        var value = effect.FlatValue ?? 0;
        if (string.IsNullOrWhiteSpace(effect.FormulaValue))
            return Result<float>.Success(value);
        var evaluated = Evaluate(effect.FormulaValue, revision, variables);
        return evaluated.IsFailure ? evaluated : Result<float>.Success(value + evaluated.Value);
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
        var variables = request.Variables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        var source = combat.GetEntity(request.SourceEntityId) ?? combat.GetEntity(request.OwnerEntityId)!;
        AddResources(variables, "source", source);
        AddResources(variables, "target", combat.GetEntity(targetId)!);
        return variables;
    }

    private static void AddResources(
        IDictionary<string, float> variables,
        string prefix,
        CombatEntity entity)
    {
        foreach (var (id, pool) in entity.ResourceState.Resources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            variables[$"{prefix}_{id}_current"] = pool.Current;
            variables[$"{prefix}_{id}_max"] = pool.Maximum;
            variables[$"{prefix}_{id}_min"] = pool.Minimum;
        }
    }

    private static Result<ResolvedTargets> ResolveTargets(
        CombatState combat,
        string ownerId,
        EffectTarget target,
        DeterministicContext context)
    {
        var owner = combat.GetEntity(ownerId)!;
        var candidates = target switch
        {
            EffectTarget.SELF or EffectTarget.TARGET => [ownerId],
            EffectTarget.ALL_ENEMIES or EffectTarget.RANDOM_ENEMY => combat.GetAllEntities()
                .Where(entity => entity.IsAlive && entity.IsHero != owner.IsHero)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray(),
            EffectTarget.ALL_ALLIES => combat.GetAllEntities()
                .Where(entity => entity.IsAlive && entity.IsHero == owner.IsHero)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray(),
            _ => []
        };
        if (candidates.Length == 0)
            return Result<ResolvedTargets>.Failure($"Trigger target {target} resolved no entities");
        if (target != EffectTarget.RANDOM_ENEMY)
            return Result<ResolvedTargets>.Success(new(candidates, context));
        var draw = context.DrawInt32(candidates.Length);
        return Result<ResolvedTargets>.Success(new([candidates[draw.Value]], draw.Context));
    }

    private sealed record ExpandedTrigger(
        CombatState Combat,
        ImmutableArray<ResolvedEffectCommand> Commands);

    private sealed record ResolvedTargets(
        IReadOnlyList<string> TargetIds,
        DeterministicContext Context);
}
