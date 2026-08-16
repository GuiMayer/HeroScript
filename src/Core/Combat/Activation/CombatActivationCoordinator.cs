using Core.Combat.Gambits;
using Core.Combat.Intents;
using Core.Combat.Models;
using Core.Common;
using Core.Entity.Controllers;
using Core.Events;
using Core.Events.Domain;
using Core.Run;

namespace Core.Combat.Activation;

public sealed class CombatActivationCoordinator : ICombatActivationCoordinator
{
    private const string DefaultRulesId = "default_activation";

    private readonly ICombatSystem _combatSystem;
    private readonly IRunManager _runManager;
    private readonly ICombatRunCoordinator _combatRunCoordinator;
    private readonly IGambitEngine _gambitEngine;
    private readonly IIntentResolver _intentResolver;
    private readonly IActionManager _actionManager;
    private readonly ICombatActivationRulesLoader _rulesLoader;
    private readonly IEventBus _eventBus;

    public CombatActivationCoordinator(
        ICombatSystem combatSystem,
        IRunManager runManager,
        ICombatRunCoordinator combatRunCoordinator,
        IGambitEngine gambitEngine,
        IIntentResolver intentResolver,
        IActionManager actionManager,
        ICombatActivationRulesLoader rulesLoader,
        IEventBus eventBus)
    {
        _combatSystem = combatSystem;
        _runManager = runManager;
        _combatRunCoordinator = combatRunCoordinator;
        _gambitEngine = gambitEngine;
        _intentResolver = intentResolver;
        _actionManager = actionManager;
        _rulesLoader = rulesLoader;
        _eventBus = eventBus;
    }

    public Result<CombatActivationResult> StartActivationCycle(Guid combatId, Guid runId, string? rulesId = null)
    {
        var contextResult = LoadContext(combatId, runId, rulesId);
        if (contextResult.IsFailure)
            return Result<CombatActivationResult>.Failure(contextResult.Error);

        var (combat, run, rules) = contextResult.Value;
        var order = ResolveActivationOrder(combat, rules);
        if (order.Count == 0)
            return Result<CombatActivationResult>.Failure("No alive actors available for activation");

        var activation = new ActivationState
        {
            ActiveActorId = order[0],
            Round = 1,
            ActivationIndex = 0,
            ActivationNumber = 1,
            ActivationOrder = order,
            CompletedActorIds = Array.Empty<string>(),
            WaitingForInput = IsPlayerActor(combat, run, order[0]),
            RulesId = rules.RulesId,
            RunId = runId,
            StartedAtUtc = combat.Determinism.AdvanceStep().LogicalTimestamp.UtcDateTime
        };

        var intentResult = ResolveIntentSnapshot(combat, runId, rules, activation);
        if (intentResult.IsFailure)
            return Result<CombatActivationResult>.Failure(intentResult.Error);
        activation = activation with { Intents = intentResult.Value };

        var updateResult = _combatSystem.UpdateCombatState(combatId, state => state with { ActivationState = activation });
        if (updateResult.IsFailure)
            return Result<CombatActivationResult>.Failure(updateResult.Error);

        var drawResult = ApplyStartActivationRules(runId, order[0], updateResult.Value, run, rules);
        if (drawResult.IsFailure)
            return Result<CombatActivationResult>.Failure(drawResult.Error);

        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<CombatActivationResult>.Failure(runResult.Error);

        PublishActivationStarted(updateResult.Value, activation, drawResult.Value, rules);
        return Success(updateResult.Value, runResult.Value, activation, drawResult.Value, Array.Empty<string>());
    }

    public Result<CombatActivationResult> GetActivationState(Guid combatId)
    {
        var combatResult = _combatSystem.GetCombatState(combatId);
        if (combatResult.IsFailure)
            return Result<CombatActivationResult>.Failure(combatResult.Error);

        var activation = combatResult.Value.ActivationState;
        if (activation == null)
            return Result<CombatActivationResult>.Failure("Combat activation has not been started");
        if (activation.RunId is not { } runId)
            return Result<CombatActivationResult>.Failure("Activation run id is missing");

        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<CombatActivationResult>.Failure(runResult.Error);

        return Success(combatResult.Value, runResult.Value, activation, Array.Empty<string>(), Array.Empty<string>());
    }

