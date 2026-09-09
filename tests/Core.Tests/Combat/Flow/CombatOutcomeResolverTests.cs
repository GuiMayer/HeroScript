using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Reactions;
using Core.Resources;
using System.Collections.Immutable;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class CombatOutcomeResolverTests
{
    private readonly CombatOutcomeResolver _resolver = new();

    [Fact]
    public void Evaluate_UsesConfiguredResourceThresholdRatherThanResourceIdentity()
    {
        var combat = Combat(
            Actor("hero", "players", ControllerKind.Player, Resource("focus", 4)),
            Actor("enemy", "enemies", ControllerKind.AI, Resource("morale", 0, defeatsOwner: true)));

        var resolved = _resolver.Evaluate(
            combat,
            Policy(OutcomeEvaluationBoundary.AfterCurrentAction),
            "hero",
            CombatOutcomeCheckpoint.ActionResolution);

        Assert.Equal(CombatStatus.VICTORY, resolved.Status);
        Assert.Equal(4, combat.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(0, combat.GetActor("enemy")!.GetResource("morale")!.Current);
    }

    [Fact]
    public void Evaluate_WaitsForOpenResolutionStackBeforePublishingOutcome()
    {
        var combat = Combat(
            Actor("hero", "players", ControllerKind.Player, Resource("resolve", 10)),
            Actor("enemy", "enemies", ControllerKind.AI, Resource("resolve", 0, defeatsOwner: true))) with
        {
            PriorityWindow = new PriorityWindowState
            {
                WindowId = "response-window",
                OpenedByActorId = "hero",
                HolderActorId = "hero",
                EligibleActorIds = ["hero"]
            }
        };
        var policy = Policy(OutcomeEvaluationBoundary.AfterResolutionStack);

        var delayed = _resolver.Evaluate(
            combat,
            policy,
            "hero",
            CombatOutcomeCheckpoint.ActionResolution);
        var resolved = _resolver.Evaluate(
            combat with { PriorityWindow = null },
            policy,
            "hero",
            CombatOutcomeCheckpoint.ActionResolution);

        Assert.Equal(CombatStatus.ACTIVE, delayed.Status);
        Assert.Equal(CombatStatus.VICTORY, resolved.Status);
    }

    [Fact]
    public void Evaluate_TreatsAlliedAiActorAsPartOfPlayerCoalition()
    {
        var combat = Combat(
            Actor("hero", "players", ControllerKind.Player, Resource("will", 0, defeatsOwner: true)),
            Actor("companion", "players", ControllerKind.AI, Resource("will", 5)),
            Actor("enemy", "enemies", ControllerKind.AI, Resource("will", 0, defeatsOwner: true)));

        var resolved = _resolver.Evaluate(
            combat,
            Policy(OutcomeEvaluationBoundary.Immediate),
            "companion",
            CombatOutcomeCheckpoint.ActionResolution);

        Assert.Equal(CombatStatus.VICTORY, resolved.Status);
    }

    private static OutcomePolicyDefinition Policy(OutcomeEvaluationBoundary boundary) => new()
    {
        EvaluationBoundary = boundary,
        TieBreak = OutcomeTieBreak.Draw
    };

    private static CombatState Combat(params CombatActorState[] actors) => new()
    {
        Actors = actors.ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
        ActorOrder = actors.Select(actor => actor.InstanceId).ToArray(),
        Sides = actors.Select(actor => actor.SideId).Distinct(StringComparer.Ordinal)
            .Select(sideId => new CombatSide { SideId = sideId }).ToImmutableArray(),
        Relationships = new CombatRelationshipPolicy
        {
            SameSide = SideRelationship.Ally,
            DifferentSides = SideRelationship.Enemy
        }
    };

    private static CombatActorState Actor(
        string instanceId,
        string sideId,
        ControllerKind controller,
        ResourcePool resource) => new()
        {
            InstanceId = instanceId,
            DefinitionId = instanceId,
            ContentRevision = "test-revision",
            Name = instanceId,
            SideId = sideId,
            ControllerBinding = new ControllerBinding
            {
                Kind = controller,
                PolicyId = controller == ControllerKind.AI ? "test-ai" : null
            },
            ResourceState = new ResourceSet
            {
                OwnerId = instanceId,
                Resources = new Dictionary<string, ResourcePool>
                {
                    [resource.ResourceId] = resource
                }
            }
        };

    private static ResourcePool Resource(string resourceId, float current, bool defeatsOwner = false) => new()
    {
        ResourceId = resourceId,
        Current = current,
        Minimum = 0,
        Maximum = 10,
        Definition = new ResourceDefinition
        {
            ResourceId = resourceId,
            DisplayName = resourceId,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = current,
            CanBeNegative = false,
            CanExceedMax = false,
            ThresholdPolicies = defeatsOwner
                ?
                [
                    new ResourceThresholdPolicy
                    {
                        PolicyId = $"{resourceId}-defeat",
                        Comparison = ResourceThresholdComparison.LessThanOrEqual,
                        ThresholdSource = ResourceThresholdSource.Minimum,
                        Consequence = ResourceThresholdConsequence.DefeatOwner,
                        Priority = 100
                    }
                ]
                : []
        }
    };
}
