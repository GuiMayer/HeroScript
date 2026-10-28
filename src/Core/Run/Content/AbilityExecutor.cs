using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;

using Core.GameplayEvents;

namespace Core.Run.Content;

public sealed record AbilityExecutionRequest
{
    private ImmutableArray<string> _selectedTargetIds = [];

    public RunState Run { get; init; } = null!;
    public CombatState Combat { get; init; } = null!;
    public string ActionId { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedTargetIds
    {
        get => _selectedTargetIds;
        init => _selectedTargetIds = value?.ToImmutableArray() ?? [];
    }
    public string? CostOptionId { get; init; }
    public bool IgnoreConfiguredCosts { get; init; }
}

public sealed record AbilityExecutionResult
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal GameplayEffectObservation? Observation { get; init; }

    public ImmutableArray<EffectExecutionStep> Steps { get; init; } = [];
    public RunState? Run { get; init; }
    private ImmutableArray<EffectApplicationRecord> _applications = [];
    private ImmutableArray<Core.Calculations.CalculationResult> _calculations = [];

    public CombatState Combat { get; init; } = null!;
    public ActionDefinition Definition { get; init; } = null!;
    public CardPlayEvaluation Evaluation { get; init; } = null!;
    public IReadOnlyList<EffectApplicationRecord> Applications
    {
        get => _applications;
        init => _applications = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<Core.Calculations.CalculationResult> Calculations
    {
        get => _calculations;
        init => _calculations = value?.ToImmutableArray() ?? [];
    }
    public string ResolutionFingerprint { get; init; } = string.Empty;
}

public interface IAbilityExecutor
{
    Result<AbilityExecutionResult> Execute(AbilityExecutionRequest request);
    Result<ObservedGameplayExecution<AbilityExecutionResult>> ExecuteObserved(AbilityExecutionRequest request, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<AbilityExecutionResult>>.Failure("This executor does not support gameplay observation");
}

/// <summary>
/// Canonical immutable executor for configured actor abilities. Action content
/// is adapted to the shared legality components, while every resulting state
/// change is handled by the universal effect pipeline.
/// </summary>
public sealed class AbilityExecutor : IAbilityExecutor
{
    private readonly IActionManager _actions;
    private readonly ICardPlayEvaluator _legality;
    private readonly IEffectTriggerExecutor _triggers;

    public AbilityExecutor(
        IActionManager actions,
        ICardPlayEvaluator legality,
        IEffectTriggerExecutor triggers)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _legality = legality ?? throw new ArgumentNullException(nameof(legality));
        _triggers = triggers ?? throw new ArgumentNullException(nameof(triggers));
    }

    public Result<AbilityExecutionResult> Execute(AbilityExecutionRequest request) => ExecuteCore(request, null);

