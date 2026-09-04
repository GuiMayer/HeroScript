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

    [Fact]
    public void CombatSystem_UpdateCombatState_StoresActivationState()
    {
        var system = CombatSystemTestFactory.Create();
        var start = system.StartCombat("hero", new List<string> { "enemy" });
        Assert.True(start.IsSuccess, start.IsFailure ? start.Error : null);

        var update = system.UpdateCombatState(start.Value.CombatId, state => state with
        {
            ActivationState = new ActivationState
            {
                ActiveActorId = "hero",
                ActivationOrder = new[] { "hero", "enemy" },
                WaitingForInput = true,
                RulesId = "default_activation"
            }
        });

        Assert.True(update.IsSuccess, update.IsFailure ? update.Error : null);
        Assert.Equal("hero", update.Value.ActivationState?.ActiveActorId);
        Assert.Equal(new[] { "hero", "enemy" }, update.Value.ActivationState?.ActivationOrder);

        var current = system.GetCombatState(start.Value.CombatId);
        Assert.Equal("hero", current.Value.ActivationState?.ActiveActorId);
    }

    private static CombatState CreateState()
    {
        return new CombatState
        {
            Hero = CreateEntity("hero"),
            Enemies = new[] { CreateEntity("enemy") }
        };
    }

    private static CombatEntity CreateEntity(string id)
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

        return new CombatEntity
        {
            EntityId = id,
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
