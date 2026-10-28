using System.Collections.Immutable;
using System.Text.Json;
using Core.CardZones;
using Core.GameplayEvents;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayCardFactProducerTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly GameplayZoneReference Source = new() { ZoneId = "arbitrary::zone", OwnerId = "owner",
        OwnerScope = CardZoneOwnerScope.Actor, ScopeId = "encounter" };
    private static readonly GameplayZoneReference Target = Source with { ZoneId = "another" };
    private static string Key(GameplayZoneReference reference) => CardZoneAddress.CreateKey(reference.ZoneId, reference.OwnerId, reference.ScopeId);
    private static CardZoneFlowStepRecord Step() => new()
    {
        FlowId = "flow", StepId = "move", Operation = CardZoneOperation.Move, SourceAddress = Key(Source), TargetAddress = Key(Target),
        PreviousStateHash = "before", StateHash = "after", InstanceIds = [A, B]
    };

    [Fact]
    public void MovementPreservesInstanceOrderAndDoesNotParsePurposeOrAddressDelimiters()
    {
        var facts = GameplayCardFactProducer.FromPrimitiveStep(Step(), Source, Target, new());
        Assert.True(facts.IsSuccess, facts.IsFailure ? facts.Error : null);
        Assert.Equal(new[] { A.ToString("D"), B.ToString("D") }, facts.Value.Select(item => ((CardMovementPayload)item.Payload).CardInstanceId));
        Assert.All(facts.Value, fact =>
        {
            Assert.Equal("card.moved", fact.Kind);
            Assert.Equal(Source, Assert.IsType<CardMovementPayload>(fact.Payload).From);
        });
        Assert.Equal(((CardMovementPayload)facts.Value[0].Payload).MovementGroupId, ((CardMovementPayload)facts.Value[1].Payload).MovementGroupId);
        Assert.True(GameplayCardFactProducer.FromPrimitiveStep(Step(), Source with { OwnerId = "other" }, Target, new()).IsFailure);
    }

    [Fact]
    public void SelectionOnlyAndAggregateFlowStepsDoNotDuplicatePrimitiveMovements()
    {
        Assert.Empty(GameplayCardFactProducer.FromPrimitiveStep(Step() with { StateHash = "before", RandomDrawEnd = 2 }, Source, Target, new()).Value);
        Assert.Empty(GameplayCardFactProducer.FromPrimitiveStep(Step() with { Operation = CardZoneOperation.ExecuteFlow }, Source, Target, new()).Value);
    }

    [Fact]
    public void ShuffleHasAnOrderFactInsteadOfPretendingEveryCardEnteredTheZoneAgain()
    {
        var step = Step() with { Operation = CardZoneOperation.Shuffle, TargetAddress = null,
            SourceOrderBefore = [A, B], SourceOrderAfter = [B, A] };
        var fact = Assert.Single(GameplayCardFactProducer.FromPrimitiveStep(step, Source, null, new()).Value);
        Assert.Equal("zone.order.changed", fact.Kind);
        Assert.Equal(B.ToString("D"), Assert.IsType<ZoneOrderPayload>(fact.Payload).After[0]);
        var encoded = JsonSerializer.Serialize<GameplayOccurrencePayload>(fact.Payload);
        Assert.IsType<ZoneOrderPayload>(JsonSerializer.Deserialize<GameplayOccurrencePayload>(encoded));
        Assert.True(GameplayCardFactProducer.FromPrimitiveStep(step with { SourceOrderAfter = [B, B] }, Source, null, new()).IsFailure);
    }

    [Fact]
    public void CreationAndDestructionHaveOnlyTheirActualEndpoint()
    {
        var created = Step() with { Operation = CardZoneOperation.Create, SourceAddress = null, CreatedInstanceIds = [A] };
        var creation = Assert.IsType<CardMovementPayload>(Assert.Single(GameplayCardFactProducer.FromPrimitiveStep(created, null, Target, new()).Value).Payload);
        Assert.Null(creation.From); Assert.Equal(Target, creation.To);
        var destroyed = Step() with { Operation = CardZoneOperation.Destroy, TargetAddress = null, DestroyedInstanceIds = [A] };
        var destruction = Assert.IsType<CardMovementPayload>(Assert.Single(GameplayCardFactProducer.FromPrimitiveStep(destroyed, Source, null, new()).Value).Payload);
        Assert.Equal(Source, destruction.From); Assert.Null(destruction.To);
    }
}
