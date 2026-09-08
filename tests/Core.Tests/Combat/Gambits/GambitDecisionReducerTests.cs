using Core.Combat.Gambits;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Content;
using System.Collections.Immutable;
using Core.Math;
using Core.Resources;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Gambits;

public sealed class GambitDecisionReducerTests
{
    [Fact]
    public void Decide_UsesFormulaPredicateAndHighestPriorityLegalCandidate()
    {
        var (request, legal) = Context(20);
        var formulas = FormulaEvaluator();
        var reducer = new GambitDecisionReducer(formulas.Object);
        var rules = new[]
        {
            Rule("heal", 100, "actor_resource_health_percent", ActionType.POWER, "heal", DecisionTargetSelection.Self,
                maximum: .3f),
            Rule("attack", 10, "1", ActionType.POWER, "attack", DecisionTargetSelection.FirstOrdinal,
                relationship: SideRelationship.Enemy)
        };

        var result = reducer.Decide(request, rules, legal);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("heal", result.Value.RuleId);
        Assert.Equal("heal", result.Value.Candidate.ActionId);
        Assert.Equal("actor", result.Value.Candidate.Command.TargetId);
        Assert.Equal(request.Run.Determinism, result.Value.Determinism);
    }

    [Fact]
    public void Decide_SelectsEnemyAcrossThreeSidesWithOrdinalTieBreak()
    {
        var (request, legal) = Context(80, includeThirdSide: true);
        var reducer = new GambitDecisionReducer(FormulaEvaluator().Object);
        var rule = Rule("attack", 10, "1", ActionType.POWER, "attack", DecisionTargetSelection.FirstOrdinal,
            relationship: SideRelationship.Enemy);

        var result = reducer.Decide(request, [rule], legal);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("enemy-a", result.Value.Candidate.Command.TargetId);
    }

