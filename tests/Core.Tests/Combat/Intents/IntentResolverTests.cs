using Core.Combat;
using Core.Combat.Activation;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.Intents;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Intents;

public sealed class IntentResolverTests
{
    private readonly Mock<IDecisionPolicyRegistry> _decisions = new();
    private readonly Mock<IActionManager> _actions = new();
    private readonly Mock<ILegalActionResolver> _legal = new();

    [Fact]
    public void ResolveIntent_UsesCanonicalPreviewInsteadOfParallelDamageEstimate()
    {
        var (run, combat) = State();
        var candidate = Candidate(run, combat);
        _decisions.Setup(service => service.Decide(
                It.IsAny<ControllerBinding>(), It.IsAny<DecisionPolicyRequest>()))
            .Returns(Result<DecisionPolicyResult>.Success(new DecisionPolicyResult
            {
                Candidate = candidate,
                PolicyId = "gambit",
                RuleId = "enemy_attack",
                Priority = 50,
                Intent = new GambitIntentDefinition
                {
                    DisplayName = "Enemy raises blade",
                    TelegraphType = "Attack",
                    Tags = ["intent"]
                },
                Determinism = run.Determinism,
                StateFingerprint = "state",
                DecisionFingerprint = "decision"
            }));
        _actions.Setup(manager => manager.GetDefinition("slash"))
            .Returns(Result<ActionDefinition>.Success(new ActionDefinition
            {
                ActionId = "slash", DisplayName = "Slash", Description = "Deal damage.",
                ActionType = ActionType.POWER, Tags = ["physical"]
            }));

        var result = Resolver().ResolveIntent(run, combat, "enemy-1", ["enemy_attack"]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("slash", result.Value.ActionId);
        Assert.Equal(["hero"], result.Value.TargetIds);
        Assert.Equal("preview", result.Value.PreviewFingerprint);
        Assert.Single(result.Value.PreviewApplications);
        Assert.Equal(84, result.Value.PreviewApplications[0].CurrentValue);
        Assert.Equal("decision", result.Value.DecisionFingerprint);
        Assert.Equal("enemy_attack", result.Value.RuleId);
        Assert.Contains("intent", result.Value.Tags);
        Assert.Contains("physical", result.Value.Tags);
    }

    [Fact]
    public void ResolveEnemyIntents_LockKeepsStillLegalIntentUntilActorActs()
    {
        var (run, initial) = State();
        var locked = new CombatIntent
        {
            ActorId = "enemy-1", ActionType = ActionType.POWER, ActionId = "slash",
            TargetIds = ["hero"], ActorActionCount = 0, RuleId = "enemy_attack"
        };
        var combat = initial with
        {
            ActivationState = initial.ActivationState! with { Intents = [locked] }
        };
        _legal.Setup(service => service.Evaluate(
                run, combat, It.IsAny<CombatActionCommand>(), CombatCommandOrigin.AutomaticController))
            .Returns(Result<LegalActionEvaluation>.Success(new LegalActionEvaluation
            {
                Candidate = Candidate(run, combat)
            }));

        var result = Resolver().ResolveEnemyIntents(run, combat, ["enemy_attack"], new IntentPolicyDefinition
        {
            Refresh = IntentRefreshStrategy.LockUntilActorActivation,
            WhenInvalid = InvalidIntentStrategy.Recompute
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Same(locked, result.Value.Single());
        _decisions.Verify(service => service.Decide(
            It.IsAny<ControllerBinding>(), It.IsAny<DecisionPolicyRequest>()), Times.Never);
    }

    [Fact]
    public void ResolveEnemyIntents_InvalidLockedIntentRecomputesWhenConfigured()
    {
        var (run, initial) = State();
        var locked = new CombatIntent
        {
            ActorId = "enemy-1", ActionType = ActionType.POWER, ActionId = "slash",
            TargetIds = ["hero"], ActorActionCount = 0
        };
        var combat = initial with
        {
            ActivationState = initial.ActivationState! with { Intents = [locked] }
        };
        _legal.Setup(service => service.Evaluate(
                run, combat, It.IsAny<CombatActionCommand>(), CombatCommandOrigin.AutomaticController))
            .Returns(Result<LegalActionEvaluation>.Success(new LegalActionEvaluation
            {
                FailureReasons = ["stunned"]
            }));
        _decisions.Setup(service => service.Decide(
                It.IsAny<ControllerBinding>(), It.IsAny<DecisionPolicyRequest>()))
            .Returns(Result<DecisionPolicyResult>.Success(new DecisionPolicyResult
            {
                Candidate = Candidate(run, combat) with
                {
                    Command = new CombatActionCommand
                    {
                        RunId = run.RunId, ActorId = "enemy-1", ActionType = ActionType.END_TURN
                    },
                    ActionId = null
                },
                PolicyId = "gambit", RuleId = "end", Determinism = run.Determinism,
                StateFingerprint = "state", DecisionFingerprint = "recomputed"
            }));

        var result = Resolver().ResolveEnemyIntents(run, combat, ["enemy_attack", "end"],
            new IntentPolicyDefinition
            {
                Refresh = IntentRefreshStrategy.LockUntilActorActivation,
                WhenInvalid = InvalidIntentStrategy.Recompute
            });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(ActionType.END_TURN, result.Value.Single().ActionType);
        Assert.Equal("recomputed", result.Value.Single().DecisionFingerprint);
    }

    private IntentResolver Resolver() => new(_decisions.Object, _actions.Object, _legal.Object);

    private static LegalActionCandidate Candidate(RunState run, CombatState combat) => new()
    {
        CandidateId = "candidate", Source = LegalActionSource.Ability, ActionId = "slash",
        Command = new CombatActionCommand
        {
            RunId = run.RunId, ActorId = "enemy-1", ActionType = ActionType.POWER,
            PowerId = "slash", TargetId = "hero", TargetIds = ["hero"]
        },
        Applications =
        [
            new EffectApplicationRecord
            {
                EffectInstanceId = "damage", EffectType = EffectType.DAMAGE,
                TargetEntityId = "hero", ResourceId = "health", PreviousValue = 100, CurrentValue = 84
            }
        ],
        ResolutionFingerprint = "preview", SuccessorRun = run, SuccessorCombat = combat
    };

    private static (RunState Run, CombatState Combat) State()
    {
        var actors = new[] { Actor("enemy-1", false), Actor("hero", true) };
        var combat = new CombatState
        {
            Actors = actors.ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
            ActivationState = new ActivationState { ActiveActorId = "enemy-1", WaitingForInput = false }
        };
        var run = new RunState
        {
            RunId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            PlayerEntityId = "hero",
            Determinism = DeterministicContext.Create(42, "revision")
        };
        return (run, combat);
    }

    private static CombatActorState Actor(string id, bool player) => new()
    {
        InstanceId = id, DefinitionId = id, ContentRevision = "revision", Name = id,
        SideId = player ? "player" : "opposition",
        ControllerBinding = new ControllerBinding
        {
            Kind = player ? ControllerKind.Player : ControllerKind.AI,
            PolicyId = player ? null : "gambit"
        },
        ResourceState = new ResourceSet
        {
            OwnerId = id,
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new()
                {
                    ResourceId = "health", Current = 100, Maximum = 100, Minimum = 0,
                    Definition = new ResourceDefinition { ResourceId = "health" }
                }
            }
        }
    };
}