    public Result<ObservedGameplayExecution<AbilityExecutionResult>> ExecuteObserved(AbilityExecutionRequest request, GameplayEffectObservation observation)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != request.Run.Determinism.ContentRevision)
            return Result<ObservedGameplayExecution<AbilityExecutionResult>>.Failure("Observed execution requires the captured action revision");
        var result = ExecuteCore(request, observation);
        return result.IsFailure ? Result<ObservedGameplayExecution<AbilityExecutionResult>>.Failure(result.Error)
            : Result<ObservedGameplayExecution<AbilityExecutionResult>>.Success(new(result.Value, result.Value.Observation!));
    }

    private Result<AbilityExecutionResult> ExecuteCore(AbilityExecutionRequest request, GameplayEffectObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Run);
        ArgumentNullException.ThrowIfNull(request.Combat);
        if (string.IsNullOrWhiteSpace(request.ActionId))
            return Result<AbilityExecutionResult>.Failure("ActionId is required");
        var actor = request.Combat.GetActor(request.ActorId);
        if (actor == null || !actor.IsAlive)
            return Result<AbilityExecutionResult>.Failure($"Active actor not found: {request.ActorId}");
        var definition = _actions is IRevisionedActionCatalog revisioned
            ? revisioned.GetDefinition(
                request.ActionId,
                request.Run.Determinism.ContentRevision,
                request.Run.ConfigName)
            : _actions.GetDefinition(request.ActionId);
        if (definition.IsFailure)
            return Result<AbilityExecutionResult>.Failure(definition.Error);

        var legalCard = AdaptToLegalityComponents(request, definition.Value);
        var evaluation = _legality.Evaluate(
            legalCard,
            request.Combat,
            new CardPlayRequest
            {
                Run = request.Run,
                ActorId = request.ActorId,
                SelectedTargetIds = request.SelectedTargetIds,
                CostOptionId = request.CostOptionId,
                ContentRevision = request.Run.Determinism.ContentRevision,
                IgnoreConfiguredCosts = request.IgnoreConfiguredCosts
            });
        if (evaluation.IsFailure)
            return Result<AbilityExecutionResult>.Failure(evaluation.Error);
        if (!evaluation.Value.IsLegal)
            return Result<AbilityExecutionResult>.Failure(
                string.Join("; ", evaluation.Value.FailureReasons));

        var trigger = new EffectTriggerDefinition
        {
            TriggerId = "ability.resolve",
            Boundary = "Immediate",
            Effects = definition.Value.Effects
        };
        var triggerRequest = new EffectTriggerExecutionRequest
        {
            Run = request.Run,
            Combat = request.Combat,
            PrefixCommands = request.IgnoreConfiguredCosts ? [] : ActionEffectCosts.Compile(evaluation.Value,
                request.ActorId, new() { Kind = EffectProvenanceKind.Ability, SourceId = request.ActionId }, observation?.CostReasonId),
            Tags = definition.Value.Tags.ToImmutableHashSet(StringComparer.Ordinal),
            Trigger = trigger,
            OwnerEntityId = request.ActorId,
            SourceEntityId = request.ActorId,
            SelectedTargetEntityIds = evaluation.Value.ResolvedTargetIds,
            ContentRevision = request.Run.Determinism.ContentRevision,
            Provenance = new EffectProvenance
            {
                Kind = EffectProvenanceKind.Ability,
                SourceId = definition.Value.ActionId
            }
        };
        Result<EffectBatchResult> executed;
        if (observation == null) executed = _triggers.Execute(triggerRequest);
        else
        {
            var observed = _triggers.ExecuteObserved(triggerRequest, observation);
            if (observed.IsFailure) return Result<AbilityExecutionResult>.Failure(observed.Error);
            observation = observed.Value.Observation;
            executed = Result<EffectBatchResult>.Success(observed.Value.Execution);
        }
        if (executed.IsFailure)
            return Result<AbilityExecutionResult>.Failure(executed.Error);

        var applications = executed.Value.Records.ToImmutableArray();
        var appended = CombatTransitions.AppendAction(
            executed.Value.State,
            new CombatAction
            {
                Turn = request.Combat.CurrentTurn,
                EffectExecutionId = executed.Value.ExecutionId,
                ActorId = request.ActorId,
                ActionType = definition.Value.ActionType,
                PowerId = definition.Value.ActionType == ActionType.BASIC_ATTACK
                    ? null
                    : definition.Value.ActionId,
                TargetId = evaluation.Value.ResolvedTargetIds.FirstOrDefault(),
                TargetIds = evaluation.Value.ResolvedTargetIds,
                Applications = applications
            });
        var fingerprint = CanonicalJson.ComputeHash(new
        {
            definition = definition.Value,
            evaluation = evaluation.Value,
            calculations = executed.Value.Calculations,
            applications,
            finalState = CanonicalJson.ComputeHash(appended.State)
        });
        return Result<AbilityExecutionResult>.Success(new AbilityExecutionResult
        {
            Observation = observation,
            Combat = appended.State,
            Steps = executed.Value.Steps,
            Run = executed.Value.Run,
            Definition = definition.Value,
            Evaluation = evaluation.Value,
            Applications = applications,
            Calculations = executed.Value.Calculations,
            ResolutionFingerprint = fingerprint
        });
    }

    private static EffectiveCardDefinition AdaptToLegalityComponents(
        AbilityExecutionRequest request,
        ActionDefinition definition)
    {
        var components = new List<CardComponentDefinition>
        {
            new CardCostComponentDefinition
            {
                ComponentId = "ability.cost",
                Order = 10,
                Costs = definition.Costs
            }
        };
        if (definition.RequiresTarget)
        {
            components.Add(new CardTargetingComponentDefinition
            {
                ComponentId = "ability.targeting",
                Order = 80,
                Target = EffectTarget.TARGET,
                MinimumTargets = 1,
                MaximumTargets = definition.MultiTarget ? int.MaxValue : 1
            });
        }
        return new EffectiveCardDefinition
        {
            CardInstanceId = DeterministicId.Create(
                request.Run.Determinism.Seed,
                request.Combat.Determinism.Step,
                $"ability-legality:{definition.ActionId}"),
            DefinitionId = definition.ActionId,
            Tags = definition.Tags.Append("ability").ToImmutableArray(),
            Components = components
        };
    }
}
