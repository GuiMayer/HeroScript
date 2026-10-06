using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Math;
using Core.StatusEffects;
using Core.Calculations;
using Core.CardZones;
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
    public ImmutableArray<StackPayloadLot> StackPayloadLots { get; init; } = [];
    public ImmutableSortedDictionary<string, CalculationQuantity> Quantities { get; init; } =
        ImmutableSortedDictionary<string, CalculationQuantity>.Empty.WithComparers(StringComparer.Ordinal);
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
    private readonly ICalculationSettlementPlanner _settlements;
    private readonly bool _allowUnconfiguredCalculations;
    private readonly ICardZoneFlowExecutor? _cardZoneFlows;

    public EffectTriggerExecutor(
        IRuntimeFormulaEvaluator formulas,
        IImmutableEffectProcessor effects,
        IContentRuntimeResolver? contentRuntimes = null,
        ICalculationEngine? calculations = null,
        ICalculationInfluenceProvider? influences = null,
        ICalculationSettlementPlanner? settlements = null,
        bool allowUnconfiguredCalculations = false,
        ICardZoneFlowExecutor? cardZoneFlows = null)
    {
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _contentRuntimes = contentRuntimes;
        _calculations = calculations;
        _influences = influences;
        _settlements = settlements ?? new CalculationSettlementPlanner();
        _allowUnconfiguredCalculations = allowUnconfiguredCalculations;
        _cardZoneFlows = cardZoneFlows;
    }

    public Result<EffectBatchResult> Execute(EffectTriggerExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Combat);
        ArgumentNullException.ThrowIfNull(request.Trigger);
        if (string.IsNullOrWhiteSpace(request.Trigger.TriggerId))
            return Result<EffectBatchResult>.Failure("TriggerId is required");
        if (request.Combat.GetActor(request.OwnerEntityId) == null)
            return Result<EffectBatchResult>.Failure($"Trigger owner not found: {request.OwnerEntityId}");
        if (request.Variables.Values.Any(value => !float.IsFinite(value)))
            return Result<EffectBatchResult>.Failure("Execution variables must be finite");
        if (request.Variables.Keys.Any(key => key.StartsWith("results.", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("parent.", StringComparison.OrdinalIgnoreCase)))
            return Result<EffectBatchResult>.Failure("Execution result namespaces cannot be supplied by the caller");
        var definitionErrors = EffectDefinitionValidator.Validate(
            request.PrefixCommands.Select(command => command.Definition).Concat(
                (request.Components.IsEmpty ? [request.Trigger] : request.Components).SelectMany(item => item.Effects)));
        if (!definitionErrors.IsEmpty)
            return Result<EffectBatchResult>.Failure(string.Join("; ", definitionErrors));

        var current = request.Combat;
        var currentRun = request.Run;
        var executionId = EffectResultContext.ExecutionId(request);
        var resultContext = new EffectResultContext();
        var records = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        var calculations = ImmutableArray.CreateBuilder<CalculationResult>();
        var steps = ImmutableArray.CreateBuilder<EffectExecutionStep>();
        var work = 0;
        foreach (var prefix in request.PrefixCommands)
        {
            if (++work > EffectExecutionLimits.MaximumSteps)
                return Result<EffectBatchResult>.Failure("Effect execution limit exceeded");
            var before = CanonicalJson.ComputeHash(current);
            var command = prefix with
            {
                Identity = EffectResultContext.Identity(executionId, prefix.Provenance.ComponentId ?? "prefix",
                    $"prefix:{steps.Count}", 0, prefix.TargetEntityIds.FirstOrDefault() ?? string.Empty, null,
                    prefix.Definition.OutputId)
            };
            var applied = _effects.Apply(current, [command]);
            if (applied.IsFailure) return Result<EffectBatchResult>.Failure(applied.Error);
            current = applied.Value.State;
            records.AddRange(applied.Value.Records);
            var accumulated = resultContext.Add(applied.Value.Records);
            if (accumulated.IsFailure) return Result<EffectBatchResult>.Failure(accumulated.Error);
            resultContext = accumulated.Value;
            steps.Add(new()
            {
                Index = steps.Count, EffectInstanceId = command.EffectInstanceId,
                Identity = command.Identity,
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
                var executed = ExecuteEffect(effect, $"{component.TriggerId}:{index}", 0, request.SelectedTargetEntityIds, null, null);
                if (executed.IsFailure) return Result<EffectBatchResult>.Failure(executed.Error);
            }
        }
        return Result<EffectBatchResult>.Success(new()
        {
            ExecutionId = executionId, State = current, Run = currentRun, Records = records.ToImmutable(),
            Calculations = calculations.ToImmutable(), Steps = steps.ToImmutable(),
            Fingerprint = CanonicalJson.ComputeHash(new { state = current, steps = steps.ToImmutable(), run = currentRun })
        });

        Result ExecuteEffect(EffectDefinition effect, string path, int depth, IReadOnlyList<string> selection,
            string? parentProcId, EffectApplicationRecord? parentApplication)
        {
            if (++work > EffectExecutionLimits.MaximumSteps || depth > EffectExecutionLimits.MaximumDepth)
                return Result.Failure("Effect execution limit exceeded");
            if (effect.Repeat < 1 || effect.Repeat > EffectExecutionLimits.MaximumRepeat)
                return Result.Failure($"Effect {path} repeat is outside execution limits");
            if (!float.IsFinite(effect.Chance) || effect.Chance is < 0 or > 1 || !Enum.IsDefined(effect.ChanceScope))
                return Result.Failure($"Effect {path} has an invalid chance policy");
            for (var repeat = 0; repeat < effect.Repeat; repeat++)
            {
                var beforeSelection = CanonicalJson.ComputeHash(current);
                var targets = EffectTargetResolver.Resolve(request.Combat, current, request.OwnerEntityId, selection, effect);
                if (targets.IsFailure) return Result.Failure(targets.Error);
                current = current with { Determinism = targets.Value.Context };
                foreach (var lostId in targets.Value.LostTargetIds)
                {
                    if (++work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                    steps.Add(new()
                    {
                        Index = steps.Count,
                        EffectInstanceId = $"{request.Provenance.SourceId}:{request.Trigger.TriggerId}:{path}:{repeat}:{lostId}:lost",
                        Identity = EffectResultContext.Identity(executionId, activeTriggerId, path, repeat,
                            lostId, parentProcId, effect.OutputId, parentApplication?.Identity?.ImpactId),
                        TargetEntityId = lostId, RepeatIndex = repeat, Applied = false,
                        SkipReason = targets.Value.StopRepeat ? "repeat_stopped" : "target_defeated",
                        TargetLossPolicy = effect.TargetLoss.Policy,
                        ContentRevision = request.ContentRevision,
                        Provenance = request.Provenance with { ComponentId = activeTriggerId },
                        StateBeforeHash = lostId == targets.Value.LostTargetIds[0] ? beforeSelection : CanonicalJson.ComputeHash(current),
                        StateAfterHash = CanonicalJson.ComputeHash(current),
                        RunBeforeHash = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun),
                        RunAfterHash = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun)
                    });
                }
                if (targets.Value.StopRepeat) break;
                if (targets.Value.TargetIds.IsEmpty) continue;
                var beforeChance = CanonicalJson.ComputeHash(current);
                double? effectRoll = null;
                var effectPass = effect.ChanceScope != EffectChanceScope.PerEffect || DrawChance(effect.Chance, out effectRoll);
                for (var targetIndex = 0; targetIndex < targets.Value.TargetIds.Length; targetIndex++)
                {
                    if (++work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                    var targetId = targets.Value.TargetIds[targetIndex];
                    var id = $"{request.Provenance.SourceId}:{request.Trigger.TriggerId}:{path}:{repeat}:{targetId}";
                    var identity = EffectResultContext.Identity(executionId, activeTriggerId, path, repeat, targetId,
                        parentProcId, effect.OutputId, parentApplication?.Identity?.ImpactId);
                    var before = targetIndex == 0
                        ? targets.Value.LostTargetIds.IsEmpty ? beforeSelection : beforeChance
                        : CanonicalJson.ComputeHash(current);
                    var runBefore = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun);
                    var variables = BuildVariables(request with { Run = currentRun }, current, targetId);
                    resultContext.AddVariables(variables, targetId);
                    if (parentApplication?.ResourceOutcome is { } parentOutcome)
                    {
                        var values = new Dictionary<string, double>
                        {
                            ["parent.requested_change"] = parentOutcome.RequestedChange,
                            ["parent.applied_change"] = parentOutcome.AppliedChange,
                            ["parent.limited_change"] = parentOutcome.LimitedChange,
                            ["parent.caused_defeat"] = parentOutcome.CausedDefeat ? 1 : 0
                        };
                        foreach (var (key, number) in values)
                        {
                            if (!float.IsFinite((float)number)) return Result.Failure($"Result fact {key} exceeds formula range");
                            variables[key] = (float)number;
                        }
                    }
                    if (parentApplication != null)
                        variables["parent.stack_delta"] = parentApplication.StackChanges.Sum(change =>
                            (float)change.CurrentStacks - change.PreviousStacks);
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
                    ImmutableArray<ResolvedEffectNumericParameter> resolvedParameters = [];
                    ImmutableArray<CalculationResult> payloadCalculations = [];
                    if (applies)
                    {
                        var value = ResolveValue(request with { Combat = current, Run = currentRun }, effect, targetId, variables,
                            $"{path}:{repeat}:{targetId}", activeTriggerId);
                        if (value.IsFailure) return Result.Failure(value.Error);
                        calculation = value.Value.Calculation;
                        resolvedParameters = value.Value.Parameters;
                        payloadCalculations = value.Value.PayloadCalculations;
                        work += value.Value.PayloadWork;
                        if (work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                        var status = ResolveAppliedStatus(effect, request.ContentRevision, request.Run?.ConfigName);
                        if (status.IsFailure) return Result.Failure(status.Error);
                        var payload = CapturePayload(request with { Combat = current, Run = currentRun,
                            Provenance = request.Provenance with { ComponentId = activeTriggerId } }, value.Value.Definition,
                            status.Value, targetId, identity.ImpactId, variables);
                        if (payload.IsFailure) return Result.Failure(payload.Error);
                        payloadCalculations = payloadCalculations.AddRange(payload.Value.Calculations);
                        work += payload.Value.Lot?.Parameters.Count ?? 0;
                        if (work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                        var command = new ResolvedEffectCommand
                        {
                            EffectInstanceId = id, Definition = value.Value.Definition, SourceEntityId = request.SourceEntityId,
                            Identity = identity,
                            Parameters = resolvedParameters,
                            TargetEntityIds = [targetId], ResolvedValue = value.Value.Value,
                            Calculation = value.Value.Calculation, Settlements = value.Value.Settlements,
                            StatusDefinition = status.Value,
                            PayloadLot = payload.Value.Lot,
                            ContentRevision = request.ContentRevision,
                            Provenance = request.Provenance with { ComponentId = activeTriggerId }
                        };
                        if (RunEffectReducer.Supports(effect.Type))
                        {
                            if (currentRun == null) return Result.Failure("Effect requires an immutable run snapshot");
                            var appliedRun = RunEffectReducer.Apply(currentRun, current, command,
                                _contentRuntimes, request.ContentRevision, _cardZoneFlows);
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
                        var accumulated = resultContext.Add(appliedRecords);
                        if (accumulated.IsFailure) return Result.Failure(accumulated.Error);
                        resultContext = accumulated.Value;
                        if (calculation != null) calculations.Add(calculation);
                        calculations.AddRange(resolvedParameters.Where(parameter => parameter.Parameter != EffectNumericParameter.Amount)
                            .Select(parameter => parameter.Calculation));
                        calculations.AddRange(payloadCalculations);
                    }
                    steps.Add(new()
                    {
                        Index = steps.Count, EffectInstanceId = id, TargetEntityId = targetId,
                        Identity = identity,
                        RepeatIndex = repeat, TargetIndex = targetIndex, Applied = applies,
                        Retargeted = targets.Value.Retargeted,
                        SkipReason = applies ? null : !tagsPass ? "tags" : !effectPass || !chancePass ? "chance" : "condition",
                        ChanceRoll = roll, ContentRevision = request.ContentRevision,
                        Provenance = request.Provenance with { ComponentId = activeTriggerId },
                        Calculation = calculation, Parameters = resolvedParameters, PayloadCalculations = payloadCalculations,
                        Applications = appliedRecords,
                        StateBeforeHash = before, StateAfterHash = CanonicalJson.ComputeHash(current),
                        RunBeforeHash = runBefore, RunAfterHash = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun)
                    });
                    if (!applies) continue;
                    foreach (var (child, childIndex) in (effect.ChainedEffects ?? []).Select((item, index) => (item, index)))
                    {
                        var parentRecord = appliedRecords.LastOrDefault(record => record.CalculationInfluenceId == null &&
                            record.TargetEntityId == targetId);
                        var childResult = ExecuteEffect(child, $"{path}:{repeat}:{targetIndex}.chain.{childIndex}", depth + 1,
                            [targetId], identity.ProcId, parentRecord);
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
        string revision, string? configName)
    {
        if (effect.Type != EffectType.APPLY_STATUS)
            return Result<StatusEffectDefinition?>.Success(null);
        if (string.IsNullOrWhiteSpace(effect.StatusId))
            return Result<StatusEffectDefinition?>.Failure("APPLY_STATUS requires statusId");
        if (_contentRuntimes == null || string.IsNullOrWhiteSpace(revision))
            return Result<StatusEffectDefinition?>.Failure(
                $"Trigger cannot resolve applied status {effect.StatusId} without pinned content");
        var runtime = _contentRuntimes.Resolve(revision, configName);
        if (runtime.IsFailure)
            return Result<StatusEffectDefinition?>.Failure(runtime.Error);
        var definition = runtime.Value.GetDefinition<StatusEffectDefinition>("status-effects", effect.StatusId);
        return definition.IsFailure
            ? Result<StatusEffectDefinition?>.Failure(definition.Error)
            : Result<StatusEffectDefinition?>.Success(definition.Value);
    }

    private Result<StackPayloadCapture> CapturePayload(EffectTriggerExecutionRequest request, EffectDefinition effect,
        StatusEffectDefinition? status, string targetId, string impactId, IReadOnlyDictionary<string, float> variables)
    {
        var definitions = status?.PayloadParameters ?? [];
        var policy = status?.PayloadReapply ?? StackPayloadReapplyPolicy.PreserveLots;
        var stacks = effect.StatusStacks ?? status?.DefaultStacks ?? 1;
        if (effect.Type == EffectType.APPLY_MODIFIER)
        {
            if (_contentRuntimes == null) return Result<StackPayloadCapture>.Failure("Pinned modifier runtime is unavailable");
            var runtime = _contentRuntimes.Resolve(request.ContentRevision, request.Run?.ConfigName);
            if (runtime.IsFailure) return Result<StackPayloadCapture>.Failure(runtime.Error);
            var modifier = runtime.Value.GetDefinition<Core.Combat.Modifiers.ScriptModifierDefinition>("modifiers", effect.ModifierId!);
            if (modifier.IsFailure) return Result<StackPayloadCapture>.Failure(modifier.Error);
            definitions = modifier.Value.PayloadParameters;
            policy = modifier.Value.PayloadReapply;
            stacks = effect.ModifierStacks ?? modifier.Value.DefaultStacks;
        }
        if (!definitions.IsEmpty)
        {
            if (_contentRuntimes == null || request.Run?.ResolvedMode == null)
                return Result<StackPayloadCapture>.Failure("Payload application requires a configured pinned runtime");
            var runtime = _contentRuntimes.Resolve(request.ContentRevision, request.Run.ConfigName);
            if (runtime.IsFailure) return Result<StackPayloadCapture>.Failure(runtime.Error);
            foreach (var parameter in definitions)
            {
                var pipeline = CalculationResolver.ResolvePipeline(effect with
                { CalculationChannel = parameter.Numeric.Channel, CalculationPipelineId = parameter.Numeric.PipelineId }, request.Run, runtime.Value);
                if (pipeline.IsFailure) return Result<StackPayloadCapture>.Failure(pipeline.Error);
                if (pipeline.Value.Stages.IsEmpty || pipeline.Value.UnitId != parameter.Numeric.UnitId ||
                    parameter.Numeric.StageIds.Any(id => !pipeline.Value.Stages.Any(stage => stage.StageId == id)))
                    return Result<StackPayloadCapture>.Failure("Payload application requires a compatible staged profile");
            }
        }
        var resolver = new CalculationResolver(_formulas, _contentRuntimes, _calculations, _influences, _allowUnconfiguredCalculations);
        return new StackPayloadResolver(resolver, _calculations ?? new CalculationEngine(_formulas))
            .Capture(request, effect, definitions, policy, targetId, stacks, impactId, variables);
    }

    private Result<ResolvedAmount> ResolveValue(
        EffectTriggerExecutionRequest request, EffectDefinition effect, string targetId,
        Dictionary<string, float> variables, string calculationSuffix, string componentId)
    {
        var context = new CalculationSourceContext
        {
            ContentRevision = request.ContentRevision, Run = request.Run, Combat = request.Combat, Card = request.Card,
            ComponentId = componentId,
            Actor = request.Combat.GetActor(request.SourceEntityId) ?? request.Combat.GetActor(request.OwnerEntityId),
            Target = request.Combat.GetActor(targetId), Variables = variables,
            Tags = request.Tags.Concat(effect.Tags).ToHashSet(StringComparer.Ordinal)
        };
        var resolver = new CalculationResolver(_formulas, _contentRuntimes, _calculations, _influences,
            _allowUnconfiguredCalculations);
        var calculationId = $"{request.Provenance.SourceId}:{request.Trigger.TriggerId}:{calculationSuffix}";
        var parameters = ImmutableArray.CreateBuilder<ResolvedEffectNumericParameter>();
        var payloadTraces = ImmutableArray.CreateBuilder<CalculationResult>();
        var payloadWork = 0;
        var bound = effect;
        ResolvedEffectAmount? amountOverride = null;
        foreach (var definition in effect.Parameters.OrderBy(parameter => parameter.Parameter))
        {
            var parameterContext = context;
            if (definition.InputQuantityId is { } inputId)
            {
                CalculationQuantity quantity;
                if (inputId.StartsWith("payload.", StringComparison.Ordinal))
                {
                    payloadWork += request.StackPayloadLots.Length + 1;
                    if (payloadWork > EffectExecutionLimits.MaximumSteps) return Result<ResolvedAmount>.Failure("Payload execution limit exceeded");
                    var evaluated = new StackPayloadResolver(resolver, _calculations ?? new CalculationEngine(_formulas))
                        .Evaluate(request, targetId, inputId[8..], $"{calculationId}:input:{definition.Parameter}");
                    if (evaluated.IsFailure) return Result<ResolvedAmount>.Failure(evaluated.Error);
                    quantity = evaluated.Value.Quantity;
                    payloadTraces.AddRange(evaluated.Value.Calculations);
                }
                else if (!request.Quantities.TryGetValue(inputId, out quantity!))
                    return Result<ResolvedAmount>.Failure($"Unknown input quantity: {inputId}");
                parameterContext = context with { InputQuantity = quantity };
            }
            var result = resolver.ResolveParameter(effect, definition, $"{calculationId}:{definition.Parameter}", parameterContext);
            if (result.IsFailure) return Result<ResolvedAmount>.Failure(result.Error);
            var parameter = new ResolvedEffectNumericParameter { Parameter = definition.Parameter, Calculation = result.Value.Calculation! };
            var binding = EffectNumericParameters.Bind(bound, parameter);
            if (binding.IsFailure) return Result<ResolvedAmount>.Failure(binding.Error);
            bound = binding.Value;
            parameters.Add(parameter);
            if (definition.Parameter == EffectNumericParameter.Amount)
            {
                amountOverride = result.Value;
                context = parameterContext;
            }
        }
        var resolved = amountOverride == null ? resolver.Resolve(effect, calculationId, context)
            : Result<ResolvedEffectAmount>.Success(amountOverride);
        if (resolved.IsFailure) return Result<ResolvedAmount>.Failure(resolved.Error);
        if (resolved.Value.Calculation == null || resolved.Value.Pipeline == null)
            return Result<ResolvedAmount>.Success(new(resolved.Value.Value, null, [], bound, parameters.ToImmutable(),
                payloadTraces.ToImmutable(), payloadWork));
        var planned = _settlements.Plan(resolved.Value.Calculation, resolved.Value.Pipeline,
            context with { Tags = CalculationResolver.NormalizeTags(effect, context.Tags), Pipeline = resolved.Value.Pipeline });
        return planned.IsFailure
            ? Result<ResolvedAmount>.Failure(planned.Error)
            : Result<ResolvedAmount>.Success(new(resolved.Value.Value, resolved.Value.Calculation,
                planned.Value.ToImmutableArray(), bound, parameters.ToImmutable(), payloadTraces.ToImmutable(), payloadWork));
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
        var source = combat.GetActor(request.SourceEntityId) ?? combat.GetActor(request.OwnerEntityId)!;
        return GameplayFormulaContext.Build(source, combat.GetActor(targetId),
            combat.GetActor(request.OwnerEntityId)!, request.Run, request.Variables);
    }

    private sealed record ResolvedAmount(
        float Value,
        CalculationResult? Calculation,
        ImmutableArray<ResolvedCalculationSettlement> Settlements,
        EffectDefinition Definition,
        ImmutableArray<ResolvedEffectNumericParameter> Parameters,
        ImmutableArray<CalculationResult> PayloadCalculations, int PayloadWork);
}
