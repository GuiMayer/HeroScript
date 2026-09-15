using Core.CardZones;
using Core.Determinism;
using Core.Run;
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

    [Fact]
    public void CreationOrderInsertion_ReconstructsStableCollectionOrderWithoutASecondRegistry()
    {
        var empty = CardZoneTransitions.CreateEmpty([Library, Ready]).Value;
        var created = CardZoneTransitions.CreateInstances(empty, Library, ["one", "two", "three"],
            new(), new(), null, CardZoneOrdering.Ordered,
            DeterministicContext.Create(12, "content")).Value;
        var collection = created.State.CollectionInstanceIds.ToArray();
        var middleAndLast = CardZoneTransitions.Move(created.State, Library, Ready,
            collection.Skip(1).ToArray(), new(), null,
            CardZoneOrdering.Ordered, CardZoneOrdering.Ordered, created.Context).Value;
        var completed = CardZoneTransitions.Move(middleAndLast.State, Library, Ready,
            [collection[0]], new() { Strategy = CardZoneInsertionStrategy.CreationOrder }, null,
            CardZoneOrdering.Ordered, CardZoneOrdering.Ordered, middleAndLast.Context).Value;

        Assert.Equal(collection, completed.State.GetZone(Ready)!.InstanceIds);
        Assert.Equal(collection, completed.State.CollectionInstanceIds);
        Assert.Equal(3, completed.State.Instances.Count);
    }

    [Fact]
    public void ApplyUpgrade_ChangesOnlyTheSelectedImmutableInstance()
    {
        var empty = CardZoneTransitions.CreateEmpty([Library, Ready]).Value;
        var created = CardZoneTransitions.CreateInstances(empty, Library, ["spark", "ward"],
            new(), new(), null, CardZoneOrdering.Ordered,
            DeterministicContext.Create(12, "content")).Value;
        var id = created.State.CollectionInstanceIds[0];
        var definition = new CardUpgradeDefinition
        {
            UpgradeId = "spark-plus", CardDefinitionIds = ["spark"]
        };

        var upgraded = CardZoneTransitions.ApplyUpgrade(created.State, id, definition, created.Context);
        var repeated = CardZoneTransitions.ApplyUpgrade(upgraded.Value.State, id, definition, upgraded.Value.Context);

        Assert.True(upgraded.IsSuccess, upgraded.IsFailure ? upgraded.Error : null);
        Assert.Empty(created.State.GetCard(id)!.Upgrades);
        Assert.Equal("spark-plus", Assert.Single(upgraded.Value.State.GetCard(id)!.Upgrades).UpgradeId);
        Assert.Empty(upgraded.Value.State.GetCard(created.State.CollectionInstanceIds[1])!.Upgrades);
        Assert.Equal(created.State.CollectionInstanceIds, upgraded.Value.State.CollectionInstanceIds);
        Assert.True(repeated.IsFailure);
        Assert.Contains("application limit", repeated.Error);
    }
}
