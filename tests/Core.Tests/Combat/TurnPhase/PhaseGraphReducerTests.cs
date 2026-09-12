using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Activation;
using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public sealed class PhaseGraphReducerTests
{
    private const string Revision = "phase-revision";

    [Fact]
    public void Enter_AdvancesAutomaticallyAndAppliesStatusAtPhaseBoundary()
    {
        var reducer = Reducer();

        var result = reducer.Enter(Run(), Combat(), Sequence(withStatus: true));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("main", result.Value.Combat.PhaseState!.Cursor);
        Assert.Equal("graph", result.Value.Combat.PhaseState.SequenceId);
        Assert.Equal(Revision, result.Value.Combat.PhaseState.ContentRevision);
        Assert.Single(result.Value.Transitions);
        Assert.Single(result.Value.Applications);
        Assert.Single(result.Value.Combat.StatusEffects["hero"]);
        Assert.Equal("marked", result.Value.Combat.StatusEffects["hero"][0].StatusId);
    }

    [Fact]
    public void HandleCommand_SelectsHighestPriorityTrueConditionalEdge()
    {
        var reducer = Reducer();
        var entered = reducer.Enter(Run(), Combat(), Sequence()).Value;

        var result = reducer.HandleCommand(
            entered.Run,
            entered.Combat,
            Sequence(),
            new CombatActionCommand { ActorId = "hero", ActionType = ActionType.PASS },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "system" });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("empowered", result.Value.Combat.PhaseState!.Cursor);
        Assert.Equal("main-empowered", Assert.Single(result.Value.Transitions).EdgeId);
    }

    [Fact]
    public void ValidateCommand_AllowsConfiguredTagsAndRejectsUnlistedCommands()
    {
        var reducer = Reducer();
        var sequence = Sequence();
        var entered = reducer.Enter(Run(), Combat(), sequence).Value;

        var tagged = reducer.ValidateCommand(entered.Combat, sequence,
            new CombatActionCommand { ActorId = "hero", ActionType = ActionType.POWER },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "spell" });
        var rejected = reducer.ValidateCommand(entered.Combat, sequence,
            new CombatActionCommand { ActorId = "hero", ActionType = ActionType.BASIC_ATTACK },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "attack" });

        Assert.True(tagged.IsSuccess, tagged.IsFailure ? tagged.Error : null);
        Assert.True(rejected.IsFailure);
    }

    [Fact]
    public void Exit_FollowsConfiguredEdgesToEndAndAppliesExitEffects()
    {
        var reducer = Reducer();
        var sequence = Sequence(withExitEffect: true);
        var entered = reducer.Enter(Run(), Combat(), sequence).Value;

        var result = reducer.Exit(entered.Run, entered.Combat, sequence);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("end", result.Value.Combat.PhaseState!.Cursor);
        Assert.Equal(9, result.Value.Combat.GetActor("hero")!.GetResource("energy")!.Current);
        Assert.Equal("main-end", Assert.Single(result.Value.Transitions).EdgeId);
        Assert.Single(result.Value.Applications);
    }

    [Fact]
    public void AutomaticResolution_UsesConfiguredDeterministicLimit()
    {
        var sequence = Sequence() with
        {
            MaxAutomaticTransitions = 1,
            Phases =
            [
                Phase("start", PhaseRole.Start, 10,
                    [Edge("start-bridge", "bridge", PhaseEdgeTrigger.Automatic)]),
                Phase("bridge", PhaseRole.Start, 15,
                    [Edge("bridge-main", "main", PhaseEdgeTrigger.Automatic)]),
                Sequence().Find("main")!,
                Sequence().Find("empowered")!,
                Sequence().Find("fallback")!,
                Sequence().Find("end")!
            ]
        };

        var result = Reducer().Enter(Run(), Combat(), sequence);

        Assert.True(result.IsFailure);
        Assert.Contains("exceeded 1", result.Error);
    }

    [Fact]
    public void IdenticalInputs_ProduceIdenticalFingerprintTenTimes()
    {
        var fingerprints = Enumerable.Range(0, 10).Select(_ =>
        {
            var reducer = Reducer();
            var sequence = Sequence();
            var entered = reducer.Enter(Run(), Combat(), sequence).Value;
            return reducer.HandleCommand(entered.Run, entered.Combat, sequence,
                new CombatActionCommand { ActorId = "hero", ActionType = ActionType.PASS },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "system" }).Value.Fingerprint;
        }).ToArray();

        Assert.Single(fingerprints.Distinct(StringComparer.Ordinal));
    }

    private static IPhaseGraphReducer Reducer()
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(service => service.Evaluate(
                It.IsAny<string>(), It.IsAny<Dictionary<string, float>>(), It.IsAny<float>()))
            .Returns((string expression, Dictionary<string, float>? variables, float _) =>
                Result<float>.Success(expression == "has_energy"
                    ? variables!["source.resources.energy.current"] > 0 ? 1 : 0
                    : 0));
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve(Revision, It.IsAny<string?>()))
            .Returns(Result<ContentRuntime>.Success(StatusRuntime()));
        var effects = new EffectTriggerExecutor(formulas.Object, new ImmutableEffectProcessor(), runtimes.Object,
            allowUnconfiguredCalculations: true);
        return new PhaseGraphReducer(formulas.Object, effects);
    }

    private static PhaseSequenceDefinition Sequence(bool withStatus = false, bool withExitEffect = false) => new()
    {
        SequenceId = "graph",
        EntryPhaseId = "start",
        MaxAutomaticTransitions = 16,
        Phases =
        [
            Phase("start", PhaseRole.Start, 10,
                [Edge("start-main", "main", PhaseEdgeTrigger.Automatic)],
                entryEffects: withStatus
                    ? [new EffectDefinition
                    {
                        Type = EffectType.APPLY_STATUS, Target = EffectTarget.SELF,
                        StatusId = "marked", StatusStacks = 1, StatusDuration = 2
                    }]
                    : []),
            Phase("main", PhaseRole.Middle, 20,
                [
                    Edge("main-empowered", "empowered", PhaseEdgeTrigger.Command, 10,
                        "has_energy", [ActionType.PASS]),
                    Edge("main-fallback", "fallback", PhaseEdgeTrigger.Command, 0,
                        actionTypes: [ActionType.PASS]),
                    Edge("main-end", "end", PhaseEdgeTrigger.ActivationExit)
                ],
                [ActionType.PASS, ActionType.END_TURN], ["spell"],
                exitEffects: withExitEffect
                    ? [new EffectDefinition
                    {
                        Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF,
                        TargetResource = "energy", Operation = ResourceEffectOperation.SUBTRACT,
                        FlatValue = 1
                    }]
                    : []),
            Phase("empowered", PhaseRole.Middle, 30,
                [Edge("empowered-end", "end", PhaseEdgeTrigger.ActivationExit)],
                [ActionType.END_TURN]),
            Phase("fallback", PhaseRole.Middle, 40,
                [Edge("fallback-end", "end", PhaseEdgeTrigger.ActivationExit)],
                [ActionType.END_TURN]),
            Phase("end", PhaseRole.End, 50, [])
        ]
    };

    private static PhaseDefinition Phase(
        string id,
        PhaseRole role,
        int order,
        IReadOnlyList<PhaseEdgeDefinition> edges,
        IReadOnlyList<ActionType>? actions = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<EffectDefinition>? entryEffects = null,
        IReadOnlyList<EffectDefinition>? exitEffects = null) => new()
    {
        PhaseId = id,
        Role = role,
        Order = order,
        AllowedActions = actions ?? [],
        AllowedCommandTags = tags ?? [],
        Edges = edges,
        EntryEffects = entryEffects ?? [],
        ExitEffects = exitEffects ?? []
    };

    private static PhaseEdgeDefinition Edge(
        string id,
        string target,
        PhaseEdgeTrigger trigger,
        int priority = 0,
        string? condition = null,
        IReadOnlyList<ActionType>? actionTypes = null) => new()
    {
        EdgeId = id,
        TargetPhaseId = target,
        Trigger = trigger,
        Priority = priority,
        Condition = condition,
        ActionTypes = actionTypes ?? []
    };

    private static RunState Run() => new()
    {
        RunId = Guid.Parse("10000000-0000-8000-8000-000000000001"),
        PlayerEntityId = "hero",
        ConfigName = "default",
        Determinism = DeterministicContext.Create(11, Revision)
    };

    private static CombatState Combat()
    {
        var hero = new CombatActorState
        {
            InstanceId = "hero", DefinitionId = "hero", ContentRevision = Revision,
            SideId = "player", ControllerBinding = new ControllerBinding { Kind = ControllerKind.Player },
            ResourceState = new ResourceSet
            {
                OwnerId = "hero",
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = Pool("health", 20),
                    ["energy"] = Pool("energy", 10)
                }
            }
        };
        return new CombatState
        {
            CombatId = Guid.Parse("20000000-0000-8000-8000-000000000001"),
            Actors = new Dictionary<string, CombatActorState> { ["hero"] = hero },
            ActorOrder = ["hero"],
            ActivationState = new ActivationState
            {
                ActiveActorId = "hero", WaitingForInput = true, Round = 1, ActivationNumber = 1
            },
            Determinism = DeterministicContext.Create(22, Revision)
        };
    }

    private static ResourcePool Pool(string id, float value) => new()
    {
        ResourceId = id, Current = value, Minimum = 0, Maximum = value,
        Definition = new ResourceDefinition { ResourceId = id, DefaultMax = value }
    };

    private static ContentRuntime StatusRuntime()
    {
        const string path = "status-effects/status.json";
        return ContentRuntime.Create(new ContentBundle
        {
            Manifest = new ContentManifest
            {
                Revision = Revision,
                ConfigName = "default",
                Artifacts = [new() { Kind = "status-effects", Path = path, DefinitionCount = 1 }]
            },
            Artifacts = ImmutableDictionary<string, JsonElement>.Empty.Add(path,
                JsonSerializer.SerializeToElement(new Dictionary<string, StatusEffectDefinition>
                {
                    ["marked"] = new()
                    {
                        StatusId = "marked", DefaultStacks = 1, DefaultDuration = 2, MaxStacks = 10
                    }
                }))
        }).Value;
    }
}
