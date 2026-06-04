using Core.Combat.Activation;
using Core.Combat;
using Core.Combat.Gambits;
using Core.Combat.Intents;
using Core.Combat.Models;
using Core.Common;
using Core.Events;
using Core.Resources;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Activation;

public sealed class CombatActivationCoordinatorTests
{
    private readonly Mock<ICombatSystem> _combatSystem = new();
    private readonly Mock<IRunManager> _runManager = new();
    private readonly Mock<ICombatRunCoordinator> _combatRunCoordinator = new();
    private readonly Mock<IGambitEngine> _gambitEngine = new();
    private readonly Mock<IIntentResolver> _intentResolver = new();
    private readonly Mock<IActionManager> _actionManager = new();
    private readonly Mock<ICombatActivationRulesLoader> _rulesLoader = new();
    private readonly RecordingEventBus _eventBus = new();

    [Fact]
    public void StartActivationCycle_ActivatesFirstAliveActorAndDrawsCards()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var combat = CreateCombat(combatId);
        var run = CreateRun(runId, hand: new[] { "strike" }, draw: new[] { "defend" });
        var rules = Rules(drawCount: 1);
        var updatedCombat = combat with
        {
            ActivationState = new ActivationState
            {
                ActiveActorId = "hero",
                ActivationOrder = new[] { "hero", "enemy" },
                WaitingForInput = true,
                RulesId = "default_activation",
                RunId = runId
            }
        };
        var runAfterDraw = CreateRun(runId, hand: new[] { "strike", "defend" });
        SetupContext(combatId, runId, combat, run, rules);
        _combatSystem.Setup(m => m.UpdateCombatState(combatId, It.IsAny<Func<CombatState, CombatState>>()))
            .Returns((Guid _, Func<CombatState, CombatState> update) => Result<CombatState>.Success(update(combat)));
        _runManager.Setup(m => m.DrawCards(runId, 1)).Returns(Result<IReadOnlyList<string>>.Success(new[] { "defend" }));
        _runManager.SetupSequence(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(run))
            .Returns(Result<RunState>.Success(runAfterDraw));

        var coordinator = CreateCoordinator();
        var result = coordinator.StartActivationCycle(combatId, runId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("hero", result.Value.ActivationState.ActiveActorId);
        Assert.True(result.Value.ActivationState.WaitingForInput);
        Assert.Equal(new[] { "defend" }, result.Value.DrawnCardIds);
        Assert.Contains(_eventBus.Events, e => e.EventType == "ActivationStartedEvent");
    }

    [Fact]
    public void EndCurrentActivation_DiscardsNonRetainCardsAndAdvancesToEnemy()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var activation = new ActivationState
        {
            ActiveActorId = "hero",
            Round = 1,
            ActivationIndex = 0,
            ActivationNumber = 1,
            ActivationOrder = new[] { "hero", "enemy" },
            CompletedActorIds = Array.Empty<string>(),
            WaitingForInput = true,
            RulesId = "default_activation",
            RunId = runId
        };
        var combat = CreateCombat(combatId) with { ActivationState = activation };
        var run = CreateRun(runId, hand: new[] { "strike", "keep" });
        var rules = Rules(discardPolicy: ActivationDiscardPolicy.DiscardNonRetain, drawCount: 0);
        var runAfterDiscard = CreateRun(runId, hand: new[] { "keep" }, discard: new[] { "strike" });
        SetupContext(combatId, runId, combat, run, rules);
        _actionManager.Setup(m => m.GetDefinition("strike")).Returns(Result<ActionDefinition>.Success(new ActionDefinition { ActionId = "strike" }));
        _actionManager.Setup(m => m.GetDefinition("keep")).Returns(Result<ActionDefinition>.Success(new ActionDefinition { ActionId = "keep", Tags = new List<string> { "retain" } }));
        _runManager.Setup(m => m.DiscardCards(runId, It.Is<IReadOnlyList<string>>(cards => cards.Single() == "strike")))
            .Returns(Result<IReadOnlyList<string>>.Success(new[] { "strike" }));
        _combatSystem.Setup(m => m.UpdateCombatState(combatId, It.IsAny<Func<CombatState, CombatState>>()))
            .Returns((Guid _, Func<CombatState, CombatState> update) => Result<CombatState>.Success(update(combat)));
        _runManager.SetupSequence(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(run))
            .Returns(Result<RunState>.Success(runAfterDiscard))
            .Returns(Result<RunState>.Success(runAfterDiscard));

