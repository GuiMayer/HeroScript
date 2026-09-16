using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.CardZones;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Reactions;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;

namespace Core.Combat.LegalActions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CombatCommandOrigin { PlayerInput, AutomaticController, PendingResolution }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LegalActionSource { System, Ability, Card }

public sealed record LegalActionCandidate
{
    private ImmutableArray<EffectApplicationRecord> _applications = [];
    private ImmutableArray<CalculationResult> _calculations = [];
    private ImmutableArray<EffectExecutionStep> _steps = [];
    private ImmutableArray<PhaseTransitionRecord> _phaseTransitions = [];
    private ImmutableArray<ResolvedCardCost> _costs = [];

    public string CandidateId { get; init; } = string.Empty;
    public LegalActionSource Source { get; init; }
    public CombatActionCommand Command { get; init; } = new();
    public string? ActionId { get; init; }
    public string? CardDefinitionId { get; init; }
    /// <summary>Resolved configured costs for this exact legal choice, not a client estimate.</summary>
    public IReadOnlyList<ResolvedCardCost> Costs
    {
        get => _costs;
        init => _costs = value?.ToImmutableArray() ?? [];
    }
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
    public IReadOnlyList<PhaseTransitionRecord> PhaseTransitions
    {
        get => _phaseTransitions;
        init => _phaseTransitions = value?.ToImmutableArray() ?? [];
    }
    public string ResolutionFingerprint { get; init; } = string.Empty;
    public bool OutcomeUncertain { get; init; }
    public ReactionTransitionKind ReactionTransition { get; init; } = ReactionTransitionKind.ResolvedImmediately;
    public CombatActionCommand? ResolvedCommand { get; init; }
    public PendingActionState? PendingAction { get; init; }

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
        var resolvedActorId = actorId ?? encounter.Combat.PriorityWindow?.HolderActorId ??
            encounter.Combat.ActivationState?.ActiveActorId;
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
    private readonly IPhaseGraphReducer _phases;
    private readonly IReactionFlowReducer _reactions;
    private readonly IEffectTriggerExecutor _effects;

    public LegalActionResolver(
        IActionManager actions,
        IContentRuntimeResolver runtimes,
        ICardContentCompiler cards,
        IEffectiveCardResolver effectiveCards,
        ICardPlayEvaluator cardLegality,
        ICardPlayExecutor cardExecutor,
        IAbilityExecutor abilityExecutor,
        IPhaseGraphReducer phases,
        IReactionFlowReducer reactions,
        IEffectTriggerExecutor effects)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _effectiveCards = effectiveCards ?? throw new ArgumentNullException(nameof(effectiveCards));
        _cardLegality = cardLegality ?? throw new ArgumentNullException(nameof(cardLegality));
        _cardExecutor = cardExecutor ?? throw new ArgumentNullException(nameof(cardExecutor));
        _abilityExecutor = abilityExecutor ?? throw new ArgumentNullException(nameof(abilityExecutor));
        _phases = phases ?? throw new ArgumentNullException(nameof(phases));
        _reactions = reactions ?? throw new ArgumentNullException(nameof(reactions));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
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

