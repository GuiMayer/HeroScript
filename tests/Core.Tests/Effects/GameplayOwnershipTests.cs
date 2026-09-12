using Core.Combat;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class GameplayOwnershipTests
{
    [Fact]
    public void RelationsUseSidesNotHeroFlagAndSupportNeutralThirdSide()
    {
        var state = State() with
        {
            Relationships = new CombatRelationshipPolicy
            {
                Rules = [new() { FromSideId = "blue", ToSideId = "green", Relationship = SideRelationship.Neutral }]
            }
        };
        Assert.Equal(SideRelationship.Ally, state.Relationship(state.GetActor("hero")!, state.GetActor("ally")!));
        Assert.Equal(SideRelationship.Enemy, state.Relationship(state.GetActor("hero")!, state.GetActor("enemy")!));
        Assert.Equal(SideRelationship.Neutral, state.Relationship(state.GetActor("hero")!, state.GetActor("neutral")!));
    }

    [Fact]
    public void RunOwnershipDoesNotIncludeEnemyOrAllyAutomatically()
    {
        var state = State();
        var run = new RunState { RunId = Guid.Parse("11111111-1111-1111-1111-111111111111"), PlayerEntityId = "hero" };
        var owner = new GameplayOwner { Kind = GameplayOwnerKind.Run, Id = run.RunId.ToString() };
        Assert.True(owner.Includes(state.GetActor("hero")!, state, run));
        Assert.False(owner.Includes(state.GetActor("enemy")!, state, run));
        Assert.False(owner.Includes(state.GetActor("ally")!, state, run));
    }

    [Fact]
    public void TargetRequiresSelectionAndDoesNotSilentlyDiscardInvalidIds()
    {
        var executor = new EffectTriggerExecutor(Mock.Of<IRuntimeFormulaEvaluator>(), new ImmutableEffectProcessor(),
            allowUnconfiguredCalculations: true);
        var request = new EffectTriggerExecutionRequest
        {
            Combat = State(), OwnerEntityId = "hero", SourceEntityId = "hero",
            Trigger = new() { TriggerId = "test", Effects = [new() { Type = EffectType.HEAL, TargetResource = "focus", FlatValue = 1 }] }
        };
        Assert.True(executor.Execute(request).IsFailure);
        Assert.True(executor.Execute(request with { SelectedTargetEntityIds = ["hero", "missing"] }).IsFailure);
    }

    internal static CombatState State() => CombatTransitions.Create(
        [Entity("hero", "blue"), Entity("ally", "blue"), Entity("enemy", "red"), Entity("neutral", "green")],
        DeterministicContext.Create(123, "revision"));

    private static CombatActorState Entity(string id, string side) => new()
    {
        InstanceId = id,
        SideId = side,
        ControllerBinding = new ControllerBinding { Kind = id == "hero" ? ControllerKind.Player : ControllerKind.AI },
        ResourceState = new() { OwnerId = id, Resources = new Dictionary<string, ResourcePool>
        {
            ["focus"] = ResourcePool.Materialize(new ResourceDefinition { ResourceId = "focus", DisplayName = "Focus", DefaultMax = 20 }, 10, 20)
        } }
    };
}