        var coordinator = CreateCoordinator();
        var result = coordinator.EndCurrentActivation(combatId, runId, "hero");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("enemy", result.Value.ActivationState.ActiveActorId);
        Assert.False(result.Value.ActivationState.WaitingForInput);
        Assert.Equal(new[] { "strike" }, result.Value.DiscardedCardIds);
        Assert.Contains(_eventBus.Events, e => e.EventType == "ActivationEndedEvent");
        Assert.Contains(_eventBus.Events, e => e.EventType == "ActivationAdvancedEvent");
    }

    [Fact]
    public void AdvanceToNextActivation_StartsNewRoundAfterLastActor()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var activation = new ActivationState
        {
            ActiveActorId = "enemy",
            Round = 1,
            ActivationIndex = 1,
            ActivationNumber = 2,
            ActivationOrder = new[] { "hero", "enemy" },
            CompletedActorIds = new[] { "hero" },
            RulesId = "default_activation",
            RunId = runId
        };
        var combat = CreateCombat(combatId) with { ActivationState = activation };
        var run = CreateRun(runId);
        var rules = Rules(drawCount: 0);
        SetupContext(combatId, runId, combat, run, rules);
        _combatSystem.Setup(m => m.UpdateCombatState(combatId, It.IsAny<Func<CombatState, CombatState>>()))
            .Returns((Guid _, Func<CombatState, CombatState> update) => Result<CombatState>.Success(update(combat)));
        _runManager.SetupSequence(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(run))
            .Returns(Result<RunState>.Success(run));

        var coordinator = CreateCoordinator();
        var result = coordinator.AdvanceToNextActivation(combatId, runId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.ActivationState.Round);
        Assert.Equal("hero", result.Value.ActivationState.ActiveActorId);
        Assert.Empty(result.Value.ActivationState.CompletedActorIds);
    }

    [Fact]
    public void StartActivationCycle_WhenIntentsEnabled_StoresConfiguredIntentSnapshot()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var combat = CreateCombat(combatId);
        var run = CreateRun(runId);
        var rules = Rules(drawCount: 0) with
        {
            Intents = new ActivationIntentRules
            {
                Enabled = true,
                Authoritative = true,
                ActorScope = ActivationIntentActorScope.EnemiesOnly,
                GambitIds = new List<string> { "enemy_default" }
            }
        };
        var intent = new CombatIntent { ActorId = "enemy", PowerId = "enemy_strike", TelegraphType = "Attack" };
        SetupContext(combatId, runId, combat, run, rules);
        _intentResolver.Setup(m => m.ResolveEnemyIntents(combat, runId, It.Is<IReadOnlyList<string>>(ids => ids.Single() == "enemy_default")))
            .Returns(Result<IReadOnlyList<CombatIntent>>.Success(new[] { intent }));
        _combatSystem.Setup(m => m.UpdateCombatState(combatId, It.IsAny<Func<CombatState, CombatState>>()))
            .Returns((Guid _, Func<CombatState, CombatState> update) => Result<CombatState>.Success(update(combat)));
        _runManager.SetupSequence(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(run))
            .Returns(Result<RunState>.Success(run));

        var coordinator = CreateCoordinator();
        var result = coordinator.StartActivationCycle(combatId, runId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(result.Value.ActivationState.Intents);
        Assert.Equal("enemy_strike", result.Value.Intents.Single().PowerId);
    }

    [Fact]
    public void ProcessCurrentAiActivation_WhenIntentsAuthoritative_ExecutesStoredIntent()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var activation = new ActivationState
        {
            ActiveActorId = "enemy",
            Round = 1,
            ActivationIndex = 1,
            ActivationNumber = 2,
            ActivationOrder = new[] { "hero", "enemy" },
            CompletedActorIds = new[] { "hero" },
            RulesId = "default_activation",
            RunId = runId,
            Intents = new[]
            {
                new CombatIntent
                {
                    ActorId = "enemy",
                    ActionType = ActionType.POWER,
                    PowerId = "enemy_strike",
                    TargetId = "hero",
                    CostOptionId = "1"
                }
            }
        };
        var combat = CreateCombat(combatId) with { ActivationState = activation };
        var run = CreateRun(runId);
        var rules = Rules(drawCount: 0) with
        {
            Ai = new ActivationAiRules { AutoEndAfterAction = false },
            Intents = new ActivationIntentRules { Enabled = true, Authoritative = true }
        };
        var combatAfterAction = combat with { ActivationState = activation };
        SetupContext(combatId, runId, combat, run, rules);
        _combatSystem.Setup(m => m.ExecuteAction(combatId, It.Is<CombatActionCommand>(command =>
                command.ActorId == "enemy" &&
                command.ActionType == ActionType.POWER &&
                command.PowerId == "enemy_strike" &&
                command.TargetId == "hero" &&
                command.CostOptionId == "1" &&
                command.RunId == runId)))
            .Returns(Result<CombatState>.Success(combatAfterAction));
        _runManager.SetupSequence(m => m.GetRun(runId))
            .Returns(Result<RunState>.Success(run))
            .Returns(Result<RunState>.Success(run));

        var coordinator = CreateCoordinator();
        var result = coordinator.ProcessCurrentAiActivation(combatId, runId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        _gambitEngine.Verify(m => m.DecideAction(It.IsAny<Core.Entity.Entity>(), It.IsAny<CombatState>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    private CombatActivationCoordinator CreateCoordinator() => new(
        _combatSystem.Object,
        _runManager.Object,
        _combatRunCoordinator.Object,
        _gambitEngine.Object,
        _intentResolver.Object,
        _actionManager.Object,
        _rulesLoader.Object,
        _eventBus);

    private void SetupContext(Guid combatId, Guid runId, CombatState combat, RunState run, CombatActivationRulesDefinition rules)
    {
        _combatSystem.Setup(m => m.GetCombatState(combatId)).Returns(Result<CombatState>.Success(combat));
        _runManager.Setup(m => m.GetRun(runId)).Returns(Result<RunState>.Success(run));
        _rulesLoader.Setup(m => m.Load(run.ConfigName, rules.RulesId)).Returns(Result<CombatActivationRulesDefinition>.Success(rules));
    }

    private static CombatActivationRulesDefinition Rules(
        int drawCount = 0,
        ActivationDiscardPolicy discardPolicy = ActivationDiscardPolicy.None) => new()
        {
            RulesId = "default_activation",
            StartActivation = new ActivationStartRules { DrawCount = drawCount },
            EndActivation = new ActivationEndRules
            {
                DiscardPolicy = discardPolicy,
                RetainTags = new List<string> { "retain" },
                UnknownCardPolicy = UnknownCardPolicy.Fail
            }
        };

    private static RunState CreateRun(
        Guid runId,
        IReadOnlyList<string>? hand = null,
        IReadOnlyList<string>? draw = null,
        IReadOnlyList<string>? discard = null) => new()
        {
            RunId = runId,
            ConfigName = "test",
            PlayerEntityId = "hero",
            Deck = new DeckState
            {
                Hand = hand?.ToList() ?? new List<string>(),
                DrawPile = draw?.ToList() ?? new List<string>(),
                DiscardPile = discard?.ToList() ?? new List<string>()
            }
        };

    private static CombatState CreateCombat(Guid combatId) => new()
    {
        CombatId = combatId,
        Hero = CreateEntity("hero", true, alive: true),
        Enemies = new[] { CreateEntity("enemy", false, alive: true) },
        TurnOrder = new[] { "hero", "enemy" }
    };

    private static CombatEntity CreateEntity(string id, bool isHero, bool alive) => new()
    {
        EntityId = id,
        Name = id,
        IsHero = isHero,
        ResourceState = new EntityResourceState
        {
            EntityId = id,
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new()
                {
                    ResourceId = "health",
                    Current = alive ? 10 : 0,
                    Maximum = 10,
                    Minimum = 0,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = "health",
                        DisplayName = "Health",
                        Category = ResourceCategory.VITAL
                    }
                }
            }
        }
    };

    private sealed class RecordingEventBus : IEventBus
    {
        public List<GameEvent> Events { get; } = new();

        public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
        {
            if (@event is GameEvent gameEvent)
                Events.Add(gameEvent);
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent => new NoopDisposable();
        public IReadOnlyList<IEvent> GetEventHistory() => Events;
        public IReadOnlyList<IEvent> GetEventHistory(EventCategory category) => Events.Where(e => e.Category == category).ToList();
        public IReadOnlyList<IEvent> GetEventHistory(EventSeverity severity) => Events.Where(e => e.Severity == severity).ToList();
        public void ClearHistory() => Events.Clear();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
