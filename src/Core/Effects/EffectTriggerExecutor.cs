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
    public Core.Run.Content.EffectiveCardDefinition? Card { get; init; }
    public ImmutableHashSet<string> Tags { get; init; } = ImmutableHashSet<string>.Empty;
    public ImmutableArray<ResolvedEffectCommand> PrefixCommands { get; init; } = [];
    public ImmutableArray<EffectTriggerDefinition> Components { get; init; } = [];
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
        var definitionErrors = EffectDefinitionValidator.Validate(
            (request.Components.IsEmpty ? [request.Trigger] : request.Components).SelectMany(item => item.Effects));
        if (!definitionErrors.IsEmpty)
            return Result<EffectBatchResult>.Failure(string.Join("; ", definitionErrors));

        var current = request.Combat;
        var currentRun = request.Run;
        var records = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        var calculations = ImmutableArray.CreateBuilder<CalculationResult>();
        var steps = ImmutableArray.CreateBuilder<EffectExecutionStep>();
        var work = 0;
        foreach (var command in request.PrefixCommands)
        {
            if (++work > EffectExecutionLimits.MaximumSteps)
                return Result<EffectBatchResult>.Failure("Effect execution limit exceeded");
            var before = CanonicalJson.ComputeHash(current);
            var applied = _effects.Apply(current, [command]);
            if (applied.IsFailure) return Result<EffectBatchResult>.Failure(applied.Error);
            current = applied.Value.State;
            records.AddRange(applied.Value.Records);
            steps.Add(new()
            {
                Index = steps.Count, EffectInstanceId = command.EffectInstanceId,
                TargetEntityId = command.TargetEntityIds.FirstOrDefault() ?? string.Empty,
                Applied = true, ContentRevision = request.ContentRevision, Provenance = command.Provenance,
                Applications = applied.Value.Records.ToImmutableArray(),
                StateBeforeHash = before, StateAfterHash = CanonicalJson.ComputeHash(current)
            });
        }
        var activeTriggerId = request.Trigger.TriggerId;
        foreach (var component in request.Components.IsEmpty ? [request.Trigger] : request.Components)
        {
            activeTriggerId = component.TriggerId;
            foreach (var (effect, index) in component.Effects.Select((item, index) => (item, index)))
            {
                var executed = ExecuteEffect(effect, $"{component.TriggerId}:{index}", 0, request.SelectedTargetEntityIds);
                if (executed.IsFailure) return Result<EffectBatchResult>.Failure(executed.Error);
            }
        }
        return Result<EffectBatchResult>.Success(new()
        {
            State = current, Run = currentRun, Records = records.ToImmutable(),
            Calculations = calculations.ToImmutable(), Steps = steps.ToImmutable(),
            Fingerprint = CanonicalJson.ComputeHash(new { state = current, steps = steps.ToImmutable(), run = currentRun })
        });

        Result ExecuteEffect(EffectDefinition effect, string path, int depth, IReadOnlyList<string> selection)
        {
            if (++work > EffectExecutionLimits.MaximumSteps || depth > EffectExecutionLimits.MaximumDepth)
                return Result.Failure("Effect execution limit exceeded");
            if (effect.Repeat < 1 || effect.Repeat > EffectExecutionLimits.MaximumRepeat)
                return Result.Failure($"Effect {path} repeat is outside execution limits");
            if (!float.IsFinite(effect.Chance) || effect.Chance is < 0 or > 1 || !Enum.IsDefined(effect.ChanceScope))
                return Result.Failure($"Effect {path} has an invalid chance policy");
            if (effect.ConditionalEffects?.Count > 0)
                return Result.Failure("conditionalEffects is unsupported; use chainedEffects with a condition");
            for (var repeat = 0; repeat < effect.Repeat; repeat++)
            {
                var beforeSelection = CanonicalJson.ComputeHash(current);
                var targets = ResolveTargets(current, request.OwnerEntityId, selection, effect.Target,
                    effect.SelectionResourceId, current.Determinism);
                if (targets.IsFailure) return Result.Failure(targets.Error);
                current = current with { Determinism = targets.Value.Context };
                double? effectRoll = null;
                var effectPass = effect.ChanceScope != EffectChanceScope.PerEffect || DrawChance(effect.Chance, out effectRoll);
                for (var targetIndex = 0; targetIndex < targets.Value.TargetIds.Count; targetIndex++)
                {
                    if (++work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                    var targetId = targets.Value.TargetIds[targetIndex];
                    var id = $"{request.Provenance.SourceId}:{request.Trigger.TriggerId}:{path}:{repeat}:{targetId}";
                    var before = targetIndex == 0 ? beforeSelection : CanonicalJson.ComputeHash(current);
                    var runBefore = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun);
                    var variables = BuildVariables(request with { Run = currentRun }, current, targetId);
                    variables["repeat_index"] = repeat;
                    variables["target_index"] = targetIndex;
                    IReadOnlySet<string> tags = request.Tags.Count == 0 ? effect.Tags.ToHashSet(StringComparer.Ordinal) : request.Tags;
                    var tagsPass = (effect.RequiredTags ?? []).All(tags.Contains) && !(effect.ExcludedTags ?? []).Any(tags.Contains);
                    var condition = effectPass && tagsPass
                        ? EvaluateCondition(effect.Condition, request.ContentRevision, variables) : Result<bool>.Success(false);
                    if (condition.IsFailure) return Result.Failure(condition.Error);
                    var roll = effectRoll;
                    var chancePass = effectPass;
                    if (effectPass && condition.Value && effect.ChanceScope == EffectChanceScope.PerTarget)
                        chancePass = DrawChance(effect.Chance, out roll);
                    var applies = tagsPass && condition.Value && chancePass;
                    CalculationResult? calculation = null;
                    ImmutableArray<EffectApplicationRecord> appliedRecords = [];
                    if (applies)
                    {
                        var value = ResolveValue(request with { Combat = current, Run = currentRun }, effect, targetId, variables,
                            $"{path}:{repeat}:{targetId}");
                        if (value.IsFailure) return Result.Failure(value.Error);
                        calculation = value.Value.Calculation;
                        var status = ResolveAppliedStatus(effect, request.ContentRevision);
                        if (status.IsFailure) return Result.Failure(status.Error);
                        var command = new ResolvedEffectCommand
                        {
                            EffectInstanceId = id, Definition = effect, SourceEntityId = request.SourceEntityId,
                            TargetEntityIds = [targetId], ResolvedValue = value.Value.Value, StatusDefinition = status.Value,
                            ContentRevision = request.ContentRevision,
                            Provenance = request.Provenance with { ComponentId = activeTriggerId }
                        };
                        if (RunEffectReducer.Supports(effect.Type))
                        {
                            if (currentRun == null) return Result.Failure("Effect requires an immutable run snapshot");
                            var appliedRun = RunEffectReducer.Apply(currentRun, current, command, _contentRuntimes, request.ContentRevision);
                            if (appliedRun.IsFailure) return Result.Failure(appliedRun.Error);
                            currentRun = appliedRun.Value.Run;
                            appliedRecords = [appliedRun.Value.Record];
                        }
                        else
                        {
                            var applied = _effects.Apply(current, [command]);
                            if (applied.IsFailure) return Result.Failure(applied.Error);
                            current = applied.Value.State;
                            appliedRecords = applied.Value.Records.ToImmutableArray();
                        }
                        records.AddRange(appliedRecords);
                        if (calculation != null) calculations.Add(calculation);
                    }
                    steps.Add(new()
                    {
                        Index = steps.Count, EffectInstanceId = id, TargetEntityId = targetId,
                        RepeatIndex = repeat, TargetIndex = targetIndex, Applied = applies,
                        SkipReason = applies ? null : !tagsPass ? "tags" : !effectPass || !chancePass ? "chance" : "condition",
                        ChanceRoll = roll, ContentRevision = request.ContentRevision,
                        Provenance = request.Provenance with { ComponentId = activeTriggerId },
                        Calculation = calculation, Applications = appliedRecords,
                        StateBeforeHash = before, StateAfterHash = CanonicalJson.ComputeHash(current),
                        RunBeforeHash = runBefore, RunAfterHash = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun)
                    });
                    if (!applies) continue;
                    foreach (var (child, childIndex) in (effect.ChainedEffects ?? []).Select((item, index) => (item, index)))
                    {
                        var childResult = ExecuteEffect(child, $"{path}:{repeat}:{targetIndex}.chain.{childIndex}", depth + 1, [targetId]);
                        if (childResult.IsFailure) return childResult;
                    }
                }
            }
            return Result.Success();
        }

        bool DrawChance(float chance, out double? roll)
        {
            roll = null;
            if (chance <= 0) return false;
            if (chance >= 1) return true;
            var draw = current.Determinism.DrawDouble();
            current = current with { Determinism = draw.Context };
            roll = draw.Value;
            return draw.Value < chance;
        }
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
                    ContentRevision = request.ContentRevision, Run = request.Run, Combat = request.Combat, Card = request.Card,
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
        if (target != EffectTarget.SELF && candidates.Any(id => combat.GetEntity(id)?.IsAlive != true))
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