    [Fact]
    public void Decide_IsIdenticalAcrossTenFreshReducers()
    {
        var fingerprints = Enumerable.Range(0, 10).Select(_ =>
        {
            var (request, legal) = Context(80, includeThirdSide: true);
            var result = new GambitDecisionReducer(FormulaEvaluator().Object)
                .Decide(request,
                    [Rule("attack", 10, "1", ActionType.POWER, "attack", DecisionTargetSelection.FirstOrdinal,
                        relationship: SideRelationship.Enemy)],
                    legal);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            return result.Value.DecisionFingerprint;
        }).ToArray();

        Assert.Single(fingerprints.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void Decide_FailsWhenNoConfiguredRuleMatchesALegalAction()
    {
        var (request, legal) = Context(80);
        var result = new GambitDecisionReducer(FormulaEvaluator().Object)
            .Decide(request,
                [Rule("missing", 10, "1", ActionType.POWER, "not-owned", DecisionTargetSelection.FirstOrdinal)],
                legal);

        Assert.True(result.IsFailure);
        Assert.Contains("No configured decision", result.Error);
    }

    [Fact]
    public void GambitPolicy_MissingPinnedDefinitionIsAnError()
    {
        var (request, _) = Context(80);
        var bundle = new ContentBundle
        {
            Manifest = new ContentManifest
            {
                ConfigName = "default", Revision = request.Run.Determinism.ContentRevision
            },
            Artifacts = ImmutableDictionary<string, System.Text.Json.JsonElement>.Empty
        };
        var runtime = ContentRuntime.Create(bundle).Value;
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve(request.Run.Determinism.ContentRevision, request.Run.ConfigName))
            .Returns(Result<ContentRuntime>.Success(runtime));
        var policy = new GambitDecisionPolicy(
            runtimes.Object,
            Mock.Of<ILegalActionResolver>(),
            FormulaEvaluator().Object);

        var result = policy.Decide(request with { DecisionIds = ["missing"] });

        Assert.True(result.IsFailure);
        Assert.Contains("missing", result.Error);
    }

    [Fact]
    public void Registry_RejectsUnknownControllerPolicy()
    {
        var (request, _) = Context(80);
        var registry = new DecisionPolicyRegistry([]);

        var result = registry.Decide(
            new ControllerBinding { Kind = ControllerKind.AI, PolicyId = "unknown" },
            request);

        Assert.True(result.IsFailure);
        Assert.Contains("Unknown decision policy", result.Error);
    }

    private static Mock<IRuntimeFormulaEvaluator> FormulaEvaluator()
    {
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(service => service.Evaluate(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, float>>(),
                It.IsAny<float>()))
            .Returns((string expression, Dictionary<string, float>? variables, float _) =>
                Result<float>.Success(float.TryParse(expression, out var constant)
                    ? constant
                    : variables![expression]));
        return formulas;
    }

    private static GambitDefinition Rule(
        string id,
        int priority,
        string expression,
        ActionType type,
        string actionId,
        DecisionTargetSelection selector,
        float? maximum = null,
        SideRelationship? relationship = null) => new()
    {
        GambitId = id,
        Priority = priority,
        Predicates = [new DecisionPredicateDefinition { Expression = expression, Maximum = maximum }],
        Action = new GambitActionMatcher
        {
            ActionType = type,
            ActionId = actionId,
            TargetSelector = new DecisionTargetSelectorDefinition
            {
                Strategy = selector,
                Relationship = relationship
            }
        }
    };

    private static (DecisionPolicyRequest Request, LegalActionSet Legal) Context(
        float actorHealth,
        bool includeThirdSide = false)
    {
        var actors = new[]
        {
            Actor("actor", "blue", actorHealth),
            Actor("enemy-b", "red", 50),
            Actor("enemy-a", "red", 50),
            Actor("neutral", "green", 50)
        };
        var combat = new CombatState
        {
            Actors = actors.Take(includeThirdSide ? 4 : 3)
                .ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
            Relationships = new CombatRelationshipPolicy
            {
                DifferentSides = SideRelationship.Enemy,
                Rules =
                [
                    new SideRelationshipRule
                    {
                        FromSideId = "blue", ToSideId = "green", Relationship = SideRelationship.Neutral
                    }
                ]
            }
        };
        var context = DeterministicContext.Create(42, new string('a', 64));
        var run = new RunState { RunId = Guid.Parse("10000000-0000-0000-0000-000000000001"), Determinism = context };
        var candidates = new[]
        {
            Candidate(run, combat, "heal", "actor"),
            Candidate(run, combat, "attack", "enemy-b"),
            Candidate(run, combat, "attack", "enemy-a"),
            Candidate(run, combat, "attack", "neutral")
        };
        return (
            new DecisionPolicyRequest(run, combat, "actor", ["rules"]),
            new LegalActionSet
            {
                ActorId = "actor", Candidates = candidates, Determinism = context, StateFingerprint = "state"
            });
    }

    private static LegalActionCandidate Candidate(RunState run, CombatState combat, string actionId, string targetId) => new()
    {
        CandidateId = $"{actionId}:{targetId}",
        Source = LegalActionSource.Ability,
        ActionId = actionId,
        Command = new CombatActionCommand
        {
            RunId = run.RunId,
            ActorId = "actor",
            ActionType = ActionType.POWER,
            PowerId = actionId,
            TargetId = targetId,
            TargetIds = [targetId]
        },
        SuccessorRun = run,
        SuccessorCombat = combat,
        ResolutionFingerprint = $"preview:{actionId}:{targetId}"
    };

    private static CombatActorState Actor(string id, string side, float health) => new()
    {
        InstanceId = id,
        DefinitionId = id,
        ContentRevision = "revision",
        Name = id,
        SideId = side,
        ControllerBinding = new ControllerBinding { Kind = ControllerKind.AI, PolicyId = "gambit" },
        ResourceState = new ResourceSet
        {
            OwnerId = id,
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new()
                {
                    ResourceId = "health", Current = health, Maximum = 100, Minimum = 0,
                    Definition = new ResourceDefinition { ResourceId = "health" }
                }
            }
        }
    };
}
