using System.Text.Json;
using Core.Determinism;
using Core.Effects;
using Core.Entity;
using Core.GameplayEvents;
using Core.StatusEffects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayEntityFactProducerTests
{
    private static GameplayOccurrenceDraft Metadata() => new() { Attribution = GameplayAttribution.Source,
        SourceActorId = "actor-a", ReasonId = "rule.effect" };

    [Fact]
    public void AppliedAndExpiredStatusFactsUseCanonicalInstanceSnapshotsWithoutInventingBuffSemantics()
    {
        var state = Core.Tests.Effects.EffectTransactionTests.Request().Combat;
        var command = new ResolvedEffectCommand
        {
            EffectInstanceId = "apply", SourceEntityId = "hero", TargetEntityIds = ["enemy"],
            Definition = new() { Type = EffectType.APPLY_STATUS, StatusId = "custom", StatusStacks = 2, StatusDuration = 3 },
            StatusDefinition = new() { StatusId = "custom", DefaultDuration = 3, MaxStacks = 9 }
        };
        var hash = CanonicalJson.ComputeHash(state);
        var applied = new ImmutableEffectProcessor().Apply(state, [command]);
        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
        var change = Assert.Single(Assert.Single(applied.Value.Records).StackChanges);
        var status = Assert.Single(applied.Value.State.StatusEffects["enemy"]);
        var fact = GameplayEntityFactProducer.Status(change, null, status, Metadata());
        Assert.True(fact.IsSuccess, fact.IsFailure ? fact.Error : null);
        Assert.Equal("status.applied", fact.Value.Kind);
        Assert.Equal("enemy", fact.Value.SubjectActorId);
        Assert.Equal("actor-a", fact.Value.SourceActorId); // Caller binds immediate cause, not the historic applier.
        var payload = Assert.IsType<StatusChangePayload>(fact.Value.Payload);
        Assert.Null(payload.BeforeDuration);
        Assert.Equal(3, payload.AfterDuration);
        Assert.Equal(2, payload.AfterStacks);
        var expired = GameplayEntityFactProducer.Status(change with { PreviousStacks = 2, CurrentStacks = 0,
            Reason = EffectStackChangeReason.Expire }, status, null, Metadata());
        Assert.Equal("status.expired", expired.Value.Kind);
        Assert.Null(Assert.IsType<StatusChangePayload>(expired.Value.Payload).AfterDuration);
        Assert.Equal(hash, CanonicalJson.ComputeHash(state));
        Assert.True(GameplayEntityFactProducer.Status(change with { PreviousStacks = 9 }, null, status, Metadata()).IsFailure);
    }

    [Fact]
    public void DurationAndAttributeChangesPreserveTheirNatureAndDoNotMutateSnapshots()
    {
        var status = new StatusEffectInstance { InstanceId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            StatusId = "custom", TargetId = "actor", Stacks = 2, Duration = 3, Definition = new() { StatusId = "custom" } };
        var duration = GameplayEntityFactProducer.StatusDuration(status, status with { Duration = 2 }, Metadata());
        Assert.Equal("status.duration.changed", duration.Value.Kind);
        Assert.Equal(3, status.Duration);
        Assert.True(GameplayEntityFactProducer.StatusDuration(status, status, Metadata()).IsFailure);
        var attribute = GameplayEntityFactProducer.Attribute(new("component", "scaling", AttributeOperation.Multiply,
            AttributeLifetime.RunBase, 3, 6), Metadata());
        var payload = Assert.IsType<AttributeChangePayload>(attribute.Value.Payload);
        Assert.Equal(AttributeOperation.Multiply, payload.Operation);
        Assert.Equal(AttributeLifetime.RunBase, payload.Lifetime);
        Assert.Equal(6, payload.After);
        Assert.Equal(attribute.Value, JsonSerializer.Deserialize<GameplayOccurrenceDraft>(JsonSerializer.Serialize(attribute.Value)));
    }

    [Theory]
    [InlineData(GameplayBoundary.ActivationStarted, "activation.started")]
    [InlineData(GameplayBoundary.RoundEnded, "round.ended")]
    [InlineData(GameplayBoundary.CombatEnded, "combat.ended")]
    [InlineData(GameplayBoundary.PhaseEntered, "phase.entered")]
    public void BoundaryFactsUseTypedFrontiersWithoutExecutingTheLifecycle(GameplayBoundary boundary, string kind)
    {
        var fact = GameplayEntityFactProducer.Boundary(boundary, "boundary-id", 7, Metadata());
        Assert.Equal(kind, fact.Value.Kind);
        Assert.Equal(7, Assert.IsType<BoundaryOccurrencePayload>(fact.Value.Payload).Sequence);
        Assert.True(GameplayEntityFactProducer.Boundary(GameplayBoundary.Unspecified, "x", 0, Metadata()).IsFailure);
        Assert.True(GameplayEntityFactProducer.Boundary(boundary, "x", -1, Metadata()).IsFailure);
    }
}