    public Result<CombatActivationResult> EndCurrentActivation(Guid combatId, Guid runId, string actorId)
    {
        var contextResult = LoadContextFromActivation(combatId, runId);
        if (contextResult.IsFailure)
            return Result<CombatActivationResult>.Failure(contextResult.Error);

        var (combat, run, rules, activation) = contextResult.Value;
        if (!string.Equals(activation.ActiveActorId, actorId, StringComparison.OrdinalIgnoreCase))
            return Result<CombatActivationResult>.Failure($"Actor {actorId} is not the active actor");

        var discardResult = ApplyEndActivationRules(runId, actorId, combat, run, rules);
        if (discardResult.IsFailure)
            return Result<CombatActivationResult>.Failure(discardResult.Error);

        PublishActivationEnded(combat, activation, discardResult.Value, rules);
        return AdvanceToNextActivation(combatId, runId, discardResult.Value);
    }

    public Result<CombatActivationResult> AdvanceToNextActivation(Guid combatId, Guid runId)
    {
        return AdvanceToNextActivation(combatId, runId, Array.Empty<string>());
    }

    public Result<CombatActivationResult> ProcessCurrentAiActivation(Guid combatId, Guid runId, IReadOnlyList<string>? gambitIds = null)
    {
        var contextResult = LoadContextFromActivation(combatId, runId);
        if (contextResult.IsFailure)
            return Result<CombatActivationResult>.Failure(contextResult.Error);

        var (combat, _, rules, activation) = contextResult.Value;
        if (string.IsNullOrWhiteSpace(activation.ActiveActorId))
            return Result<CombatActivationResult>.Failure("No active actor to process");

        var activeActor = combat.GetEntity(activation.ActiveActorId);
        if (activeActor == null)
            return Result<CombatActivationResult>.Failure($"Active actor not found: {activation.ActiveActorId}");
        if (activeActor.IsHero)
            return Result<CombatActivationResult>.Failure("Active actor is not AI-controlled");

        var actionResult = ResolveAiAction(combat, activeActor.EntityId, rules, activation, gambitIds);
        if (actionResult.IsFailure)
            return Result<CombatActivationResult>.Failure(actionResult.Error);

        var command = new CombatActionCommand
        {
            ActorId = activeActor.EntityId,
            ActionType = actionResult.Value.ActionType,
            PowerId = actionResult.Value.PowerId,
            TargetId = actionResult.Value.TargetId,
            CostOptionId = actionResult.Value.CostOptionId?.ToString(),
            RunId = runId
        };

        var combatResult = command.ActionType is ActionType.PASS or ActionType.END_TURN
            ? _combatSystem.ExecuteAction(combatId, command)
            : _combatSystem.ExecuteAction(combatId, command);
        if (combatResult.IsFailure)
            return Result<CombatActivationResult>.Failure(combatResult.Error);

        if (rules.Ai.AutoEndAfterAction)
            return EndCurrentActivation(combatId, runId, activeActor.EntityId);

        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<CombatActivationResult>.Failure(runResult.Error);

        var updatedActivation = combatResult.Value.ActivationState ?? activation;
        return Success(combatResult.Value, runResult.Value, updatedActivation, Array.Empty<string>(), Array.Empty<string>());
    }

    private Result<CombatActivationResult> AdvanceToNextActivation(Guid combatId, Guid runId, IReadOnlyList<string> discardedCards)
    {
        var contextResult = LoadContextFromActivation(combatId, runId);
        if (contextResult.IsFailure)
            return Result<CombatActivationResult>.Failure(contextResult.Error);

        var (combat, run, rules, activation) = contextResult.Value;
        var completed = activation.CompletedActorIds.ToList();
        if (!string.IsNullOrWhiteSpace(activation.ActiveActorId) && !completed.Contains(activation.ActiveActorId))
            completed.Add(activation.ActiveActorId);

        var order = activation.ActivationOrder.Where(actorId => IsActorEligible(combat, actorId, rules)).ToList();
        if (order.Count == 0)
            return Result<CombatActivationResult>.Failure("No alive actors available for activation");

        var nextIndex = FindNextIndex(order, activation.ActivationIndex, completed);
        var round = activation.Round;
        if (nextIndex < 0)
        {
            round += 1;
            completed.Clear();
            order = ResolveActivationOrder(combat, rules);
            nextIndex = 0;
        }

        var nextActorId = order[nextIndex];
        var nextActivation = activation with
        {
            ActiveActorId = nextActorId,
            Round = round,
            ActivationIndex = nextIndex,
            ActivationNumber = activation.ActivationNumber + 1,
            ActivationOrder = order,
            CompletedActorIds = completed,
            WaitingForInput = IsPlayerActor(combat, run, nextActorId),
            StartedAtUtc = combat.Determinism.AdvanceStep().LogicalTimestamp.UtcDateTime
        };

        var intentResult = ResolveIntentSnapshot(combat, runId, rules, nextActivation);
        if (intentResult.IsFailure)
            return Result<CombatActivationResult>.Failure(intentResult.Error);
        nextActivation = nextActivation with { Intents = intentResult.Value };

        var updateResult = _combatSystem.UpdateCombatState(combatId, state => state with { ActivationState = nextActivation });
        if (updateResult.IsFailure)
            return Result<CombatActivationResult>.Failure(updateResult.Error);

        var drawResult = ApplyStartActivationRules(runId, nextActorId, updateResult.Value, run, rules);
        if (drawResult.IsFailure)
            return Result<CombatActivationResult>.Failure(drawResult.Error);

        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<CombatActivationResult>.Failure(runResult.Error);

        PublishActivationAdvanced(updateResult.Value, nextActivation, rules);
        PublishActivationStarted(updateResult.Value, nextActivation, drawResult.Value, rules);
        return Success(updateResult.Value, runResult.Value, nextActivation, drawResult.Value, discardedCards);
    }

