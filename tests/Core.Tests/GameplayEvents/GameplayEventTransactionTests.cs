using System.Text.Json;
using Core.Combat.Reactions;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.StatusEffects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayEventTransactionTests
{
    private static GameplayOccurrenceDraft Draft() => new()
    {
        Kind = GameplayOccurrenceTests.Kind.Kind, ReasonId = "cost.payment", SourceActorId = "actor",
        Attribution = GameplayAttribution.Source, Payload = GameplayOccurrenceTests.Occurrence().Payload
    };
    private static GameplayEventTransaction Transaction(GameplayEventLimits? limits = null) =>
        GameplayEventTransaction.Create(GameplayOccurrenceTests.Occurrence().Scope, GameplayOccurrenceTests.Catalog(), limits).Value;

    [Fact]
    public void RepeatedBranchingAppendHasDeterministicIdsAndDoesNotMutateItsPredecessor()
    {
        var original = Transaction();
        var a = original.Append("core.resource", "effect/0", Draft()).Value;
        var repeated = original.Append("core.resource", "effect/0", Draft()).Value;
        Assert.Equal(a.Occurrence.EventId, repeated.Occurrence.EventId);
        Assert.Equal(CanonicalJson.ComputeHash(a.Occurrence), CanonicalJson.ComputeHash(repeated.Occurrence));
        Assert.Empty(original.Occurrences);
        var child = a.Transaction.Append("core.resource", "response/0", Draft() with
        { SourceActorId = "reflector", Attribution = GameplayAttribution.InheritCause }).Value;
        Assert.Equal("actor", child.Occurrence.AttributedActorId);
        Assert.Equal("reflector", child.Occurrence.SourceActorId);
        Assert.Equal(a.Occurrence.EventId, child.Occurrence.ParentEventId);
        Assert.Equal(a.Occurrence.RootEventId, child.Occurrence.RootEventId);
        Assert.Equal(1, child.Occurrence.Ordinal);
        Assert.Single(a.Transaction.Occurrences);
        Assert.Equal(2, child.Transaction.Occurrences.Count);
    }

    [Fact]
    public void FailedAppendCannotConsumeOrdinalOrBudgetAndCannotForgeParentOrPublisher()
    {
        var original = Transaction(new() { MaximumDepth = 0, MaximumOccurrences = 1 });
        Assert.True(original.Append("untrusted", "effect/0", Draft()).IsFailure);
        Assert.True(original.Append("core.resource", "effect/0", Draft() with { Kind = null! }).IsFailure);
        Assert.True(original.Append("core.resource", "effect/0", Draft(), "invented-parent").IsFailure);
        var first = original.Append("core.resource", "effect/0", Draft()).Value;
        Assert.Equal(0, first.Occurrence.Ordinal);
        Assert.True(first.Transaction.Append("core.resource", "effect/1", Draft()).IsFailure);
        Assert.Single(first.Transaction.Occurrences);
        Assert.True(GameplayEventTransaction.Create(original.Scope, GameplayOccurrenceTests.Catalog(), new() { MaximumOccurrences = 4097 }).IsFailure);
        Assert.True(original.Append("core.resource", "effect/0", Draft() with { Attribution = GameplayAttribution.Unspecified }).IsFailure);
    }

    [Fact]
    public void HistoricalAttributionSurvivesSerializationWithoutCarryingOldExecutionDepth()
    {
        var first = Transaction().Append("core.resource", "status/application", Draft()).Value.Occurrence;
        var cause = JsonSerializer.Deserialize<GameplayCausalAnchor>(JsonSerializer.Serialize(GameplayCausalAnchor.From(first)))!;
        var nextScope = first.Scope with { ActionId = "next-action", CommandId = "next-command" };
        var next = GameplayEventTransaction.Create(nextScope, GameplayOccurrenceTests.Catalog()).Value;
        var tick = next.Append("core.resource", "status/tick", Draft() with
        { SourceActorId = null, OriginCause = cause, Attribution = GameplayAttribution.InheritCause }).Value.Occurrence;
        Assert.Equal(0, tick.Depth); // Chain budget is per action, not the age of a delayed status.
        Assert.NotEqual(first.RootEventId, tick.RootEventId);
        Assert.Equal(first.EventId, tick.OriginCause!.EventId);
        Assert.Equal("actor", tick.AttributedActorId);
        Assert.Null(tick.SourceActorId);
        Assert.Equal(cause, JsonSerializer.Deserialize<GameplayOccurrence>(JsonSerializer.Serialize(tick))!.OriginCause);
    }

    [Fact]
    public void BranchesHaveDistinctIdentityAndOnlyExplicitAncestorLineageCanBeReferenced()
    {
        var parent = Transaction().Append("core.resource", "effect/0", Draft()).Value.Occurrence;
        var scope = parent.Scope with { BranchId = "child" };
        var draft = Draft() with { OriginCause = GameplayCausalAnchor.From(parent), Attribution = GameplayAttribution.InheritCause };
        var restricted = GameplayEventTransaction.Create(scope, GameplayOccurrenceTests.Catalog()).Value;
        Assert.True(restricted.Append("core.resource", "effect/0", draft).IsFailure);
        var allowed = GameplayEventTransaction.Create(scope, GameplayOccurrenceTests.Catalog(), allowedAncestorBranches: [parent.Scope.BranchId]).Value;
        var child = allowed.Append("core.resource", "effect/0", draft).Value.Occurrence;
        Assert.NotEqual(parent.EventId, child.EventId);
        Assert.True(allowed.Append("core.resource", "effect/0", draft with
            { OriginCause = draft.OriginCause! with { RunId = "other" } }).IsFailure);
    }

    [Fact]
    public void DurableOwnersOmitAbsentCauseAndRoundTripIndependentCausalData()
    {
        var cause = GameplayCausalAnchor.From(Transaction().Append("core.resource", "effect/0", Draft()).Value.Occurrence);
        var status = new StatusEffectInstance { StatusId = "custom", Definition = new() { StatusId = "custom" } };
        var pending = new PendingActionState { PendingActionId = "pending" };
        var lot = new StackPayloadLot();
        Assert.DoesNotContain("OriginCause", CanonicalJson.Serialize(status));
        Assert.DoesNotContain("OriginCause", CanonicalJson.Serialize(pending));
        Assert.DoesNotContain("OriginCause", CanonicalJson.Serialize(lot));
        Assert.Equal(cause, JsonSerializer.Deserialize<StatusEffectInstance>(JsonSerializer.Serialize(status with { OriginCause = cause }))!.OriginCause);
        Assert.Equal(cause, JsonSerializer.Deserialize<PendingActionState>(JsonSerializer.Serialize(pending with { OriginCause = cause }))!.OriginCause);
        Assert.Equal(cause, JsonSerializer.Deserialize<StackPayloadLot>(JsonSerializer.Serialize(lot with { OriginCause = cause }))!.OriginCause);
        Assert.Null(status.OriginCause);
    }
}
