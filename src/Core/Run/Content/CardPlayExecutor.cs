using System.Collections.Immutable;
using Core.Calculations;
using Core.CardZones;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.StatusEffects;

using Core.GameplayEvents;

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
    [System.Text.Json.Serialization.JsonIgnore]
    internal GameplayEffectObservation? Observation { get; init; }

    public ImmutableArray<EffectExecutionStep> Steps { get; init; } = [];
    public RunState? Run { get; init; }
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
    public string CardZoneResolutionFlowId { get; init; } = string.Empty;
    public string ResolutionFingerprint { get; init; } = string.Empty;
}

public interface ICardPlayExecutor
{
    Result<CardPlayExecutionResult> Execute(CardPlayExecutionRequest request);
    Result<ObservedGameplayExecution<CardPlayExecutionResult>> ExecuteObserved(CardPlayExecutionRequest request, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<CardPlayExecutionResult>>.Failure("This executor does not support gameplay observation");
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
    private readonly IEffectTriggerExecutor _triggers;

    public CardPlayExecutor(
        IContentRuntimeResolver runtimes,
        ICardContentCompiler compiler,
        IEffectiveCardResolver effectiveCards,
        ICardPlayEvaluator legality,
        IEffectTriggerExecutor triggers)
    {
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _effectiveCards = effectiveCards ?? throw new ArgumentNullException(nameof(effectiveCards));
        _legality = legality ?? throw new ArgumentNullException(nameof(legality));
        _triggers = triggers ?? throw new ArgumentNullException(nameof(triggers));
    }

    public Result<CardPlayExecutionResult> Execute(CardPlayExecutionRequest request) => ExecuteCore(request, null);

    public Result<ObservedGameplayExecution<CardPlayExecutionResult>> ExecuteObserved(CardPlayExecutionRequest request, GameplayEffectObservation observation)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != request.Run.Determinism.ContentRevision)
            return Result<ObservedGameplayExecution<CardPlayExecutionResult>>.Failure("Observed execution requires the captured action revision");
        var result = ExecuteCore(request, observation);
        return result.IsFailure ? Result<ObservedGameplayExecution<CardPlayExecutionResult>>.Failure(result.Error)
            : Result<ObservedGameplayExecution<CardPlayExecutionResult>>.Success(new(result.Value, result.Value.Observation!));
    }

    private Result<CardPlayExecutionResult> ExecuteCore(CardPlayExecutionRequest request, GameplayEffectObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Run);
        ArgumentNullException.ThrowIfNull(request.Combat);
        if (request.CardInstanceId == Guid.Empty)
            return Result<CardPlayExecutionResult>.Failure("CardInstanceId is required");
        if (!CardZonePlaySource.Contains(request.Run, request.ActorId, request.CardInstanceId))
        {
            return Result<CardPlayExecutionResult>.Failure(
                $"Card instance is not in a playable zone for {request.ActorId}: {request.CardInstanceId}");
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
        var effective = DeckCompartments.DeckCompartmentComposition.Resolve(request.Run, runtime.Value, compiled.Value, instance, _effectiveCards);
        if (effective.IsFailure)
            return Result<CardPlayExecutionResult>.Failure(effective.Error);

        var evaluation = _legality.Evaluate(
            effective.Value,
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
            return Result<CardPlayExecutionResult>.Failure(evaluation.Error);
        if (!evaluation.Value.IsLegal)
        {
            return Result<CardPlayExecutionResult>.Failure(
                string.Join("; ", evaluation.Value.FailureReasons));
        }

        var triggerRequest = new EffectTriggerExecutionRequest
        {
            Run = request.Run, Combat = request.Combat, Card = effective.Value,
            OwnerEntityId = request.ActorId, SourceEntityId = request.ActorId,
            ContentRevision = request.Run.Determinism.ContentRevision,
            SelectedTargetEntityIds = evaluation.Value.ResolvedTargetIds,
            Tags = compiled.Value.Tags.ToImmutableHashSet(StringComparer.Ordinal),
            Trigger = new() { TriggerId = "card.resolve" },
            Components = effective.Value.All<CardEffectComponentDefinition>()
                .Select(component => new EffectTriggerDefinition
                { TriggerId = component.ComponentId, Effects = [component.Effect] }).ToImmutableArray(),
            PrefixCommands = request.IgnoreConfiguredCosts ? [] : ActionEffectCosts.Compile(
                evaluation.Value, request.ActorId, CardProvenance(request.CardInstanceId, "cost"), observation?.CostReasonId),
            Provenance = CardProvenance(request.CardInstanceId, "card.resolve")
        };
        Result<EffectBatchResult> applied;
        if (observation == null) applied = _triggers.Execute(triggerRequest);
        else
        {
            var observed = _triggers.ExecuteObserved(triggerRequest, observation);
            if (observed.IsFailure) return Result<CardPlayExecutionResult>.Failure(observed.Error);
            observation = observed.Value.Observation;
            applied = Result<EffectBatchResult>.Success(observed.Value.Execution);
        }
        if (applied.IsFailure)
            return Result<CardPlayExecutionResult>.Failure(applied.Error);

        var appended = CombatTransitions.AppendAction(
            applied.Value.State,
            new CombatAction
            {
                Turn = request.Combat.CurrentTurn,
                EffectExecutionId = applied.Value.ExecutionId,
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
            calculations = applied.Value.Calculations,
            effects = applied.Value.Fingerprint,
            finalState = CanonicalJson.ComputeHash(appended.State)
        });
        return Result<CardPlayExecutionResult>.Success(new CardPlayExecutionResult
        {
            Observation = observation,
            Combat = appended.State,
            Steps = applied.Value.Steps,
            Run = applied.Value.Run,
            Card = effective.Value,
            Evaluation = evaluation.Value,
            Calculations = applied.Value.Calculations,
            Applications = applied.Value.Records,
            CardZoneResolutionFlowId = evaluation.Value.CardZoneResolutionFlowId,
            ResolutionFingerprint = fingerprint
        });
    }

    private static EffectProvenance CardProvenance(Guid cardInstanceId, string componentId) => new()
    {
        Kind = EffectProvenanceKind.Card,
        SourceId = cardInstanceId.ToString(),
        ComponentId = componentId
    };
}
