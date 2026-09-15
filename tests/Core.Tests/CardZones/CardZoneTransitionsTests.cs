using Core.CardZones;
using Core.Determinism;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneTransitionsTests
{
    private static readonly CardZoneAddress Library = new() { ZoneId = "library", OwnerId = "mage" };
    private static readonly CardZoneAddress Ready = new() { ZoneId = "ready", OwnerId = "mage" };

    [Fact]
    public void CreateMoveShuffleAndDestroy_AreDeterministicAndImmutable()
    {
        var empty = CardZoneTransitions.CreateEmpty([Library, Ready]).Value;
        var context = DeterministicContext.Create(42, "content");
        var created = CardZoneTransitions.CreateInstances(empty, Library, ["spark", "ward", "spark"],
            new(), new(), null, CardZoneOrdering.Ordered, context).Value;
        var selected = CardZoneTransitions.Select(created.State, Library,
            new() { Strategy = CardZoneSelectionStrategy.Random, Count = 2 }, created.Context).Value;
        var repeated = CardZoneTransitions.Select(created.State, Library,
            new() { Strategy = CardZoneSelectionStrategy.Random, Count = 2 }, created.Context).Value;
        var moved = CardZoneTransitions.Move(created.State, Library, Ready, selected.InstanceIds,
            new(), 3, CardZoneOrdering.Ordered, CardZoneOrdering.Ordered, selected.Context).Value;
        var shuffled = CardZoneTransitions.Shuffle(moved.State, Ready, moved.Context).Value;
        var destroyed = CardZoneTransitions.Destroy(shuffled.State, [selected.InstanceIds[0]],
            CardZoneOrdering.Ordered, shuffled.Context).Value;

        Assert.True(selected.InstanceIds.SequenceEqual(repeated.InstanceIds));
        Assert.Equal(selected.Context, repeated.Context);
        Assert.Empty(empty.Instances);
        Assert.Equal(3, created.State.Instances.Count);
        Assert.Equal(2, moved.State.GetZone(Ready)!.InstanceIds.Count);
        Assert.Equal(2, moved.State.Instances.Count(card => card.Value.DefinitionId == "spark"));
        Assert.Equal(2, destroyed.State.Instances.Count);
        Assert.Null(destroyed.State.GetCard(selected.InstanceIds[0]));
        Assert.True(CardZoneTopologyValidator.Validate(destroyed.State).IsSuccess);
    }

    [Fact]
    public void Move_RejectsOverflowWithoutChangingInput()
    {
        var empty = CardZoneTransitions.CreateEmpty([Library, Ready]).Value;
        var created = CardZoneTransitions.CreateInstances(empty, Library, ["a", "b"],
            new(), new(), null, CardZoneOrdering.Ordered,
            DeterministicContext.Create(7, "content")).Value;

        var result = CardZoneTransitions.Move(created.State, Library, Ready,
            created.State.GetZone(Library)!.InstanceIds, new(), 1,
            CardZoneOrdering.Ordered, CardZoneOrdering.Ordered, created.Context);

        Assert.True(result.IsFailure);
        Assert.Equal(2, created.State.GetZone(Library)!.InstanceIds.Count);
        Assert.Empty(created.State.GetZone(Ready)!.InstanceIds);
    }
}
