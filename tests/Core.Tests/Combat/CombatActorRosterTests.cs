using Core.Combat.Models;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat;

public sealed class CombatActorRosterTests
{
    [Fact]
    public void Roster_SupportsMultiplePlayerActorsAndThreeSidesWithoutPrivilegedRoles()
    {
        var actors = new[]
        {
            Actor("alpha-1", "alpha", ControllerKind.Player),
            Actor("alpha-2", "alpha", ControllerKind.Player),
            Actor("beta-1", "beta", ControllerKind.AI),
            Actor("gamma-1", "gamma", ControllerKind.None)
        };
        var state = new CombatState
        {
            Actors = actors.ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
            ActorOrder = ["gamma-1", "alpha-2", "beta-1", "alpha-1"],
            Sides =
            [
                new CombatSide { SideId = "alpha" },
                new CombatSide { SideId = "beta" },
                new CombatSide { SideId = "gamma" }
            ]
        };

        Assert.Equal(["gamma-1", "alpha-2", "beta-1", "alpha-1"],
            state.GetAllActors().Select(actor => actor.InstanceId));
        Assert.Equal(2, state.GetAllActors().Count(actor => actor.ControllerBinding.Kind == ControllerKind.Player));
        Assert.Single(state.GetActorsForSide("gamma"));
        Assert.Equal(SideRelationship.Enemy, state.Relationship(actors[0], actors[2]));
    }

    [Fact]
    public void Defeat_IsOwnedByAnArbitraryResourcePolicy()
    {
        var resolve = new ResourcePool
        {
            ResourceId = "resolve",
            Current = 0,
            Minimum = 0,
            Maximum = 10,
            Definition = new ResourceDefinition
            {
                ResourceId = "resolve",
                DisplayName = "Resolve",
                DefaultMin = 0,
                DefaultMax = 10,
                ThresholdPolicies =
                [
                    new ResourceThresholdPolicy
                    {
                        PolicyId = "resolve_defeat",
                        Comparison = ResourceThresholdComparison.LessThanOrEqual,
                        ThresholdSource = ResourceThresholdSource.Minimum,
                        Consequence = ResourceThresholdConsequence.DefeatOwner
                    }
                ]
            }
        };
        var actor = Actor("unit", "side", ControllerKind.None) with
        {
            ResourceState = new ResourceSet
            {
                OwnerId = "unit",
                Resources = new Dictionary<string, ResourcePool>(StringComparer.Ordinal)
                {
                    ["resolve"] = resolve
                }
            }
        };

        Assert.False(actor.IsAlive);
        Assert.Null(actor.GetResource("health"));
    }

    [Fact]
    public void Roster_DoesNotRequireAPlayerControlledOrHeroActor()
    {
        var actors = new[]
        {
            Actor("environment", "world", ControllerKind.None),
            Actor("simulation", "world", ControllerKind.AI)
        };
        var state = new CombatState
        {
            Actors = actors.ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
            ActorOrder = actors.Select(actor => actor.InstanceId).ToArray(),
            Sides = [new CombatSide { SideId = "world" }]
        };

        Assert.Equal(2, state.GetAllActors().Count());
        Assert.DoesNotContain(state.GetAllActors(),
            actor => actor.ControllerBinding.Kind == ControllerKind.Player);
    }

    [Fact]
    public void Roster_FallsBackToCanonicalDictionaryOrderWhenDeclaredOrderIsInvalid()
    {
        var actors = new[]
        {
            Actor("beta", "side", ControllerKind.None),
            Actor("alpha", "side", ControllerKind.None)
        };
        var state = new CombatState
        {
            Actors = actors.ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
            ActorOrder = ["beta", "beta"]
        };

        Assert.Equal(["alpha", "beta"], state.GetAllActors().Select(actor => actor.InstanceId));
    }

    private static CombatActorState Actor(string instanceId, string sideId, ControllerKind controller) => new()
    {
        InstanceId = instanceId,
        DefinitionId = "unit",
        ContentRevision = "revision",
        SideId = sideId,
        ControllerBinding = new ControllerBinding { Kind = controller }
    };
}
