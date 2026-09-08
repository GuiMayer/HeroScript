using Core.Combat.Activation;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Moq;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Combat.LegalActions;

public sealed class LegalActionResolverTests
{
    [Fact]
    public void Evaluate_RejectsAbilityNotOwnedByActorBeforeExecutor()
    {
        var (run, combat) = State(includeAbility: false);
        var abilities = new Mock<IAbilityExecutor>();
        var resolver = Resolver(abilities);

        var result = resolver.Evaluate(run, combat, new CombatActionCommand
        {
            RunId = run.RunId, ActorId = "enemy", ActionType = ActionType.POWER,
            PowerId = "slash", TargetIds = ["hero"]
        }, CombatCommandOrigin.AutomaticController);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsLegal);
        Assert.Contains("does not own", Assert.Single(result.Value.FailureReasons));
        abilities.Verify(service => service.Execute(It.IsAny<AbilityExecutionRequest>()), Times.Never);
    }

    [Fact]
    public void Resolve_StatusBlockedAbilityLeavesOnlyCanonicalPassiveCommands()
    {
        var (run, combat) = State(includeAbility: true);
        var abilities = new Mock<IAbilityExecutor>();
        abilities.Setup(service => service.Execute(It.IsAny<AbilityExecutionRequest>()))
            .Returns(Result<AbilityExecutionResult>.Failure("Cannot act while stunned"));
        var resolver = Resolver(abilities);

        var result = resolver.Resolve(run, combat, "enemy", CombatCommandOrigin.AutomaticController);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal([ActionType.PASS, ActionType.END_TURN],
            result.Value.Candidates.Select(candidate => candidate.Command.ActionType));
        Assert.All(result.Value.Candidates, candidate => Assert.Equal(LegalActionSource.System, candidate.Source));
    }

    [Fact]
    public void Evaluate_ReturnsTheExactCanonicalAbilityPreview()
    {
        var (run, combat) = State(includeAbility: true);
        var successor = combat with { Determinism = combat.Determinism.AdvanceStep() };
        var preview = new AbilityExecutionResult
        {
            Combat = successor,
            Definition = new ActionDefinition { ActionId = "slash", ActionType = ActionType.POWER },
            Evaluation = new CardPlayEvaluation
            {
                IsLegal = true, ActorId = "enemy", ResolvedTargetIds = ["hero"]
            },
            ResolutionFingerprint = "canonical-preview"
        };
        var abilities = new Mock<IAbilityExecutor>();
        abilities.Setup(service => service.Execute(It.IsAny<AbilityExecutionRequest>()))
            .Returns(Result<AbilityExecutionResult>.Success(preview));
        var resolver = Resolver(abilities);

        var first = resolver.Evaluate(run, combat, new CombatActionCommand
        {
            RunId = run.RunId, ActorId = "enemy", ActionType = ActionType.POWER,
            PowerId = "slash", TargetIds = ["hero"]
        }, CombatCommandOrigin.AutomaticController);
        var second = resolver.Evaluate(run, combat, new CombatActionCommand
        {
            RunId = run.RunId, ActorId = "enemy", ActionType = ActionType.POWER,
            PowerId = "slash", TargetIds = ["hero"]
        }, CombatCommandOrigin.AutomaticController);

        Assert.True(first.Value.IsLegal);
        Assert.Equal(64, first.Value.Candidate!.ResolutionFingerprint.Length);
        Assert.NotEqual("canonical-preview", first.Value.Candidate.ResolutionFingerprint);
        Assert.Equal(first.Value.Candidate.ResolutionFingerprint, second.Value.Candidate!.ResolutionFingerprint);
        Assert.Same(preview, first.Value.Candidate.Ability);
        Assert.Same(successor, first.Value.Candidate.SuccessorCombat);
    }

    [Fact]
    public void Evaluate_PassReturnsThePostPhaseSnapshotAndTrace()
    {
        var (run, combat) = State(includeAbility: false);
        var resolver = Resolver(new Mock<IAbilityExecutor>());

        var result = resolver.Evaluate(run, combat, new CombatActionCommand
        {
            RunId = run.RunId, ActorId = "enemy", ActionType = ActionType.PASS
        }, CombatCommandOrigin.AutomaticController);

        Assert.True(result.Value.IsLegal);
        Assert.Equal("second", result.Value.Candidate!.SuccessorCombat.PhaseState!.Cursor);
        Assert.Equal("main-second", Assert.Single(result.Value.Candidate.PhaseTransitions).EdgeId);
    }

    private static LegalActionResolver Resolver(Mock<IAbilityExecutor> abilities)
    {
        var actions = new Mock<IActionManager>();
        actions.Setup(service => service.GetDefinition("slash"))
            .Returns(Result<ActionDefinition>.Success(new ActionDefinition
            {
                ActionId = "slash", ActionType = ActionType.POWER, RequiresTarget = true
            }));
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve("revision", "default"))
            .Returns(Result<ContentRuntime>.Success(Runtime()));
        return new LegalActionResolver(
            actions.Object,
            runtimes.Object,
            Mock.Of<ICardContentCompiler>(),
            Mock.Of<IEffectiveCardResolver>(),
            Mock.Of<ICardPlayEvaluator>(),
            Mock.Of<ICardPlayExecutor>(),
            abilities.Object,
            new PhaseGraphReducer(Mock.Of<IRuntimeFormulaEvaluator>(), Mock.Of<IEffectTriggerExecutor>()));
    }

    private static (RunState Run, CombatState Combat) State(bool includeAbility)
    {
        var enemy = Actor("enemy", false, includeAbility ? ["slash"] : []);
        var hero = Actor("hero", true, []);
        var combat = new CombatState
        {
            CombatId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Actors = new[] { enemy, hero }.ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
            ActivationState = new ActivationState { ActiveActorId = "enemy", WaitingForInput = false },
            PhaseState = new PhaseState
            {
                SequenceId = "test",
                ContentRevision = "revision",
                Cursor = "main"
            },
            Determinism = DeterministicContext.Create(7, "revision")
        };
        var run = new RunState
        {
            RunId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            PlayerEntityId = "hero",
            Determinism = DeterministicContext.Create(7, "revision"),
            ResolvedMode = new ResolvedGameMode
            {
                CombatRules = new CombatRulesDefinition
                {
                    DefaultPhaseSequenceId = "test",
                    Flow = new CombatFlowPoliciesDefinition
                    {
                        ActionBudget = new ActionBudgetPolicyDefinition
                        {
                            Strategy = ActionBudgetStrategy.FixedCount,
                            ActionCosts = ActionCostStrategy.Configured,
                            ActorScope = FlowActorScope.PlayerControlled,
                            MaxActionsPerActivation = 1,
                            ConsumingCommands = [GameplayCommandTypes.ExecuteAction]
                        }
                    }
                }
            }
        };
        return (run, combat);
    }

    private static ContentRuntime Runtime()
    {
        const string path = "phase-sequences/test.json";
        var sequence = new PhaseSequenceDefinition
        {
            SequenceId = "test",
            EntryPhaseId = "start",
            Phases =
            [
                new PhaseDefinition
                {
                    PhaseId = "start", Role = PhaseRole.Start, Order = 10,
                    Edges = [new()
                    {
                        EdgeId = "start-main", TargetPhaseId = "main",
                        Trigger = PhaseEdgeTrigger.Automatic
                    }]
                },
                new PhaseDefinition
                {
                    PhaseId = "main", Role = PhaseRole.Middle, Order = 20,
                    AllowedActions = [ActionType.POWER, ActionType.PASS, ActionType.END_TURN],
                    Edges =
                    [
                        new()
                        {
                            EdgeId = "main-second", TargetPhaseId = "second",
                            Trigger = PhaseEdgeTrigger.Command, ActionTypes = [ActionType.PASS]
                        },
                        new()
                        {
                            EdgeId = "main-end", TargetPhaseId = "end",
                            Trigger = PhaseEdgeTrigger.ActivationExit
                        }
                    ]
                },
                new PhaseDefinition
                {
                    PhaseId = "second", Role = PhaseRole.Middle, Order = 25,
                    AllowedActions = [ActionType.END_TURN],
                    Edges = [new()
                    {
                        EdgeId = "second-end", TargetPhaseId = "end",
                        Trigger = PhaseEdgeTrigger.ActivationExit
                    }]
                },
                new PhaseDefinition { PhaseId = "end", Role = PhaseRole.End, Order = 30 }
            ]
        };
        return ContentRuntime.Create(new ContentBundle
        {
            Manifest = new ContentManifest
            {
                Revision = "revision", ConfigName = "default",
                Artifacts = [new() { Kind = "phase-sequences", Path = path, DefinitionCount = 1 }]
            },
            Artifacts = ImmutableDictionary<string, JsonElement>.Empty.Add(path,
                JsonSerializer.SerializeToElement(new Dictionary<string, PhaseSequenceDefinition>
                {
                    ["test"] = sequence
                }))
        }).Value;
    }

    private static CombatActorState Actor(string id, bool player, IReadOnlyList<string> abilities) => new()
    {
        InstanceId = id, DefinitionId = id, ContentRevision = "revision", Name = id,
        SideId = player ? "player" : "opposition",
        ControllerBinding = new ControllerBinding
        {
            Kind = player ? ControllerKind.Player : ControllerKind.AI,
            PolicyId = player ? null : "gambit"
        },
        Components = new EntityComponentState[]
        {
            new ResourceEntityComponentState
            {
                ComponentId = "resources",
                State = new ResourceSet
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
            },
            new AbilityEntityComponentState { ComponentId = "abilities", AbilityIds = abilities }
        }.ToDictionary(component => component.ComponentId, StringComparer.Ordinal)
    };
}
