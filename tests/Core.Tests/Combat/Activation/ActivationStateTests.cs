using Core.Combat;
using Core.Combat.Activation;
using Core.Combat.Models;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat.Activation;

public sealed class ActivationStateTests
{
    [Fact]
    public void ActivationState_CopiesCollectionsAndUsesLogicalTimeSentinel()
    {
        var order = new List<string> { "hero", "enemy" };
        var activation = new ActivationState { ActivationOrder = order };

        order.Clear();

        Assert.Equal(new[] { "hero", "enemy" }, activation.ActivationOrder);
        Assert.Equal(DateTime.UnixEpoch, activation.StartedAtUtc);
    }

    [Fact]
    public void CombatState_CanStoreActivationState()
    {
        var state = CreateState() with
        {
            ActivationState = new ActivationState
            {
                ActiveActorId = "hero",
                Round = 2,
                ActivationIndex = 1,
                ActivationNumber = 3,
                ActivationOrder = new[] { "enemy", "hero" },
                CompletedActorIds = new[] { "enemy" },
                WaitingForInput = true,
                RulesId = "default_activation",
                RunId = Guid.NewGuid()
            }
        };

        Assert.NotNull(state.ActivationState);
        Assert.Equal("hero", state.ActivationState.ActiveActorId);
        Assert.Equal(2, state.ActivationState.Round);
        Assert.True(state.ActivationState.WaitingForInput);
    }

    private static CombatState CreateState()
    {
        return new CombatState
        {
            Actors = new[] { CreateEntity("hero"), CreateEntity("enemy") }
                .ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal)
        };
    }

    private static CombatActorState CreateEntity(string id)
    {
        var healthDefinition = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = 10
        };

        return new CombatActorState
        {
            InstanceId = id,
            Name = id,
            ResourceState = new ResourceSet
            {
                OwnerId = id,
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = new ResourcePool
                    {
                        ResourceId = "health",
                        Current = 10,
                        Maximum = 10,
                        Minimum = 0,
                        Definition = healthDefinition
                    }
                }
            }
        };
    }
}