    private Result<IReadOnlyList<string>> ApplyStartActivationRules(Guid runId, string actorId, CombatState combat, RunState run, CombatActivationRulesDefinition rules)
    {
        if (rules.StartActivation.DrawCount <= 0 || !ScopeApplies(rules.StartActivation.DrawActorScope, combat, run, actorId))
            return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());

        return _runManager.DrawCards(runId, rules.StartActivation.DrawCount);
    }

    private Result<IReadOnlyList<CombatIntent>> ResolveIntentSnapshot(CombatState combat, Guid runId, CombatActivationRulesDefinition rules, ActivationState activation)
    {
        if (!rules.Intents.Enabled)
            return Result<IReadOnlyList<CombatIntent>>.Success(Array.Empty<CombatIntent>());

        IReadOnlyList<string>? gambitIds = rules.Intents.GambitIds.Count > 0 ? rules.Intents.GambitIds : null;
        return rules.Intents.ActorScope switch
        {
            ActivationIntentActorScope.ActiveActor when !string.IsNullOrWhiteSpace(activation.ActiveActorId) =>
                SingleIntent(combat, activation.ActiveActorId, runId, gambitIds),
            ActivationIntentActorScope.ActiveActor => Result<IReadOnlyList<CombatIntent>>.Success(Array.Empty<CombatIntent>()),
            ActivationIntentActorScope.AllActors => ResolveActorIntents(combat, combat.GetAllEntities().Where(entity => entity.IsAlive).Select(entity => entity.EntityId).ToList(), runId, gambitIds),
            _ => _intentResolver.ResolveEnemyIntents(combat, runId, gambitIds)
        };
    }

    private Result<IReadOnlyList<CombatIntent>> SingleIntent(CombatState combat, string actorId, Guid runId, IReadOnlyList<string>? gambitIds)
    {
        var intent = _intentResolver.ResolveIntent(combat, actorId, runId, gambitIds);
        return intent.IsFailure
            ? Result<IReadOnlyList<CombatIntent>>.Failure(intent.Error)
            : Result<IReadOnlyList<CombatIntent>>.Success(new[] { intent.Value });
    }

    private Result<IReadOnlyList<CombatIntent>> ResolveActorIntents(CombatState combat, IReadOnlyList<string> actorIds, Guid runId, IReadOnlyList<string>? gambitIds)
    {
        var intents = new List<CombatIntent>();
        foreach (var actorId in actorIds)
        {
            var intent = _intentResolver.ResolveIntent(combat, actorId, runId, gambitIds);
            if (intent.IsFailure)
                return Result<IReadOnlyList<CombatIntent>>.Failure(intent.Error);
            intents.Add(intent.Value);
        }

        return Result<IReadOnlyList<CombatIntent>>.Success(intents);
    }

    private Result<EntityAction> ResolveAiAction(CombatState combat, string actorId, CombatActivationRulesDefinition rules, ActivationState activation, IReadOnlyList<string>? gambitIds)
    {
        if (rules.Intents.Enabled && rules.Intents.Authoritative)
        {
            var intent = activation.Intents.FirstOrDefault(item => string.Equals(item.ActorId, actorId, StringComparison.OrdinalIgnoreCase));
            if (intent != null)
            {
                return Result<EntityAction>.Success(new EntityAction
                {
                    ActionType = intent.ActionType,
                    PowerId = intent.PowerId,
                    TargetId = intent.TargetId,
                    CostOptionId = string.IsNullOrWhiteSpace(intent.CostOptionId) ? null : int.Parse(intent.CostOptionId)
                });
            }
        }

        var actor = combat.GetEntity(actorId);
        if (actor == null)
            return Result<EntityAction>.Failure($"Actor not found: {actorId}");

        return _gambitEngine.DecideAction(new Entity.Entity { EntityId = actor.EntityId, DisplayName = actor.Name }, combat, gambitIds);
    }

    private Result<IReadOnlyList<string>> ApplyEndActivationRules(Guid runId, string actorId, CombatState combat, RunState run, CombatActivationRulesDefinition rules)
    {
        if (rules.EndActivation.DiscardPolicy == ActivationDiscardPolicy.None || !ScopeApplies(rules.EndActivation.DiscardActorScope, combat, run, actorId))
            return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());

        var cards = SelectCardsToDiscard(run, rules);
        if (cards.IsFailure)
            return Result<IReadOnlyList<string>>.Failure(cards.Error);
        if (cards.Value.Count == 0)
            return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());

        return _runManager.DiscardCards(runId, cards.Value);
    }

    private Result<IReadOnlyList<string>> SelectCardsToDiscard(RunState run, CombatActivationRulesDefinition rules)
    {
        var hand = run.Deck.Hand.ToList();
        return rules.EndActivation.DiscardPolicy switch
        {
            ActivationDiscardPolicy.DiscardAll => Result<IReadOnlyList<string>>.Success(hand),
            ActivationDiscardPolicy.DiscardNonRetain => SelectNonRetainedCards(hand, rules),
            ActivationDiscardPolicy.DiscardDownToHandLimit => SelectCardsOverHandLimit(hand, rules),
            _ => Result<IReadOnlyList<string>>.Success(Array.Empty<string>())
        };
    }

    private Result<IReadOnlyList<string>> SelectNonRetainedCards(IReadOnlyList<string> hand, CombatActivationRulesDefinition rules)
    {
        var selected = new List<string>();
        foreach (var cardId in hand)
        {
            var retained = IsRetained(cardId, rules);
            if (retained.IsFailure)
                return Result<IReadOnlyList<string>>.Failure(retained.Error);
            if (!retained.Value)
                selected.Add(cardId);
        }

        return Result<IReadOnlyList<string>>.Success(selected);
    }

    private Result<IReadOnlyList<string>> SelectCardsOverHandLimit(IReadOnlyList<string> hand, CombatActivationRulesDefinition rules)
    {
        var limit = rules.EndActivation.HandLimit ?? hand.Count;
        if (hand.Count <= limit)
            return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());

        return Result<IReadOnlyList<string>>.Success(hand.Take(hand.Count - limit).ToList());
    }

    private Result<bool> IsRetained(string cardId, CombatActivationRulesDefinition rules)
    {
        var definition = _actionManager.GetDefinition(cardId);
        if (definition.IsFailure)
        {
            return rules.EndActivation.UnknownCardPolicy == UnknownCardPolicy.Discard
                ? Result<bool>.Success(false)
                : Result<bool>.Failure(definition.Error);
        }

        var retainTags = rules.EndActivation.RetainTags;
        return Result<bool>.Success(definition.Value.Tags.Any(tag => retainTags.Any(retain => string.Equals(retain, tag, StringComparison.OrdinalIgnoreCase))));
    }

    private Result<(CombatState Combat, RunState Run, CombatActivationRulesDefinition Rules)> LoadContext(Guid combatId, Guid runId, string? rulesId)
    {
        var combatResult = _combatSystem.GetCombatState(combatId);
        if (combatResult.IsFailure)
            return Result<(CombatState, RunState, CombatActivationRulesDefinition)>.Failure(combatResult.Error);

        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<(CombatState, RunState, CombatActivationRulesDefinition)>.Failure(runResult.Error);

        var resolvedRulesId = string.IsNullOrWhiteSpace(rulesId) ? DefaultRulesId : rulesId;
        var rulesResult = _rulesLoader.Load(runResult.Value.ConfigName, resolvedRulesId);
        if (rulesResult.IsFailure)
            return Result<(CombatState, RunState, CombatActivationRulesDefinition)>.Failure(rulesResult.Error);

        return Result<(CombatState, RunState, CombatActivationRulesDefinition)>.Success((combatResult.Value, runResult.Value, rulesResult.Value));
    }

    private Result<(CombatState Combat, RunState Run, CombatActivationRulesDefinition Rules, ActivationState Activation)> LoadContextFromActivation(Guid combatId, Guid runId)
    {
        var combatResult = _combatSystem.GetCombatState(combatId);
        if (combatResult.IsFailure)
            return Result<(CombatState, RunState, CombatActivationRulesDefinition, ActivationState)>.Failure(combatResult.Error);

        var activation = combatResult.Value.ActivationState;
        if (activation == null)
            return Result<(CombatState, RunState, CombatActivationRulesDefinition, ActivationState)>.Failure("Combat activation has not been started");

        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<(CombatState, RunState, CombatActivationRulesDefinition, ActivationState)>.Failure(runResult.Error);

        var rulesResult = _rulesLoader.Load(runResult.Value.ConfigName, activation.RulesId ?? DefaultRulesId);
        if (rulesResult.IsFailure)
            return Result<(CombatState, RunState, CombatActivationRulesDefinition, ActivationState)>.Failure(rulesResult.Error);

        return Result<(CombatState, RunState, CombatActivationRulesDefinition, ActivationState)>.Success((combatResult.Value, runResult.Value, rulesResult.Value, activation));
    }

    private static List<string> ResolveActivationOrder(CombatState state, CombatActivationRulesDefinition rules)
    {
        var order = rules.TurnOrderSource.Equals("existing_turn_order", StringComparison.OrdinalIgnoreCase) && state.TurnOrder is { Count: > 0 }
            ? state.TurnOrder.ToList()
            : state.GetAllEntities().Select(entity => entity.EntityId).ToList();

        return order.Where(actorId => IsActorEligible(state, actorId, rules)).Distinct().ToList();
    }

    private static bool IsActorEligible(CombatState state, string actorId, CombatActivationRulesDefinition rules)
    {
        var actor = state.GetEntity(actorId);
        return actor != null && (!rules.SkipDeadActors || actor.IsAlive);
    }

    private static int FindNextIndex(IReadOnlyList<string> order, int currentIndex, IReadOnlyCollection<string> completed)
    {
        for (var i = currentIndex + 1; i < order.Count; i++)
        {
            if (!completed.Contains(order[i]))
                return i;
        }

        return -1;
    }

    private static bool ScopeApplies(ActivationActorScope scope, CombatState combat, RunState run, string actorId)
    {
        return scope == ActivationActorScope.AllActors || IsPlayerActor(combat, run, actorId);
    }

    private static bool IsPlayerActor(CombatState combat, RunState run, string actorId)
    {
        return string.Equals(actorId, run.PlayerEntityId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(actorId, combat.Hero.EntityId, StringComparison.OrdinalIgnoreCase);
    }

    private static Result<CombatActivationResult> Success(CombatState combat, RunState run, ActivationState activation, IReadOnlyList<string> drawn, IReadOnlyList<string> discarded)
    {
        return Result<CombatActivationResult>.Success(new CombatActivationResult
        {
            CombatState = combat,
            RunState = run,
            ActivationState = activation,
            DrawnCardIds = drawn,
            DiscardedCardIds = discarded,
            Intents = activation.Intents
        });
    }

    private void PublishActivationStarted(CombatState combat, ActivationState activation, IReadOnlyList<string> drawnCards, CombatActivationRulesDefinition rules)
    {
        if (!rules.Events.EmitActivationEvents || string.IsNullOrWhiteSpace(activation.ActiveActorId))
            return;

        _eventBus.Publish(new ActivationStartedEvent(combat.CombatId, activation.RunId, activation.ActiveActorId, activation.Round, activation.ActivationIndex, drawnCards, combat.CurrentTurn));
    }

    private void PublishActivationEnded(CombatState combat, ActivationState activation, IReadOnlyList<string> discardedCards, CombatActivationRulesDefinition rules)
    {
        if (!rules.Events.EmitActivationEvents || string.IsNullOrWhiteSpace(activation.ActiveActorId))
            return;

        _eventBus.Publish(new ActivationEndedEvent(combat.CombatId, activation.RunId, activation.ActiveActorId, activation.Round, activation.ActivationIndex, discardedCards, combat.CurrentTurn));
    }

    private void PublishActivationAdvanced(CombatState combat, ActivationState activation, CombatActivationRulesDefinition rules)
    {
        if (!rules.Events.EmitActivationEvents || string.IsNullOrWhiteSpace(activation.ActiveActorId))
            return;

        _eventBus.Publish(new ActivationAdvancedEvent(combat.CombatId, activation.RunId, activation.ActiveActorId, activation.Round, activation.ActivationIndex, combat.CurrentTurn));
    }
}
