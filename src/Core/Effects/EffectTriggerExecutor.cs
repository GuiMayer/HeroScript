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
            key.StartsWith("continuation.", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("rolls.", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("parent.", StringComparison.OrdinalIgnoreCase)))
            return Result<EffectBatchResult>.Failure("Execution result namespaces cannot be supplied by the caller");
        if (request.Quantities.Keys.Any(key => key.StartsWith("continuation.", StringComparison.OrdinalIgnoreCase)))
            return Result<EffectBatchResult>.Failure("Continuation quantities cannot be supplied by the caller");
        var rootEffects = request.PrefixCommands.Select(command => command.Definition).Concat(
            (request.Components.IsEmpty ? [request.Trigger] : request.Components).SelectMany(item => item.Effects)).ToArray();
        var recipes = new Dictionary<string, CondensationRecipeDefinition>(StringComparer.Ordinal);
        var referenceErrors = new List<string>();
        var definitionErrors = EffectDefinitionValidator.Validate(rootEffects, (effect, _) =>
        {
            var distribution = ValidateSequenceProfiles(effect, request);
            if (distribution.IsFailure) referenceErrors.Add(distribution.Error);
            var continuation = ValidateContinuationProfiles(effect, request);
            if (continuation.IsFailure) referenceErrors.Add(continuation.Error);
            if (effect.Type != EffectType.CONDENSE_STACKS || string.IsNullOrWhiteSpace(effect.CondensationRecipeId)) return;
            var resolved = ResolveRecipe(effect.CondensationRecipeId, request);
            if (resolved.IsFailure) referenceErrors.Add(resolved.Error);
            else recipes[effect.CondensationRecipeId] = resolved.Value;
        });
        if (referenceErrors.Count > 0) return Result<EffectBatchResult>.Failure(string.Join("; ", referenceErrors));
        definitionErrors = definitionErrors.AddRange(EffectDefinitionValidator.Validate(rootEffects.Concat(
            recipes.OrderBy(pair => pair.Key, StringComparer.Ordinal).SelectMany(pair => pair.Value.Effects)), (effect, _) =>
            {
                var distribution = ValidateSequenceProfiles(effect, request);
                if (distribution.IsFailure) referenceErrors.Add(distribution.Error);
                var continuation = ValidateContinuationProfiles(effect, request);
                if (continuation.IsFailure) referenceErrors.Add(continuation.Error);
            }));
        if (referenceErrors.Count > 0) return Result<EffectBatchResult>.Failure(string.Join("; ", referenceErrors));
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
        var attemptedRecipes = new Dictionary<string, EffectExecutionIdentity>(StringComparer.Ordinal);
        var activeQuantities = request.Quantities;
        var sharedBudgets = new Dictionary<string, ImmutableArray<EffectSequenceBudget>>(StringComparer.Ordinal);
        var scopedRolls = new Dictionary<string, (bool Pass, double? Roll)>(StringComparer.Ordinal);
        var attemptedEffects = new HashSet<string>(StringComparer.Ordinal);
        (CombatState Combat, RunState? Run)? numericSnapshot = null;
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
                Index = steps.Count,
                EffectInstanceId = command.EffectInstanceId,
                Identity = command.Identity,
                TargetEntityId = command.TargetEntityIds.FirstOrDefault() ?? string.Empty,
                Applied = true,
                ContentRevision = request.ContentRevision,
                Provenance = command.Provenance,
                Applications = applied.Value.Records.ToImmutableArray(),
                StateBeforeHash = before,
                StateAfterHash = CanonicalJson.ComputeHash(current)
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
            ExecutionId = executionId,
            State = current,
            Run = currentRun,
            Records = records.ToImmutable(),
            Calculations = calculations.ToImmutable(),
            Steps = steps.ToImmutable(),
            Fingerprint = CanonicalJson.ComputeHash(new { state = current, steps = steps.ToImmutable(), run = currentRun })
        });

        Result ExecuteEffect(EffectDefinition effect, string path, int depth, IReadOnlyList<string> selection,
            string? parentProcId, EffectApplicationRecord? parentApplication, string? forcedProcId = null,
            string? forcedParentProcId = null, EffectSequenceFrame? parentSequence = null, string? budgetKey = null,
            string? definitionPath = null, int continuationHop = 0, ImmutableArray<string> continuationVisited = default,
            IReadOnlyDictionary<string, float>? inheritedRandomFacts = null)
        {
            if (++work > EffectExecutionLimits.MaximumSteps || depth > EffectExecutionLimits.MaximumDepth)
                return Result.Failure("Effect execution limit exceeded");
            definitionPath ??= path;
            if (effect.ExecutionScope != EffectExecutionScope.EveryInvocation)
            {
                if (effect.ExecutionScope == EffectExecutionScope.OncePerParentProc && parentProcId == null)
                    return Result.Failure("OncePerParentProc requires an enclosing proc");
                var scopeId = effect.ExecutionScope == EffectExecutionScope.OncePerAction ? executionId : parentProcId!;
                if (!attemptedEffects.Add($"{scopeId}:{(effect.ExecutionGroupId == null ? definitionPath : "group:" + effect.ExecutionGroupId)}"))
                {
                    steps.Add(new() { Index = steps.Count, EffectInstanceId = $"{path}:once", Applied = false,
                        SkipReason = "scope_already_attempted", ContentRevision = request.ContentRevision, Provenance = request.Provenance,
                        StateBeforeHash = CanonicalJson.ComputeHash(current), StateAfterHash = CanonicalJson.ComputeHash(current) });
                    return Result.Success();
                }
            }
            if (effect.Repeat < 1 || effect.Repeat > EffectExecutionLimits.MaximumRepeat)
                return Result.Failure($"Effect {path} repeat is outside execution limits");
            if (!float.IsFinite(effect.Chance) || effect.Chance is < 0 or > 1 || !Enum.IsDefined(effect.ChanceScope))
                return Result.Failure($"Effect {path} has an invalid chance policy");
            var sequenceRequest = request with
            {
                Combat = numericSnapshot?.Combat ?? current,
                Run = numericSnapshot?.Run ?? currentRun,
                Quantities = activeQuantities
            };
            var fromParent = effect.Parameters.Any(parameter => parameter.Distribution?.Scope == EffectDistributionScope.ParentSequence);
            if (fromParent && parentSequence == null) return Result.Failure("ParentSequence budget requires an enclosing effect sequence");
            var frameId = fromParent ? budgetKey! : path;
            var frameCount = fromParent ? parentSequence!.Count : effect.Repeat;
            var frameSnapshot = fromParent ? parentSequence!.Snapshot : sequenceRequest;
            ImmutableArray<EffectSequenceBudget> sequenceBudgets = [];
            var budgetReady = false;
            if (fromParent && sharedBudgets.TryGetValue(frameId, out var shared))
            { sequenceBudgets = shared; budgetReady = true; }
            bool? sequencePass = null;
            double? sequenceRoll = null;
            var stopSequence = false;
            ImmutableArray<EffectImpactShare> Shares(int index) => sequenceBudgets.Select(budget => new EffectImpactShare
            { Parameter = budget.Parameter, DistributionId = budget.Allocation.DistributionId,
                Share = budget.Allocation.Shares[fromParent ? parentSequence!.Index : index] }).ToImmutableArray();
            if (effect.Type == EffectType.CONDENSE_STACKS)
            {
                if (attemptedRecipes.TryGetValue(effect.CondensationRecipeId!, out var previous))
                {
                    steps.Add(new()
                    {
                        Index = steps.Count,
                        EffectInstanceId = $"{path}:once",
                        Identity = previous with
                        { ImpactId = CanonicalJson.ComputeHash(new { previous.ProcId, path, reason = "already_attempted" }) },
                        Applied = false,
                        SkipReason = "already_attempted",
                        ContentRevision = request.ContentRevision,
                        Provenance = request.Provenance,
                        StateBeforeHash = CanonicalJson.ComputeHash(current),
                        StateAfterHash = CanonicalJson.ComputeHash(current),
                        RunBeforeHash = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun),
                        RunAfterHash = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun)
                    });
                    return Result.Success();
                }
                attemptedRecipes.Add(effect.CondensationRecipeId!, EffectResultContext.Identity(executionId, activeTriggerId,
                    path, 0, selection.FirstOrDefault() ?? request.OwnerEntityId, parentProcId, effect.OutputId, parentApplication?.Identity?.ImpactId));
            }
            for (var repeat = 0; repeat < effect.Repeat && !stopSequence; repeat++)
            {
                var beforeSelection = CanonicalJson.ComputeHash(current);
                var targets = EffectTargetResolver.Resolve(request.Combat, current, request.OwnerEntityId, selection, effect);
                if (targets.IsFailure) return Result.Failure(targets.Error);
                current = current with { Determinism = targets.Value.Context };
                foreach (var lostId in targets.Value.LostTargetIds)
                {
                    if (++work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                    var lostIdentity = EffectResultContext.Identity(executionId, activeTriggerId, path, repeat,
                        lostId, parentProcId, effect.OutputId, parentApplication?.Identity?.ImpactId);
                    if (forcedProcId != null) lostIdentity = lostIdentity with
                    {
                        ProcId = forcedProcId,
                        ParentProcId = forcedParentProcId,
                        ImpactId = CanonicalJson.ComputeHash(new { forcedProcId, path, repeat, lostId, reason = "lost" })
                    };
                    steps.Add(new()
                    {
                        Index = steps.Count,
                        EffectInstanceId = $"{request.Provenance.SourceId}:{request.Trigger.TriggerId}:{path}:{repeat}:{lostId}:lost",
                        Identity = lostIdentity,
                        TargetEntityId = lostId,
                        RepeatIndex = repeat,
                        Applied = false,
                        ImpactShares = Shares(repeat),
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
                if ((effect.Parameters.Any(parameter => parameter.Distribution != null) ||
                    (effect.ChainedEffects ?? []).Any(child => child.Parameters.Any(parameter =>
                        parameter.Distribution?.Scope == EffectDistributionScope.ParentSequence))) && targets.Value.TargetIds.Length != 1)
                    return Result.Failure("Sequence distribution requires exactly one target per impact");
                if (effect.Type == EffectType.CONDENSE_STACKS && targets.Value.TargetIds.Length != 1)
                    return Result.Failure("Condensation requires one activation target; configure additional targets in the recipe effects");
                var beforeChance = CanonicalJson.ComputeHash(current);
                var procId = forcedProcId ?? EffectResultContext.Identity(executionId, activeTriggerId, path, repeat,
                    string.Empty, parentProcId, effect.OutputId, parentApplication?.Identity?.ImpactId).ProcId;
                double? effectRoll = null;
                var effectPass = effect.ChanceScope != EffectChanceScope.PerEffect || DrawChance(effect.Chance, out effectRoll);
                if (effect.ChanceScope == EffectChanceScope.PerSequence)
                {
                    sequencePass ??= DrawChance(effect.Chance, out sequenceRoll);
                    effectPass = sequencePass.Value;
                    effectRoll = sequenceRoll;
                }
                if (effect.ChanceScope is EffectChanceScope.PerAction or EffectChanceScope.PerProc)
                {
                    var chanceKey = effect.ChanceGroupId == null ? definitionPath : "group:" + effect.ChanceGroupId;
                    var chance = ScopedChance($"chance:{chanceKey}:{(effect.ChanceScope == EffectChanceScope.PerAction ? executionId : parentProcId ?? procId)}", effect.Chance);
                    effectPass = chance.Pass; effectRoll = chance.Roll;
                }
                for (var targetIndex = 0; targetIndex < targets.Value.TargetIds.Length; targetIndex++)
                {
                    if (++work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                    var targetId = targets.Value.TargetIds[targetIndex];
                    var id = $"{request.Provenance.SourceId}:{request.Trigger.TriggerId}:{path}:{repeat}:{targetId}";
                    var identity = EffectResultContext.Identity(executionId, activeTriggerId, path, repeat, targetId,
                        parentProcId, effect.OutputId, parentApplication?.Identity?.ImpactId);
                    if (forcedProcId != null) identity = identity with
                    {
                        ProcId = forcedProcId,
                        ParentProcId = forcedParentProcId,
                        ImpactId = CanonicalJson.ComputeHash(new { forcedProcId, path, repeat, targetId })
                    };
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
                    var beforeRetargeted = false;
                    CalculationResult? calculation = null;
                    ImmutableArray<EffectApplicationRecord> appliedRecords = [];
                    ImmutableArray<ResolvedEffectNumericParameter> resolvedParameters = [];
                    ImmutableArray<CalculationResult> payloadCalculations = [];
                    CondensationOutcome? condensation = null;
                    string? condensationSkip = null;
                    ImmutableArray<EffectSequenceBudget> capturedBudgets = [];
                    if (inheritedRandomFacts != null)
                        foreach (var pair in inheritedRandomFacts) variables[pair.Key] = pair.Value;
                    var randomInputs = ImmutableArray.CreateBuilder<EffectRandomInputResult>();
                    if (applies)
                    {
                        work += effect.RandomInputs.Length;
                        if (work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                        foreach (var input in effect.RandomInputs.OrderBy(input => input.InputId, StringComparer.Ordinal))
                        {
                            var scopeId = input.Scope switch { EffectRandomScope.Action => executionId,
                                EffectRandomScope.ParentProc => parentProcId ?? procId, _ => identity.ImpactId };
                            var inputKey = input.GroupId == null ? $"{definitionPath}:{input.InputId}" : "group:" + input.GroupId;
                            var sampled = ScopedChance($"input:{inputKey}:{scopeId}", input.Chance);
                            var fact = new EffectRandomInputResult { InputId = input.InputId, Scope = input.Scope, ScopeId = scopeId,
                                Success = sampled.Pass, Roll = sampled.Roll };
                            randomInputs.Add(fact); variables[$"rolls.{input.InputId}.success"] = sampled.Pass ? 1 : 0;
                        }
                    }
                    (CombatState Combat, RunState? Run) preConsumption = (current, currentRun);
                    if (applies && effect.Type == EffectType.CONDENSE_STACKS)
                    {
                        var resolver = new CalculationResolver(_formulas, _contentRuntimes, _calculations, _influences, _allowUnconfiguredCalculations);
                        var engine = _calculations ?? new CalculationEngine(_formulas);
                        var plan = new CondensationPlanner(engine, new StackPayloadResolver(resolver, engine)).Plan(
                            request with { Combat = current, Run = currentRun }, request.Combat, request.Run,
                            recipes[effect.CondensationRecipeId!], targetId, identity.ProcId);
                        if (plan.IsFailure) return Result.Failure(plan.Error);
                        work += plan.Value.Work;
                        if (work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                        condensation = plan.Value.Outcome;
                        condensationSkip = plan.Value.SkipReason;
                        applies = condensation != null;
                        current = plan.Value.Combat;
                        currentRun = plan.Value.Run;
                        payloadCalculations = plan.Value.Calculations;
                        calculations.AddRange(payloadCalculations);
                        if (applies)
                        {
                            appliedRecords = [new() { EffectInstanceId = id, EffectType = effect.Type, TargetEntityId = targetId,
                                Identity = identity, Condensation = condensation, StackChanges = plan.Value.Changes,
                                Provenance = request.Provenance with { ComponentId = activeTriggerId } }];
                            records.AddRange(appliedRecords);
                            var accumulated = resultContext.Add(appliedRecords);
                            if (accumulated.IsFailure) return Result.Failure(accumulated.Error);
                            resultContext = accumulated.Value;
                        }
                    }
                    else if (applies)
                    {
                        if (!budgetReady && effect.Parameters.Any(parameter => parameter.Distribution != null))
                        {
                            var planner = new EffectSequenceBudgetPlanner(new CalculationResolver(_formulas, _contentRuntimes,
                                _calculations, _influences), _calculations!);
                            var captureVariables = frameSnapshot.Variables.ToDictionary(pair => pair.Key, pair => pair.Value);
                            foreach (var input in randomInputs.Where(input => input.Scope == EffectRandomScope.Action))
                                captureVariables[$"rolls.{input.InputId}.success"] = input.Success ? 1 : 0;
                            var capture = planner.Capture(frameSnapshot with { Variables = captureVariables }, effect,
                                $"{executionId}:{frameId}", activeTriggerId, frameCount);
                            if (capture.IsFailure) return Result.Failure(capture.Error);
                            sequenceBudgets = capture.Value;
                            capturedBudgets = sequenceBudgets;
                            budgetReady = true;
                            if (fromParent) sharedBudgets.Add(frameId, sequenceBudgets);
                            work += sequenceBudgets.Sum(budget => budget.Allocation.Shares.Length + 1);
                            if (work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                            calculations.AddRange(sequenceBudgets.Select(budget => budget.Capture));
                        }
                        var zeroSlot = Shares(repeat).Any(share => share.Parameter != EffectNumericParameter.Amount && share.Share.Quantity.Value == 0);
                        var childStart = steps.Count;
                        if (!zeroSlot)
                        foreach (var (child, childIndex) in (effect.ChainedEffects ?? []).Select((item, index) => (item, index))
                            .Where(pair => pair.item.ChildTiming == EffectChildTiming.BeforeParentImpact))
                        {
                            var beforeChild = ExecuteEffect(child, $"{path}:{repeat}:{targetIndex}.before.{childIndex}", depth + 1,
                                [targetId], identity.ProcId, null, forcedProcId, forcedParentProcId,
                                new(frameId, frameSnapshot, frameCount, fromParent ? parentSequence!.Index : repeat),
                                $"{frameId}.chain.{childIndex}", $"{definitionPath}.chain.{childIndex}");
                            if (beforeChild.IsFailure) return beforeChild;
                        }
                        if (steps.Count > childStart)
                        {
                            steps[childStart] = steps[childStart] with { StateBeforeHash = before, RunBeforeHash = runBefore };
                            before = CanonicalJson.ComputeHash(current);
                            runBefore = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun);
                            variables = BuildVariables(request with { Run = currentRun, Variables = variables }, current, targetId);
                            resultContext.AddVariables(variables, targetId);
                            if (effect.Target != EffectTarget.SELF && current.GetActor(targetId)?.IsAlive != true)
                            {
                                var redirected = EffectTargetResolver.Resolve(request.Combat, current, request.OwnerEntityId, selection, effect);
                                if (redirected.IsFailure) return Result.Failure(redirected.Error);
                                current = current with { Determinism = redirected.Value.Context };
                                stopSequence = redirected.Value.StopRepeat;
                                if (redirected.Value.TargetIds.IsEmpty)
                                { applies = false; condensationSkip = stopSequence ? "repeat_stopped" : "target_defeated"; }
                                else
                                {
                                    if (redirected.Value.TargetIds.Length != 1) return Result.Failure("Before-impact retarget requires one target");
                                    targetId = redirected.Value.TargetIds[0]; beforeRetargeted = true;
                                    variables = BuildVariables(request with { Run = currentRun, Variables = variables }, current, targetId);
                                }
                            }
                        }
                        if (applies)
                        {
                        var numericRequest = request with
                        {
                            Combat = numericSnapshot?.Combat ?? current,
                            Run = numericSnapshot?.Run ?? currentRun,
                            Quantities = activeQuantities
                        };
                        var numericVariables = numericSnapshot == null ? variables : BuildVariables(numericRequest, numericRequest.Combat, targetId);
                        if (numericSnapshot != null)
                            foreach (var pair in variables.Where(pair => pair.Key.StartsWith("results.", StringComparison.Ordinal) ||
                                pair.Key.StartsWith("parent.", StringComparison.Ordinal) || pair.Key.StartsWith("rolls.", StringComparison.Ordinal) || pair.Key is "repeat_index" or "target_index"))
                                numericVariables[pair.Key] = pair.Value;
                        var value = ResolveValue(numericRequest, effect, targetId, numericVariables,
                            $"{path}:{repeat}:{targetId}", activeTriggerId, Shares(repeat),
                            request with { Combat = current, Run = currentRun, Quantities = activeQuantities });
                        if (value.IsFailure) return Result.Failure(value.Error);
                        calculation = value.Value.Calculation;
                        resolvedParameters = value.Value.Parameters;
                        payloadCalculations = value.Value.PayloadCalculations;
                        work += value.Value.PayloadWork;
                        if (work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                        if (value.Value.ZeroContribution)
                        {
                            applies = false;
                            condensationSkip = "zero_contribution";
                        }
                        else
                        {
                            var status = ResolveAppliedStatus(effect, request.ContentRevision, request.Run?.ConfigName);
                            if (status.IsFailure) return Result.Failure(status.Error);
                            var payload = CapturePayload(numericRequest with
                            { Provenance = request.Provenance with { ComponentId = activeTriggerId } }, value.Value.Definition,
                                status.Value, targetId, identity.ImpactId, numericVariables);
                            if (payload.IsFailure) return Result.Failure(payload.Error);
                            payloadCalculations = payloadCalculations.AddRange(payload.Value.Calculations);
                            work += payload.Value.Lot?.Parameters.Count ?? 0;
                            if (work > EffectExecutionLimits.MaximumSteps) return Result.Failure("Effect execution limit exceeded");
                            var command = new ResolvedEffectCommand
                            {
                                EffectInstanceId = id,
                                Definition = value.Value.Definition,
                                SourceEntityId = request.SourceEntityId,
                                Identity = identity,
                                Parameters = resolvedParameters,
                                TargetEntityIds = [targetId],
                                ResolvedValue = value.Value.Value,
                                Calculation = value.Value.Calculation,
                                Settlements = value.Value.Settlements,
                                StatusDefinition = status.Value,
                                PayloadLot = payload.Value.Lot,
                                ContentRevision = request.ContentRevision,
                                Provenance = request.Provenance with { ComponentId = activeTriggerId }
                            };
                            if (RunEffectReducer.Supports(effect))
                            {
                                if (currentRun == null) return Result.Failure("Effect requires an immutable run snapshot");
                                var appliedRun = RunEffectReducer.Apply(currentRun, current, command,
                                    _contentRuntimes, request.ContentRevision, _cardZoneFlows);
                                if (appliedRun.IsFailure) return Result.Failure(appliedRun.Error);
                                currentRun = appliedRun.Value.Run;
                                appliedRecords = [appliedRun.Value.Record];
                                if (appliedRun.Value.Record.AttributeOutcome != null)
                                {
                                    var synchronized = PersistentPlayerTransitions.Materialize(currentRun, current);
                                    if (synchronized.IsFailure) return Result.Failure(synchronized.Error);
                                    current = synchronized.Value;
                                }
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
                        }
                        if (calculation != null) calculations.Add(calculation);
                        calculations.AddRange(resolvedParameters.Where(parameter => parameter.Parameter != EffectNumericParameter.Amount)
                            .Select(parameter => parameter.Calculation));
                        calculations.AddRange(payloadCalculations);
                        }
                    }
                    var completedStepIndex = steps.Count;
                    steps.Add(new()
                    {
                        Index = steps.Count,
                        EffectInstanceId = id,
                        TargetEntityId = targetId,
                        Identity = identity,
                        RepeatIndex = repeat,
                        TargetIndex = targetIndex,
                        Applied = applies,
                        Retargeted = targets.Value.Retargeted || beforeRetargeted,
                        SkipReason = applies ? null : condensationSkip ?? (!tagsPass ? "tags" : !effectPass || !chancePass ? "chance" : "condition"),
                        ChanceRoll = roll,
                        RandomInputs = randomInputs.ToImmutable(),
                        ContentRevision = request.ContentRevision,
                        Provenance = request.Provenance with { ComponentId = activeTriggerId },
                        Calculation = calculation,
                        Parameters = resolvedParameters,
                        PayloadCalculations = payloadCalculations,
                        Condensation = condensation,
                        SequenceBudgets = capturedBudgets,
                        ImpactShares = Shares(repeat),
                        Applications = appliedRecords,
                        StateBeforeHash = before,
                        StateAfterHash = CanonicalJson.ComputeHash(current),
                        RunBeforeHash = runBefore,
                        RunAfterHash = currentRun == null ? null : CanonicalJson.ComputeHash(currentRun)
                    });
                    if (!applies) continue;
                    if (condensation != null)
                    {
                        var recipe = recipes[effect.CondensationRecipeId!];
                        var previousQuantities = activeQuantities;
                        var previousSnapshot = numericSnapshot;
                        var previousTriggerId = activeTriggerId;
                        activeQuantities = activeQuantities.SetItems(condensation.Inputs);
                        var consumedState = current;
                        var consumedRun = currentRun;
                        // Selection and influences are independent policies. BeforeConsumption freezes
                        // numeric reads to the pre-consumption world; mutations still use the live candidate.
                        numericSnapshot = recipe.EvaluationTiming == CondensationEvaluationTiming.BeforeConsumption ? preConsumption : null;
                        for (var recipeIndex = 0; recipeIndex < recipe.Effects.Length; recipeIndex++)
                        {
                            activeTriggerId = $"recipe:{recipe.RecipeId}:{recipeIndex}";
                            var activated = ExecuteEffect(recipe.Effects[recipeIndex], $"{path}.recipe.{recipeIndex}", depth + 1,
                                [targetId], identity.ProcId, appliedRecords[0], identity.ProcId, identity.ParentProcId,
                                definitionPath: $"{definitionPath}.recipe.{recipeIndex}");
                            if (activated.IsFailure) return activated;
                        }
                        activeQuantities = previousQuantities;
                        numericSnapshot = previousSnapshot;
                        activeTriggerId = previousTriggerId;
                        if (recipe.ZeroApplication == CondensationZeroPolicy.Fail &&
                            CanonicalJson.ComputeHash(current with { Determinism = consumedState.Determinism }) == CanonicalJson.ComputeHash(consumedState) &&
                            (consumedRun == null || CanonicalJson.ComputeHash(currentRun! with { Determinism = consumedRun.Determinism }) == CanonicalJson.ComputeHash(consumedRun)))
                            return Result.Failure("Condensation activation applied no state change");
                    }
                    foreach (var (child, childIndex) in (effect.ChainedEffects ?? []).Select((item, index) => (item, index))
                        .Where(pair => pair.item.ChildTiming == EffectChildTiming.AfterParentImpact))
                    {
                        var parentRecord = appliedRecords.LastOrDefault(record => record.CalculationInfluenceId == null &&
                            record.TargetEntityId == targetId);
                        var childResult = ExecuteEffect(child, $"{path}:{repeat}:{targetIndex}.chain.{childIndex}", depth + 1,
                            [targetId], identity.ProcId, parentRecord, forcedProcId, forcedParentProcId,
                            new(frameId, frameSnapshot, frameCount, fromParent ? parentSequence!.Index : repeat),
                            $"{frameId}.chain.{childIndex}", $"{definitionPath}.chain.{childIndex}");
                        if (childResult.IsFailure) return childResult;
                    }
                    if (effect.Continuation != null && calculation != null)
                    {
                        var primary = appliedRecords.LastOrDefault(record => record.CalculationInfluenceId == null && record.TargetEntityId == targetId);
                        if (primary == null) return Result.Failure("Continuation requires an observed primary resource application");
                        var visited = (continuationVisited.IsDefault ? ImmutableArray<string>.Empty : continuationVisited).Add(targetId);
                        var beforeContinuation = CanonicalJson.ComputeHash(current);
                        var planner = new EffectContinuationPlanner(new CalculationResolver(_formulas, _contentRuntimes, _calculations, _influences));
                        var planned = planner.Plan(request with { Combat = current, Run = currentRun }, effect, primary, calculation,
                            continuationHop + 1, visited, variables, activeTriggerId);
                        if (planned.IsFailure) return Result.Failure(planned.Error);
                        steps[completedStepIndex] = steps[completedStepIndex] with { Continuation = planned.Value.Trace };
                        if (planned.Value.Trace.Overflow is { } overflow) calculations.Add(overflow);
                        current = planned.Value.Combat;
                        if (planned.Value.Effect is { } next)
                        {
                            var previousQuantities = activeQuantities;
                            var previousSnapshot = numericSnapshot;
                            activeQuantities = activeQuantities.SetItem("continuation.budget", planned.Value.Quantity!);
                            // The transported value already contains its source. Only live next-target stages run.
                            numericSnapshot = null;
                            var firstContinuationStep = steps.Count;
                            var carried = ExecuteEffect(next, $"{path}:{repeat}:{targetIndex}.hop.{continuationHop + 1}", depth + 1,
                                [planned.Value.Trace.ToEntityId!], identity.ProcId, primary, identity.ProcId, identity.ParentProcId,
                                definitionPath: definitionPath, continuationHop: continuationHop + 1, continuationVisited: visited,
                                inheritedRandomFacts: variables.Where(pair => pair.Key.StartsWith("rolls.", StringComparison.Ordinal))
                                    .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
                            activeQuantities = previousQuantities;
                            numericSnapshot = previousSnapshot;
                            if (carried.IsFailure) return carried;
                            if (steps.Count > firstContinuationStep)
                                steps[firstContinuationStep] = steps[firstContinuationStep] with { StateBeforeHash = beforeContinuation };
                        }
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

        (bool Pass, double? Roll) ScopedChance(string key, float chance)
        {
            if (scopedRolls.TryGetValue(key, out var previous)) return previous;
            var pass = DrawChance(chance, out var roll);
            var result = (pass, roll); scopedRolls.Add(key, result); return result;
        }
    }

    private Result ValidateContinuationProfiles(EffectDefinition effect, EffectTriggerExecutionRequest request)
    {
        if (effect.Continuation == null) return Result.Success();
        if (_contentRuntimes == null || _calculations == null || _influences == null || request.Run?.ResolvedMode == null)
            return Result.Failure("Continuation requires configured pinned calculation services");
        var runtime = _contentRuntimes.Resolve(request.ContentRevision, request.Run.ConfigName);
        if (runtime.IsFailure) return Result.Failure(runtime.Error);
        var amount = effect.Parameters.FirstOrDefault(parameter => parameter.Parameter == EffectNumericParameter.Amount);
        if (amount == null) return Result.Failure("Continuation requires an Amount parameter");
        var impact = CalculationResolver.ResolvePipeline(effect with { CalculationChannel = amount.Channel,
            CalculationPipelineId = amount.PipelineId }, request.Run, runtime.Value);
        var overflow = CalculationResolver.ResolvePipeline(effect with { CalculationChannel = effect.Continuation.OverflowChannel,
            CalculationPipelineId = effect.Continuation.OverflowPipelineId }, request.Run, runtime.Value);
        if (impact.IsFailure) return Result.Failure(impact.Error);
        if (overflow.IsFailure) return Result.Failure(overflow.Error);
        return EffectContinuationPlanner.ValidateProfiles(effect, impact.Value, overflow.Value);
    }

    private Result ValidateSequenceProfiles(EffectDefinition effect, EffectTriggerExecutionRequest request)
    {
        var parameters = effect.Parameters.Where(parameter => parameter.Distribution != null).ToArray();
        if (parameters.Length == 0) return Result.Success();
        if (_contentRuntimes == null || _calculations == null || _influences == null || request.Run?.ResolvedMode == null)
            return Result.Failure("Sequence distribution requires configured pinned calculation services");
        var runtime = _contentRuntimes.Resolve(request.ContentRevision, request.Run.ConfigName);
        if (runtime.IsFailure) return Result.Failure(runtime.Error);
        foreach (var parameter in parameters)
        {
            var pipeline = CalculationResolver.ResolvePipeline(effect with
            { CalculationChannel = parameter.Channel, CalculationPipelineId = parameter.PipelineId }, request.Run, runtime.Value);
            if (pipeline.IsFailure) return Result.Failure(pipeline.Error);
            var valid = EffectSequenceBudgetPlanner.ValidateProfile(parameter, pipeline.Value);
            if (valid.IsFailure) return valid;
        }
        return Result.Success();
    }

    private Result<CondensationRecipeDefinition> ResolveRecipe(string recipeId, EffectTriggerExecutionRequest request)
    {
        if (_contentRuntimes == null) return Result<CondensationRecipeDefinition>.Failure("Pinned condensation runtime is unavailable");
        var runtime = _contentRuntimes.Resolve(request.ContentRevision, request.Run?.ConfigName);
        if (runtime.IsFailure) return Result<CondensationRecipeDefinition>.Failure(runtime.Error);
        var recipe = runtime.Value.GetDefinition<CondensationRecipeDefinition>("condensation-recipes", recipeId);
        if (recipe.IsFailure) return recipe;
        var valid = CondensationRecipeValidator.Validate(recipe.Value);
        return valid.IsFailure ? Result<CondensationRecipeDefinition>.Failure(valid.Error) : recipe;
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
        Dictionary<string, float> variables, string calculationSuffix, string componentId,
        ImmutableArray<EffectImpactShare> shares = default, EffectTriggerExecutionRequest? liveRequest = null)
    {
        var context = new CalculationSourceContext
        {
            ContentRevision = request.ContentRevision,
            Run = request.Run,
            Combat = request.Combat,
            Card = request.Card,
            ComponentId = componentId,
            Actor = request.Combat.GetActor(request.SourceEntityId) ?? request.Combat.GetActor(request.OwnerEntityId),
            Target = request.Combat.GetActor(targetId),
            Variables = variables,
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
            var numericDefinition = definition;
            var parameterContext = context;
            if (definition.Distribution != null)
            {
                var share = shares.FirstOrDefault(item => item.Parameter == definition.Parameter);
                if (share == null) return Result<ResolvedAmount>.Failure("Sequence budget share is unavailable");
                if (share.Share.Quantity.Value == 0 && definition.Parameter != EffectNumericParameter.Amount)
                    return Result<ResolvedAmount>.Success(new(0, null, [], bound, parameters.ToImmutable(),
                        payloadTraces.ToImmutable(), payloadWork, true));
                numericDefinition = definition with { FlatValue = null, FormulaValue = null, InputQuantityId = null, Distribution = null };
                // Distribution opts into live per-impact defenses/settlements. A condensation
                // BeforeConsumption snapshot freezes the source capture, not expendable target capacity.
                var live = liveRequest ?? request;
                parameterContext = context with
                {
                    InputQuantity = share.Share.Quantity,
                    Combat = live.Combat,
                    Run = live.Run,
                    Actor = live.Combat.GetActor(live.SourceEntityId) ?? live.Combat.GetActor(live.OwnerEntityId),
                    Target = live.Combat.GetActor(targetId),
                    Variables = BuildVariables(live with { Variables = variables }, live.Combat, targetId)
                };
            }
            else if (definition.InputQuantityId is { } inputId)
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
            var result = resolver.ResolveParameter(effect, numericDefinition, $"{calculationId}:{definition.Parameter}", parameterContext);
            if (result.IsFailure) return Result<ResolvedAmount>.Failure(result.Error);
            var parameter = new ResolvedEffectNumericParameter { Parameter = definition.Parameter, Calculation = result.Value.Calculation! };
            if (definition.Distribution != null && definition.Parameter != EffectNumericParameter.Amount && result.Value.Value == 0)
                return Result<ResolvedAmount>.Success(new(0, null, [], bound, parameters.ToImmutable().Add(parameter),
                    payloadTraces.ToImmutable(), payloadWork, true));
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
        ImmutableArray<CalculationResult> PayloadCalculations, int PayloadWork, bool ZeroContribution = false);
}