        var flow = ValidateActivation(combat, command, origin);
        if (flow.IsFailure)
            return Illegal(flow.Error);
        if (command.ActionType == ActionType.PASS_PRIORITY)
            return EvaluatePriorityPass(run, combat, command);
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
            ActionType.PLAY_CARD => EvaluateCard(run, combat, command, origin),
            ActionType.BASIC_ATTACK or ActionType.POWER or ActionType.ACTIVATE_ABILITY or ActionType.PLAY_INSTANT =>
                EvaluateAbility(run, combat, command, origin),
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
        CombatActionCommand command,
        CombatCommandOrigin origin)
    {
        var actor = combat.GetActor(command.ActorId)!;
        if (combat.ControllerOf(actor) != ControllerKind.Player ||
            !string.Equals(actor.InstanceId, run.PlayerEntityId, StringComparison.Ordinal))
            return Illegal("Only the configured run owner can play cards from its deck");
        if (command.CardInstanceId is not { } cardInstanceId || cardInstanceId == Guid.Empty)
            return Illegal("CardInstanceId is required for PLAY_CARD");
        if (!CardZonePlaySource.Contains(run, command.ActorId, cardInstanceId))
            return Illegal($"Card instance is not in a playable zone: {cardInstanceId}");
        if (origin != CombatCommandOrigin.PendingResolution && combat.PendingActions.Any(item =>
                item.Command.CardInstanceId == cardInstanceId))
            return Illegal($"Card instance is already reserved by the reaction stack: {cardInstanceId}");
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
        var tags = effective.Value.Tags
            .Append("card")
            .Append(ActionType.PLAY_CARD.ToString())
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var phase = ValidatePhase(run, combat, command, tags);
        if (phase.IsFailure) return Illegal(phase.Error);
        var evaluation = _cardLegality.Evaluate(effective.Value, combat, new CardPlayRequest
        {
            ActorId = command.ActorId,
            SelectedTargetIds = Targets(command),
            CostOptionId = command.CostOptionId,
            ContentRevision = run.Determinism.ContentRevision,
            IgnoreConfiguredCosts = IgnoreCosts(run, command),
            UseCardZoneResolution = run.ResolvedMode?.CardZoneSystem != null
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
            IgnoreConfiguredCosts = IgnoreCosts(run, command)
        });
        if (executed.IsFailure)
            return Illegal(executed.Error);
        var candidate = ApplyPhase(run, combat, CreateCandidate(run, command, executed.Value), tags);
        if (candidate.IsFailure) return Result<LegalActionEvaluation>.Failure(candidate.Error);
        return FinalizeReaction(run, combat, command, origin, tags, candidate.Value, evaluation.Value);
    }

    private Result<LegalActionEvaluation> EvaluateAbility(
        RunState run,
        CombatState combat,
        CombatActionCommand command,
        CombatCommandOrigin origin)
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
        var tags = definition.Value.Tags
            .Append("ability")
            .Append(command.ActionType.ToString())
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var phase = ValidatePhase(run, combat, effectiveCommand, tags);
        if (phase.IsFailure) return Illegal(phase.Error);
        var executed = _abilityExecutor.Execute(new AbilityExecutionRequest
        {
            Run = run,
            Combat = combat,
            ActionId = actionId,
            ActorId = command.ActorId,
            SelectedTargetIds = Targets(command),
            CostOptionId = command.CostOptionId,
            IgnoreConfiguredCosts = IgnoreCosts(run, command)
        });
        if (executed.IsFailure)
            return Illegal(executed.Error);
        var candidate = ApplyPhase(run, combat, CreateCandidate(run, effectiveCommand, executed.Value), tags);
        if (candidate.IsFailure) return Result<LegalActionEvaluation>.Failure(candidate.Error);
        return FinalizeReaction(run, combat, effectiveCommand, origin, tags, candidate.Value,
            executed.Value.Evaluation);
    }

    private Result<LegalActionEvaluation> EvaluatePassive(
        RunState run,
        CombatState combat,
        CombatActionCommand command)
    {
        var tags = new[] { "system", command.ActionType.ToString() }
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var phase = ValidatePhase(run, combat, command, tags);
        if (phase.IsFailure) return Illegal(phase.Error);
        var executed = CombatFlowTransitions.AppendPassiveCommand(combat, command);
        if (executed.IsFailure)
            return Illegal(executed.Error);
        var fingerprint = CanonicalJson.ComputeHash(new
        {
            command,
            before = CanonicalJson.ComputeHash(combat),
            after = CanonicalJson.ComputeHash(executed.Value)
        });
        var candidate = new LegalActionCandidate
        {
            CandidateId = fingerprint,
            Source = LegalActionSource.System,
            Command = command,
            SuccessorCombat = executed.Value,
            SuccessorRun = run,
            ResolutionFingerprint = fingerprint
        };
        var transitioned = ApplyPhase(run, combat, candidate, tags);
        return transitioned.IsFailure
            ? Result<LegalActionEvaluation>.Failure(transitioned.Error)
            : Legal(transitioned.Value);
    }

    private Result<LegalActionEvaluation> FinalizeReaction(
        RunState run,
        CombatState originalCombat,
        CombatActionCommand submittedCommand,
        CombatCommandOrigin origin,
        IReadOnlySet<string> tags,
        LegalActionCandidate resolved,
        CardPlayEvaluation evaluation)
    {
        var policy = run.ResolvedMode!.CombatRules.Flow.Reactions;
        if (origin == CombatCommandOrigin.PendingResolution)
        {
            return Legal(resolved with
            {
                ResolvedCommand = resolved.Command,
                ReactionTransition = ReactionTransitionKind.StackActionResolved
            }, evaluation);
        }
        var shouldPropose = _reactions.ShouldPropose(originalCombat, policy, tags);
        if (originalCombat.PriorityWindow != null &&
            policy.Strategy == ReactionStrategy.PriorityStack && !shouldPropose)
        {
            return Illegal(
                $"Action '{submittedCommand.ActionType}' is not eligible as a response in the current priority window");
        }
        if (!PhaseAllowsPriority(run, originalCombat) || !shouldPropose)
        {
            return Legal(resolved with { ResolvedCommand = resolved.Command }, evaluation);
        }

        var costResult = policy.CostTiming == ReactionCostTiming.Proposal && !IgnoreCosts(run, submittedCommand)
            ? ApplyPendingCosts(run, originalCombat, resolved, evaluation, refund: false)
            : Result<EffectBatchResult>.Success(new EffectBatchResult
            {
                State = originalCombat,
                Run = run,
                Fingerprint = CanonicalJson.ComputeHash(originalCombat)
            });
        if (costResult.IsFailure)
            return Illegal(costResult.Error);

        var storedCommand = policy.TargetLock == ReactionLockTiming.Proposal
            ? resolved.Command
            : submittedCommand with { ExpectedStep = null };
        var paidCosts = policy.CostTiming == ReactionCostTiming.Proposal && !IgnoreCosts(run, submittedCommand)
            ? evaluation.Costs.Select(PendingResourceCost.From).ToImmutableArray()
            : [];
        var pendingId = CanonicalJson.ComputeHash(new
        {
            originalCombat.CombatId,
            command = storedCommand,
            candidate = resolved.CandidateId,
            depth = originalCombat.PendingActions.Length + 1,
            originalCombat.Determinism.Step
        });
        var pending = new PendingActionState
        {
            PendingActionId = pendingId,
            Depth = originalCombat.PendingActions.Length + 1,
            Command = storedCommand,
            CandidateFingerprint = resolved.CandidateId,
            ContentRevision = run.Determinism.ContentRevision,
            ActionId = resolved.ActionId,
            CardDefinitionId = resolved.CardDefinitionId,
            LockedTargetIds = policy.TargetLock == ReactionLockTiming.Proposal
                ? resolved.Command.TargetIds
                : [],
            PaidCosts = paidCosts,
            CommandTags = tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray()
        };
        var proposed = _reactions.Propose(costResult.Value.State, pending, policy);
        if (proposed.IsFailure)
            return Illegal(proposed.Error);
        var fingerprint = CanonicalJson.ComputeHash(new
        {
            transition = ReactionTransitionKind.Proposed,
            pending,
            before = CanonicalJson.ComputeHash(originalCombat),
            after = CanonicalJson.ComputeHash(proposed.Value)
        });
        return Legal(new LegalActionCandidate
        {
            CandidateId = fingerprint,
            Source = resolved.Source,
            Command = storedCommand,
            ActionId = resolved.ActionId,
            CardDefinitionId = resolved.CardDefinitionId,
            Applications = costResult.Value.Records,
            Costs = evaluation.Costs,
            Steps = costResult.Value.Steps,
            Calculations = costResult.Value.Calculations,
            ResolutionFingerprint = fingerprint,
            OutcomeUncertain = true,
            ReactionTransition = ReactionTransitionKind.Proposed,
            PendingAction = pending,
            SuccessorCombat = proposed.Value,
            SuccessorRun = costResult.Value.Run ?? run
        }, evaluation);
    }

    private Result<LegalActionEvaluation> EvaluatePriorityPass(
        RunState run,
        CombatState combat,
        CombatActionCommand command)
    {
        var policy = run.ResolvedMode!.CombatRules.Flow.Reactions;
        if (policy.Strategy != ReactionStrategy.PriorityStack)
            return Illegal("PASS_PRIORITY requires the PriorityStack reaction strategy");
        var passed = _reactions.Pass(combat, command.ActorId, policy);
        if (passed.IsFailure) return Illegal(passed.Error);
        if (passed.Value.ActionToResolve == null)
        {
            var passFingerprint = CanonicalJson.ComputeHash(new
            {
                transition = ReactionTransitionKind.PriorityPassed,
                command,
                before = CanonicalJson.ComputeHash(combat),
                after = CanonicalJson.ComputeHash(passed.Value.Combat)
            });
            return Legal(new LegalActionCandidate
            {
                CandidateId = passFingerprint,
                Source = LegalActionSource.System,
                Command = command,
                ReactionTransition = ReactionTransitionKind.PriorityPassed,
                ResolutionFingerprint = passFingerprint,
                SuccessorCombat = passed.Value.Combat,
                SuccessorRun = run
            });
        }

        var pending = passed.Value.ActionToResolve;
        if (!string.Equals(pending.ContentRevision, run.Determinism.ContentRevision, StringComparison.Ordinal))
            return Result<LegalActionEvaluation>.Failure(
                $"Pending action content revision mismatch: {pending.ContentRevision}");
        var resolutionCommand = pending.Command with
        {
            ExpectedStep = null,
            IgnoreConfiguredCosts = pending.CostsPaid || pending.Command.IgnoreConfiguredCosts
        };
        var resolved = Evaluate(run, passed.Value.Combat, resolutionCommand,
            CombatCommandOrigin.PendingResolution);
        if (resolved.IsFailure || !resolved.Value.IsLegal)
            return ResolveFizzle(run, combat, passed.Value.Combat, command, pending, policy,
                resolved.IsFailure ? resolved.Error : string.Join("; ", resolved.Value.FailureReasons));

        var candidate = resolved.Value.Candidate!;
        var completed = _reactions.CompleteResolution(candidate.SuccessorCombat, pending, policy);
        if (completed.IsFailure) return Result<LegalActionEvaluation>.Failure(completed.Error);
        var fingerprint = CanonicalJson.ComputeHash(new
        {
            transition = ReactionTransitionKind.StackActionResolved,
            rootCommand = command,
            resolvedCommand = candidate.Command,
            pending.PendingActionId,
            candidate.ResolutionFingerprint,
            after = CanonicalJson.ComputeHash(completed.Value)
        });
        return Legal(candidate with
        {
            CandidateId = fingerprint,
            Command = command,
            ResolvedCommand = candidate.Command,
            PendingAction = pending,
            ReactionTransition = ReactionTransitionKind.StackActionResolved,
            ResolutionFingerprint = fingerprint,
            SuccessorCombat = completed.Value
        }, resolved.Value.CardEvaluation);
    }

    private Result<LegalActionEvaluation> ResolveFizzle(
        RunState run,
        CombatState originalCombat,
        CombatState poppedCombat,
        CombatActionCommand passCommand,
        PendingActionState pending,
        ReactionPolicyDefinition policy,
        string reason)
    {
        if (policy.Failure == ReactionResolutionFailure.RejectTransaction)
            return Illegal($"Pending action '{pending.PendingActionId}' cannot resolve: {reason}");
        var refunded = policy.Failure == ReactionResolutionFailure.FizzleRefund && pending.CostsPaid
            ? ApplyPendingCosts(run, poppedCombat, pending, refund: true)
            : Result<EffectBatchResult>.Success(new EffectBatchResult
            {
                State = poppedCombat,
                Run = run,
                Fingerprint = CanonicalJson.ComputeHash(poppedCombat)
            });
        if (refunded.IsFailure) return Result<LegalActionEvaluation>.Failure(refunded.Error);
        var completed = _reactions.CompleteResolution(refunded.Value.State, pending, policy);
        if (completed.IsFailure) return Result<LegalActionEvaluation>.Failure(completed.Error);
        var fingerprint = CanonicalJson.ComputeHash(new
        {
            transition = ReactionTransitionKind.StackActionFizzled,
            passCommand,
            pending,
            reason,
            before = CanonicalJson.ComputeHash(originalCombat),
            after = CanonicalJson.ComputeHash(completed.Value)
        });
        return Legal(new LegalActionCandidate
        {
            CandidateId = fingerprint,
            Source = LegalActionSource.System,
            Command = passCommand,
            PendingAction = pending,
            ReactionTransition = ReactionTransitionKind.StackActionFizzled,
            Applications = refunded.Value.Records,
            Steps = refunded.Value.Steps,
            Calculations = refunded.Value.Calculations,
            OutcomeUncertain = false,
            ResolutionFingerprint = fingerprint,
            SuccessorCombat = completed.Value,
            SuccessorRun = refunded.Value.Run ?? run
        });
    }

    private Result<EffectBatchResult> ApplyPendingCosts(
        RunState run,
        CombatState combat,
        LegalActionCandidate candidate,
        CardPlayEvaluation evaluation,
        bool refund)
    {
        var costs = evaluation.Costs.Select(PendingResourceCost.From).ToArray();
        return ApplyPendingCosts(run, combat, new PendingActionState
        {
            PendingActionId = candidate.CandidateId,
            Command = candidate.Command,
            PaidCosts = costs
        }, refund);
    }

    private Result<EffectBatchResult> ApplyPendingCosts(
        RunState run,
        CombatState combat,
        PendingActionState pending,
        bool refund)
    {
        var provenance = new EffectProvenance
        {
            Kind = EffectProvenanceKind.Rule,
            SourceId = $"reaction:{pending.PendingActionId}",
            ComponentId = refund ? "refund" : "reservation"
        };
        var commands = pending.PaidCosts.Select(cost => new ResolvedEffectCommand
        {
            EffectInstanceId = $"{provenance.SourceId}:{provenance.ComponentId}:{cost.ComponentId}:{cost.ResourceId}",
            Definition = new EffectDefinition
            {
                EffectId = $"reaction-cost:{cost.ComponentId}",
                Type = EffectType.MODIFY_RESOURCE,
                TargetResource = cost.ResourceId,
                Operation = refund ? ResourceEffectOperation.ADD : ResourceEffectOperation.SUBTRACT
            },
            SourceEntityId = pending.Command.ActorId,
            TargetEntityIds = [pending.Command.ActorId],
            ResolvedValue = cost.Amount,
            ContentRevision = run.Determinism.ContentRevision,
            Provenance = provenance
        }).ToImmutableArray();
        return _effects.Execute(new EffectTriggerExecutionRequest
        {
            Run = run,
            Combat = combat,
            Trigger = new EffectTriggerDefinition { TriggerId = refund ? "reaction.cost.refund" : "reaction.cost.reserve" },
            OwnerEntityId = pending.Command.ActorId,
            SourceEntityId = pending.Command.ActorId,
            ContentRevision = run.Determinism.ContentRevision,
            PrefixCommands = commands,
            Provenance = provenance
        });
    }

    private bool PhaseAllowsPriority(RunState run, CombatState combat)
    {
        var sequence = ResolveSequence(run);
        return sequence.IsSuccess && combat.PhaseState != null &&
               sequence.Value.Find(combat.PhaseState.Cursor)?.AllowPriority == true;
    }

    private IEnumerable<CombatActionCommand> EnumerateCommands(
        RunState run,
        CombatState combat,
        CombatActorState actor)
    {
        if (combat.PriorityWindow != null)
            yield return BaseCommand(run, actor, ActionType.PASS_PRIORITY);
        else
        {
            yield return BaseCommand(run, actor, ActionType.PASS);
            yield return BaseCommand(run, actor, ActionType.END_TURN);
        }

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
        var reservedCards = combat.PendingActions
            .Where(item => item.Command.CardInstanceId.HasValue)
            .Select(item => item.Command.CardInstanceId!.Value)
            .ToHashSet();
        foreach (var cardId in CardZonePlaySource.CardsForActor(run, actor.InstanceId)
            .Where(id => !reservedCards.Contains(id)).OrderBy(id => id))
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

    private static bool IgnoreCosts(RunState run, CombatActionCommand command) =>
        command.IgnoreConfiguredCosts ||
        run.ResolvedMode!.CombatRules.Flow.ActionBudget.ActionCosts == ActionCostStrategy.Ignore;

    private Result<ActionDefinition> ResolveAction(RunState run, string actionId) =>
        _actions is IRevisionedActionCatalog revisioned
            ? revisioned.GetDefinition(actionId, run.Determinism.ContentRevision, run.ConfigName)
            : _actions.GetDefinition(actionId);

    private static Result ValidateActivation(
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
        if (origin == CombatCommandOrigin.PendingResolution)
            return Result.Success();
        var controllingActorId = combat.PriorityWindow?.HolderActorId ?? activation.ActiveActorId;
        if (!string.Equals(controllingActorId, command.ActorId, StringComparison.Ordinal))
            return Result.Failure($"Actor '{command.ActorId}' does not control the current combat input");
        if (combat.PriorityWindow != null && command.ActionType is ActionType.PASS or ActionType.END_TURN)
            return Result.Failure($"{command.ActionType} is not legal while a priority window is open");
        if (combat.PriorityWindow == null && origin == CombatCommandOrigin.PlayerInput && !activation.WaitingForInput)
            return Result.Failure("Combat is resolving automatic actions");
        if (combat.PriorityWindow == null && origin == CombatCommandOrigin.AutomaticController && activation.WaitingForInput)
            return Result.Failure("Combat is waiting for player input");
        if (origin == CombatCommandOrigin.PlayerInput && combat.ControllerOf(actor) != ControllerKind.Player)
            return Result.Failure("Player command requires the current player controller");
        if (origin == CombatCommandOrigin.AutomaticController && combat.ControllerOf(actor) != ControllerKind.AI)
            return Result.Failure("Automatic command requires the current AI controller");
        return Result.Success();
    }

    private Result ValidatePhase(
        RunState run,
        CombatState combat,
        CombatActionCommand command,
        IReadOnlySet<string> tags)
    {
        var sequence = ResolveSequence(run);
        return sequence.IsFailure
            ? Result.Failure(sequence.Error)
            : _phases.ValidateCommand(combat, sequence.Value, command, tags);
    }

    private Result<LegalActionCandidate> ApplyPhase(
        RunState run,
        CombatState originalCombat,
        LegalActionCandidate candidate,
        IReadOnlySet<string> tags)
    {
        var sequence = ResolveSequence(run);
        if (sequence.IsFailure) return Result<LegalActionCandidate>.Failure(sequence.Error);
        var transitioned = _phases.HandleCommand(
            candidate.SuccessorRun,
            candidate.SuccessorCombat,
            sequence.Value,
            candidate.Command,
            tags);
        if (transitioned.IsFailure) return Result<LegalActionCandidate>.Failure(transitioned.Error);
        var steps = candidate.Steps.Concat(transitioned.Value.Steps)
            .Select((step, index) => step with { Index = index }).ToImmutableArray();
        var calculations = candidate.Calculations.Concat(transitioned.Value.Calculations).ToImmutableArray();
        var applications = candidate.Applications.Concat(transitioned.Value.Applications).ToImmutableArray();
        var fingerprint = CanonicalJson.ComputeHash(new
        {
            command = candidate.Command,
            before = CanonicalJson.ComputeHash(originalCombat),
            action = candidate.ResolutionFingerprint,
            phase = transitioned.Value.Fingerprint,
            after = CanonicalJson.ComputeHash(transitioned.Value.Combat)
        });
        return Result<LegalActionCandidate>.Success(candidate with
        {
            Steps = steps,
            Calculations = calculations,
            Applications = applications,
            PhaseTransitions = transitioned.Value.Transitions,
            SuccessorCombat = transitioned.Value.Combat,
            SuccessorRun = transitioned.Value.Run,
            ResolutionFingerprint = fingerprint,
            CandidateId = fingerprint
        });
    }

    private Result<PhaseSequenceDefinition> ResolveSequence(RunState run)
    {
        var sequenceId = run.ResolvedMode?.CombatRules.DefaultPhaseSequenceId;
        if (string.IsNullOrWhiteSpace(sequenceId))
            return Result<PhaseSequenceDefinition>.Failure("Combat rules have no phase sequence id");
        var runtime = _runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        return runtime.IsFailure
            ? Result<PhaseSequenceDefinition>.Failure(runtime.Error)
            : runtime.Value.GetDefinition<PhaseSequenceDefinition>("phase-sequences", sequenceId);
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
            Costs = result.Evaluation.Costs,
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
            Costs = result.Evaluation.Costs,
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
