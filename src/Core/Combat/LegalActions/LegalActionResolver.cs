using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;

namespace Core.Combat.LegalActions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CombatCommandOrigin { PlayerInput, AutomaticController }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LegalActionSource { System, Ability, Card }

public sealed record LegalActionCandidate
{
    private ImmutableArray<EffectApplicationRecord> _applications = [];
    private ImmutableArray<CalculationResult> _calculations = [];
    private ImmutableArray<EffectExecutionStep> _steps = [];

    public string CandidateId { get; init; } = string.Empty;
    public LegalActionSource Source { get; init; }
    public CombatActionCommand Command { get; init; } = new();
    public string? ActionId { get; init; }
    public string? CardDefinitionId { get; init; }
    public IReadOnlyList<EffectApplicationRecord> Applications
    {
        get => _applications;
        init => _applications = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CalculationResult> Calculations
    {
        get => _calculations;
        init => _calculations = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectExecutionStep> Steps
    {
        get => _steps;
        init => _steps = value?.ToImmutableArray() ?? [];
    }
    public string ResolutionFingerprint { get; init; } = string.Empty;
    public bool OutcomeUncertain { get; init; }

    [JsonIgnore] public CombatState SuccessorCombat { get; init; } = null!;
    [JsonIgnore] public RunState SuccessorRun { get; init; } = null!;
    [JsonIgnore] public CardPlayExecutionResult? CardPlay { get; init; }
    [JsonIgnore] public AbilityExecutionResult? Ability { get; init; }
}

public sealed record LegalActionEvaluation
{
    private ImmutableArray<string> _failureReasons = [];
    public bool IsLegal => Candidate != null && _failureReasons.IsEmpty;
    public LegalActionCandidate? Candidate { get; init; }
    public CardPlayEvaluation? CardEvaluation { get; init; }
    public IReadOnlyList<string> FailureReasons
    {
        get => _failureReasons;
        init => _failureReasons = value?.ToImmutableArray() ?? [];
    }
}

public sealed record LegalActionSet
{
    private ImmutableArray<LegalActionCandidate> _candidates = [];
    public string ActorId { get; init; } = string.Empty;
    public IReadOnlyList<LegalActionCandidate> Candidates
    {
        get => _candidates;
        init => _candidates = value?.ToImmutableArray() ?? [];
    }
    public DeterministicContext Determinism { get; init; } = null!;
    public string StateFingerprint { get; init; } = string.Empty;
}

public interface ILegalActionResolver
{
    Result<LegalActionEvaluation> Evaluate(
        RunState run,
        CombatState combat,
        CombatActionCommand command,
        CombatCommandOrigin origin);

    Result<LegalActionSet> Resolve(
        RunState run,
        CombatState combat,
        string actorId,
        CombatCommandOrigin origin);
}

public interface ILegalActionQueryService
{
    Result<LegalActionSet> Get(Guid combatId, string? actorId = null);
}

/// <summary>Read application service used by HTTP without moving rules into controllers.</summary>
public sealed class LegalActionQueryService : ILegalActionQueryService
{
    private readonly IRunQueryService _runs;
    private readonly ILegalActionResolver _resolver;

    public LegalActionQueryService(IRunQueryService runs, ILegalActionResolver resolver)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public Result<LegalActionSet> Get(Guid combatId, string? actorId = null)
    {
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return Result<LegalActionSet>.Failure(run.Error);
        var encounter = run.Value.GetEncounter(combatId);
        if (encounter == null)
            return Result<LegalActionSet>.Failure($"Combat not found in run: {combatId}");
        var resolvedActorId = actorId ?? encounter.Combat.ActivationState?.ActiveActorId;
        if (string.IsNullOrWhiteSpace(resolvedActorId))
            return Result<LegalActionSet>.Failure("Actor id is required");
        var actor = encounter.Combat.GetActor(resolvedActorId);
        if (actor == null)
            return Result<LegalActionSet>.Failure($"Actor not found: {resolvedActorId}");
        var origin = encounter.Combat.ControllerOf(actor) == ControllerKind.AI
            ? CombatCommandOrigin.AutomaticController
            : CombatCommandOrigin.PlayerInput;
        return _resolver.Resolve(run.Value, encounter.Combat, resolvedActorId, origin);
    }
}

/// <summary>
/// Sole legality/preview boundary for player commands, automatic policies and
/// REST projections. Calling it never commits or mutates runtime state.
/// </summary>
public sealed class LegalActionResolver : ILegalActionResolver
{
    private readonly IActionManager _actions;
    private readonly IContentRuntimeResolver _runtimes;
    private readonly ICardContentCompiler _cards;
    private readonly IEffectiveCardResolver _effectiveCards;
    private readonly ICardPlayEvaluator _cardLegality;
    private readonly ICardPlayExecutor _cardExecutor;
    private readonly IAbilityExecutor _abilityExecutor;

    public LegalActionResolver(
        IActionManager actions,
        IContentRuntimeResolver runtimes,
        ICardContentCompiler cards,
        IEffectiveCardResolver effectiveCards,
        ICardPlayEvaluator cardLegality,
        ICardPlayExecutor cardExecutor,
        IAbilityExecutor abilityExecutor)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _effectiveCards = effectiveCards ?? throw new ArgumentNullException(nameof(effectiveCards));
        _cardLegality = cardLegality ?? throw new ArgumentNullException(nameof(cardLegality));
        _cardExecutor = cardExecutor ?? throw new ArgumentNullException(nameof(cardExecutor));
        _abilityExecutor = abilityExecutor ?? throw new ArgumentNullException(nameof(abilityExecutor));
    }

    public Result<LegalActionEvaluation> Evaluate(
        RunState run,
        CombatState combat,
        CombatActionCommand command,
        CombatCommandOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(command);
        if (run.ResolvedMode == null)
            return Result<LegalActionEvaluation>.Failure("Run has no resolved game mode");

        var flow = ValidateFlow(combat, command, origin);
        if (flow.IsFailure)
            return Illegal(flow.Error);
        var budget = CombatFlowTransitions.ValidateActionBudget(
            run,
            combat,
            command,
            run.ResolvedMode.CombatRules.Flow.ActionBudget,
            CommandType(command));
        if (budget.IsFailure)
            return Illegal(budget.Error);

        return command.ActionType switch
        {
            ActionType.PLAY_CARD => EvaluateCard(run, combat, command),
            ActionType.BASIC_ATTACK or ActionType.POWER or ActionType.ACTIVATE_ABILITY =>
                EvaluateAbility(run, combat, command),
            ActionType.PASS or ActionType.END_TURN => EvaluatePassive(run, combat, command),
            _ => Illegal($"Unsupported combat action: {command.ActionType}")
        };
    }

    public Result<LegalActionSet> Resolve(
        RunState run,
        CombatState combat,
        string actorId,
        CombatCommandOrigin origin)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            return Result<LegalActionSet>.Failure("Actor id is required");
        var actor = combat.GetActor(actorId);
        if (actor == null)
            return Result<LegalActionSet>.Failure($"Actor not found: {actorId}");
        CombatActionCommand[] commands;
        try
        {
            commands = EnumerateCommands(run, combat, actor).ToArray();
        }
        catch (InvalidOperationException exception)
        {
            return Result<LegalActionSet>.Failure(exception.Message);
        }
        var candidates = ImmutableArray.CreateBuilder<LegalActionCandidate>();
        foreach (var command in commands)
        {
            var evaluated = Evaluate(run, combat, command, origin);
            if (evaluated.IsFailure)
                return Result<LegalActionSet>.Failure(evaluated.Error);
            if (evaluated.Value.Candidate != null)
                candidates.Add(evaluated.Value.Candidate);
        }

        var ordered = candidates
            .GroupBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.Command.ActionType)
            .ThenBy(candidate => candidate.ActionId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.CardDefinitionId, StringComparer.Ordinal)
            .ThenBy(candidate => string.Join("\u001f", candidate.Command.TargetIds), StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Command.CostOptionId, StringComparer.Ordinal)
            .ToImmutableArray();
        return Result<LegalActionSet>.Success(new LegalActionSet
        {
            ActorId = actorId,
            Candidates = ordered,
            Determinism = run.Determinism,
            StateFingerprint = CanonicalJson.ComputeHash(new
            {
                run.Sequence,
                runStep = run.Determinism.Step,
                combatStep = combat.Determinism.Step,
                combat,
                run.Deck
            })
        });
    }

    private Result<LegalActionEvaluation> EvaluateCard(
        RunState run,
        CombatState combat,
        CombatActionCommand command)
    {
        var actor = combat.GetActor(command.ActorId)!;
        if (combat.ControllerOf(actor) != ControllerKind.Player ||
            !string.Equals(actor.InstanceId, run.PlayerEntityId, StringComparison.Ordinal))
            return Illegal("Only the configured run owner can play cards from its deck");
        if (command.CardInstanceId is not { } cardInstanceId || cardInstanceId == Guid.Empty)
            return Illegal("CardInstanceId is required for PLAY_CARD");
        if (!run.Deck.HandInstanceIds.Contains(cardInstanceId))
            return Illegal($"Card instance is not in run hand: {cardInstanceId}");
        var instance = run.Deck.GetCard(cardInstanceId);
        if (instance == null)
            return Result<LegalActionEvaluation>.Failure($"Card instance was not found: {cardInstanceId}");
        var runtime = _runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure)
            return Result<LegalActionEvaluation>.Failure(runtime.Error);
        var compiled = _cards.Compile(instance.DefinitionId, runtime.Value);
        if (compiled.IsFailure)
            return Result<LegalActionEvaluation>.Failure(compiled.Error);
        var effective = _effectiveCards.Resolve(compiled.Value, instance);
        if (effective.IsFailure)
            return Result<LegalActionEvaluation>.Failure(effective.Error);
        var evaluation = _cardLegality.Evaluate(effective.Value, combat, new CardPlayRequest
        {
            ActorId = command.ActorId,
            SelectedTargetIds = Targets(command),
            CostOptionId = command.CostOptionId,
            ContentRevision = run.Determinism.ContentRevision,
            IgnoreConfiguredCosts = IgnoreCosts(run)
        });
        if (evaluation.IsFailure)
            return Result<LegalActionEvaluation>.Failure(evaluation.Error);
        if (!evaluation.Value.IsLegal)
            return Illegal(evaluation.Value.FailureReasons, evaluation.Value);
        var executed = _cardExecutor.Execute(new CardPlayExecutionRequest
        {
            Run = run,
            Combat = combat,
            CardInstanceId = cardInstanceId,
            ActorId = command.ActorId,
            SelectedTargetIds = Targets(command),
            CostOptionId = command.CostOptionId,
            IgnoreConfiguredCosts = IgnoreCosts(run)
        });
        if (executed.IsFailure)
            return Illegal(executed.Error);
        return Legal(CreateCandidate(run, command, executed.Value), evaluation.Value);
    }

    private Result<LegalActionEvaluation> EvaluateAbility(
        RunState run,
        CombatState combat,
        CombatActionCommand command)
    {
        var actor = combat.GetActor(command.ActorId)!;
        var abilityIds = actor.Component<AbilityEntityComponentState>()?.AbilityIds ?? [];
        var actionId = command.PowerId;
        if (string.IsNullOrWhiteSpace(actionId))
        {
            var matching = abilityIds
                .Select(id => ResolveAction(run, id))
                .Where(result => result.IsSuccess && result.Value.ActionType == command.ActionType)
                .Select(result => result.Value.ActionId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (matching.Length != 1)
                return Illegal($"PowerId is required when {matching.Length} actor abilities match {command.ActionType}");
            actionId = matching[0];
        }
        if (!abilityIds.Contains(actionId, StringComparer.Ordinal))
            return Illegal($"Actor '{actor.InstanceId}' does not own ability '{actionId}'");
        var definition = ResolveAction(run, actionId);
        if (definition.IsFailure)
            return Result<LegalActionEvaluation>.Failure(definition.Error);
        if (definition.Value.ActionType != command.ActionType && command.ActionType != ActionType.ACTIVATE_ABILITY)
            return Illegal($"Action {actionId} is configured as {definition.Value.ActionType}, not {command.ActionType}");
        var effectiveCommand = command with { PowerId = actionId };
        var executed = _abilityExecutor.Execute(new AbilityExecutionRequest
        {
            Run = run,
            Combat = combat,
            ActionId = actionId,
            ActorId = command.ActorId,
            SelectedTargetIds = Targets(command),
            CostOptionId = command.CostOptionId,
            IgnoreConfiguredCosts = IgnoreCosts(run)
        });
        if (executed.IsFailure)
            return Illegal(executed.Error);
        return Legal(CreateCandidate(run, effectiveCommand, executed.Value));
    }

    private static Result<LegalActionEvaluation> EvaluatePassive(
        RunState run,
        CombatState combat,
        CombatActionCommand command)
    {
        var executed = CombatFlowTransitions.AppendPassiveCommand(combat, command);
        if (executed.IsFailure)
            return Illegal(executed.Error);
        var fingerprint = CanonicalJson.ComputeHash(new
        {
            command,
            before = CanonicalJson.ComputeHash(combat),
            after = CanonicalJson.ComputeHash(executed.Value)
        });
        return Legal(new LegalActionCandidate
        {
            CandidateId = fingerprint,
            Source = LegalActionSource.System,
            Command = command,
            SuccessorCombat = executed.Value,
            SuccessorRun = run,
            ResolutionFingerprint = fingerprint
        });
    }

    private IEnumerable<CombatActionCommand> EnumerateCommands(
        RunState run,
        CombatState combat,
        CombatActorState actor)
    {
        yield return BaseCommand(run, actor, ActionType.PASS);
        yield return BaseCommand(run, actor, ActionType.END_TURN);

        var targetSets = TargetSets(combat).ToArray();
        foreach (var abilityId in (actor.Component<AbilityEntityComponentState>()?.AbilityIds ?? [])
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            var definition = ResolveAction(run, abilityId);
            if (definition.IsFailure)
                throw new InvalidOperationException(definition.Error);
            foreach (var costOption in CostOptions(definition.Value.Costs))
            foreach (var targets in targetSets)
                yield return BaseCommand(run, actor, definition.Value.ActionType) with
                {
                    PowerId = abilityId,
                    TargetId = targets.FirstOrDefault(),
                    TargetIds = targets,
                    CostOptionId = costOption
                };
        }

        if (combat.ControllerOf(actor) != ControllerKind.Player ||
            !string.Equals(actor.InstanceId, run.PlayerEntityId, StringComparison.Ordinal))
            yield break;
        var runtime = _runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure)
            throw new InvalidOperationException(runtime.Error);
        foreach (var cardId in run.Deck.HandInstanceIds.OrderBy(id => id))
        {
            var instance = run.Deck.GetCard(cardId)
                ?? throw new InvalidOperationException($"Card instance was not found: {cardId}");
            var compiled = _cards.Compile(instance.DefinitionId, runtime.Value);
            if (compiled.IsFailure)
                throw new InvalidOperationException(compiled.Error);
            var effective = _effectiveCards.Resolve(compiled.Value, instance);
            if (effective.IsFailure)
                throw new InvalidOperationException(effective.Error);
            var options = effective.Value.All<CardCostComponentDefinition>()
                .SelectMany(component => CostOptions(component.Costs))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (var costOption in options)
            foreach (var targets in targetSets)
                yield return BaseCommand(run, actor, ActionType.PLAY_CARD) with
                {
                    CardInstanceId = cardId,
                    TargetId = targets.FirstOrDefault(),
                    TargetIds = targets,
                    CostOptionId = costOption
                };
        }
    }

    private static CombatActionCommand BaseCommand(RunState run, CombatActorState actor, ActionType actionType) => new()
    {
        RunId = run.RunId,
        ActorId = actor.InstanceId,
        ActionType = actionType
    };

    private static IEnumerable<IReadOnlyList<string>> TargetSets(CombatState combat)
    {
        yield return [];
        var ids = combat.GetAllActors().Where(actor => actor.IsAlive)
            .Select(actor => actor.InstanceId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        foreach (var id in ids)
            yield return [id];
        if (ids.Length > 1)
            yield return ids;
    }

    private static IEnumerable<string?> CostOptions(ActionCosts costs)
    {
        yield return null;
        foreach (var id in costs.AlternativeCosts.Select(option => option.OptionId)
                     .Where(id => !string.IsNullOrWhiteSpace(id)).OrderBy(id => id, StringComparer.Ordinal))
            yield return id;
    }

    private static IReadOnlyList<string> Targets(CombatActionCommand command) =>
        command.TargetIds.Count > 0
            ? command.TargetIds
            : string.IsNullOrWhiteSpace(command.TargetId) ? [] : [command.TargetId];

    private static bool IgnoreCosts(RunState run) =>
        run.ResolvedMode!.CombatRules.Flow.ActionBudget.ActionCosts == ActionCostStrategy.Ignore;

    private Result<ActionDefinition> ResolveAction(RunState run, string actionId) =>
        _actions is IRevisionedActionCatalog revisioned
            ? revisioned.GetDefinition(actionId, run.Determinism.ContentRevision, run.ConfigName)
            : _actions.GetDefinition(actionId);

    private static Result ValidateFlow(
        CombatState combat,
        CombatActionCommand command,
        CombatCommandOrigin origin)
    {
        if (!combat.IsActive)
            return Result.Failure($"Combat is not active: {combat.CombatId}");
        var actor = combat.GetActor(command.ActorId);
        if (actor == null || !actor.IsAlive)
            return Result.Failure($"Active actor not found: {command.ActorId}");
        var activation = combat.ActivationState;
        if (activation == null)
            return Result.Failure("Combat activation has not been initialized");
        if (!string.Equals(activation.ActiveActorId, command.ActorId, StringComparison.Ordinal))
            return Result.Failure($"Actor '{command.ActorId}' is not the active actor");
        if (origin == CombatCommandOrigin.PlayerInput && !activation.WaitingForInput)
            return Result.Failure("Combat is resolving automatic actions");
        if (origin == CombatCommandOrigin.AutomaticController &&
            (activation.WaitingForInput || combat.ControllerOf(actor) != ControllerKind.AI))
            return Result.Failure("Automatic command requires the active AI controller");
        var phase = combat.PhaseState?.PhaseSequence.Find(combat.PhaseState.CurrentPhaseId);
        if (phase == null)
            return Result.Failure("Combat phase has not been initialized");
        return phase.AllowedActions.Contains(command.ActionType)
            ? Result.Success()
            : Result.Failure($"Action '{command.ActionType}' is not allowed in phase '{phase.PhaseId}'");
    }

    private static string CommandType(CombatActionCommand command) => command.ActionType switch
    {
        ActionType.PLAY_CARD => GameplayCommandTypes.PlayCard,
        ActionType.END_TURN => GameplayCommandTypes.EndTurn,
        _ => GameplayCommandTypes.ExecuteAction
    };

    private static LegalActionCandidate CreateCandidate(
        RunState run,
        CombatActionCommand command,
        CardPlayExecutionResult result)
    {
        var normalized = command with
        {
            TargetId = result.Evaluation.ResolvedTargetIds.FirstOrDefault(),
            TargetIds = result.Evaluation.ResolvedTargetIds
        };
        return new LegalActionCandidate
        {
            CandidateId = CanonicalJson.ComputeHash(new { command = normalized, result.ResolutionFingerprint }),
            Source = LegalActionSource.Card,
            Command = normalized,
            CardDefinitionId = result.Card.DefinitionId,
            Applications = result.Applications,
            Calculations = result.Calculations,
            Steps = result.Steps,
            ResolutionFingerprint = result.ResolutionFingerprint,
            SuccessorCombat = result.Combat,
            SuccessorRun = result.Run ?? run,
            CardPlay = result
        };
    }

    private static LegalActionCandidate CreateCandidate(
        RunState run,
        CombatActionCommand command,
        AbilityExecutionResult result)
    {
        var normalized = command with
        {
            TargetId = result.Evaluation.ResolvedTargetIds.FirstOrDefault(),
            TargetIds = result.Evaluation.ResolvedTargetIds
        };
        return new LegalActionCandidate
        {
            CandidateId = CanonicalJson.ComputeHash(new { command = normalized, result.ResolutionFingerprint }),
            Source = LegalActionSource.Ability,
            Command = normalized,
            ActionId = result.Definition.ActionId,
            Applications = result.Applications,
            Calculations = result.Calculations,
            Steps = result.Steps,
            ResolutionFingerprint = result.ResolutionFingerprint,
            SuccessorCombat = result.Combat,
            SuccessorRun = result.Run ?? run,
            Ability = result
        };
    }

    private static Result<LegalActionEvaluation> Legal(
        LegalActionCandidate candidate,
        CardPlayEvaluation? cardEvaluation = null) =>
        Result<LegalActionEvaluation>.Success(new LegalActionEvaluation
        {
            Candidate = candidate,
            CardEvaluation = cardEvaluation
        });

    private static Result<LegalActionEvaluation> Illegal(string reason) =>
        Result<LegalActionEvaluation>.Success(new LegalActionEvaluation { FailureReasons = [reason] });

    private static Result<LegalActionEvaluation> Illegal(
        IReadOnlyList<string> reasons,
        CardPlayEvaluation cardEvaluation) =>
        Result<LegalActionEvaluation>.Success(new LegalActionEvaluation
        {
            FailureReasons = reasons,
            CardEvaluation = cardEvaluation
        });
}
