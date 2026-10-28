using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.Resources;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResourceFactProducerTests
{
    private static ResourceSet State() => new()
    {
        OwnerId = "board-1", Resources = new Dictionary<string, ResourcePool>
        { ["custom-charge"] = ResourcePool.Materialize(new() { ResourceId = "custom-charge", DisplayName = "Custom charge", DefaultMax = 20 }, 10) }
    };
    private static readonly GameplayObjectReference Owner = new() { Kind = "board", Id = "board-1" };
    private static GameplayEventTransaction Transaction() =>
        GameplayEventTransaction.Create(GameplayOccurrenceTests.Occurrence().Scope, GameplayOccurrenceTests.Catalog()).Value;
    private static GameplayOccurrenceDraft Metadata(string reason = "cost.payment") => new()
        { ReasonId = reason, Attribution = GameplayAttribution.None };

    [Theory]
    [InlineData(ResourceMutationOperation.Add, 3, 13)]
    [InlineData(ResourceMutationOperation.Subtract, 12, 0)]
    [InlineData(ResourceMutationOperation.Set, 17, 17)]
    [InlineData(ResourceMutationOperation.Add, 0, 10)]
    public void ShadowFactsUseCanonicalResultsWithoutChangingResourceRules(ResourceMutationOperation operation, float amount, float expected)
    {
        var state = State();
        var before = CanonicalJson.ComputeHash(state);
        var transaction = Transaction();
        var mutations = new[] { new ResolvedResourceMutation { MutationId = "mutation", ResourceId = "custom-charge", Operation = operation, Value = amount } };
        var canonical = state.Apply(mutations).Value;
        var observed = new ObservedResourceTransitions().Apply(state, mutations, transaction, Owner, Metadata(), "core.resource", "mutation");
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        Assert.Equal(expected, observed.Value.Mutation.State.Get("custom-charge")!.Current);
        Assert.Equal(CanonicalJson.ComputeHash(canonical), CanonicalJson.ComputeHash(observed.Value.Mutation));
        var fact = Assert.Single(observed.Value.Events.Occurrences);
        var change = Assert.IsType<ResourceChangePayload>(fact.Payload);
        Assert.Equal(amount, change.Operand);
        Assert.Equal(expected - 10d, change.AppliedDelta);
        Assert.Equal(Owner, change.Owner);
        Assert.Null(fact.SubjectActorId); // A board resource is not silently attributed to an actor.
        Assert.Empty(transaction.Occurrences);
        Assert.Equal(before, CanonicalJson.ComputeHash(state));
    }

    [Fact]
    public void CostsAndDamageCanShareTheSameOperationWithoutSharingAReason()
    {
        var state = State();
        var costs = new[] { new ResolvedResourceCost { ResourceId = "custom-charge", Amount = 2 } };
        var observed = new ObservedResourceTransitions().Spend(state, costs, "payment", Transaction(), Owner,
            Metadata(), "core.resource", "cost");
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        var cost = Assert.Single(observed.Value.Events.Occurrences);
        Assert.Equal("cost.payment", cost.ReasonId);
        var mutation = new ResolvedResourceMutation { MutationId = "damage", ResourceId = "custom-charge",
            Value = 2, Operation = ResourceMutationOperation.Subtract };
        var damage = new ObservedResourceTransitions().Apply(state, [mutation], Transaction(), Owner,
            Metadata("combat.damage.direct"), "core.resource", "damage").Value;
        Assert.Equal(cost.Payload, damage.Events.Occurrences[0].Payload);
        Assert.NotEqual(cost.ReasonId, damage.Events.Occurrences[0].ReasonId);
    }

    [Fact]
    public void RejectedFactsAndFailedSpendingReturnNoPartialSuccessor()
    {
        var state = State();
        var hash = CanonicalJson.ComputeHash(state);
        var transaction = Transaction();
        var transitions = new ObservedResourceTransitions();
        Assert.True(transitions.Spend(state, [new() { ResourceId = "custom-charge", Amount = 200 }], "cost", transaction,
            Owner, Metadata(), "core.resource", "cost").IsFailure);
        Assert.True(transitions.Apply(state, [new() { MutationId = "x", ResourceId = "custom-charge", Value = 1 }], transaction,
            Owner, Metadata("unknown.reason"), "core.resource", "mutation").IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(state));
        Assert.Empty(transaction.Occurrences);
        Assert.True(transitions.Apply(state, [], transaction, Owner with { Id = "other" }, Metadata(), "core.resource", "mutation").IsFailure);
    }

    [Fact]
    public void EffectFactsKeepTheActualOperandInsteadOfGuessingItFromRoundedDelta()
    {
        var state = Core.Tests.Effects.EffectTransactionTests.Request().Combat;
        var actor = state.GetActor("enemy")!;
        var pool = actor.GetResource("focus")! with { Current = 1.1f };
        state = state.ReplaceActor(actor with { ResourceState = actor.ResourceState with
            { Resources = new Dictionary<string, ResourcePool>(actor.ResourceState.Resources) { ["focus"] = pool } } });
        var command = new ResolvedEffectCommand
        {
            EffectInstanceId = "effect-1", SourceEntityId = "hero", TargetEntityIds = ["enemy"], ResolvedValue = .1f,
            Definition = new() { Type = EffectType.MODIFY_RESOURCE, TargetResource = "focus", Operation = ResourceEffectOperation.ADD }
        };
        var applied = new ImmutableEffectProcessor().Apply(state, [command]).Value;
        var record = Assert.Single(applied.Records);
        var fact = GameplayResourceFactProducer.FromEffect(command, record, new() { Kind = "actor", Id = "enemy" }, Metadata());
        Assert.True(fact.IsSuccess, fact.IsFailure ? fact.Error : null);
        var payload = Assert.IsType<ResourceChangePayload>(fact.Value.Payload);
        Assert.Equal(.1f, payload.Operand);
        Assert.NotEqual((double)payload.Operand, payload.RequestedDelta);
        Assert.Equal("hero", fact.Value.SourceActorId);
        Assert.Equal("enemy", fact.Value.SubjectActorId);
        Assert.True(GameplayResourceFactProducer.FromEffect(command with { EffectInstanceId = "other" }, record,
            new() { Kind = "actor", Id = "enemy" }, Metadata()).IsFailure);
        Assert.True(GameplayResourceFactProducer.FromEffect(command with { TargetEntityIds = ["other"] }, record,
            new() { Kind = "actor", Id = "enemy" }, Metadata()).IsFailure);
        var environmental = command with { SourceEntityId = string.Empty };
        var environmentRecord = Assert.Single(new ImmutableEffectProcessor().Apply(state, [environmental]).Value.Records);
        Assert.Null(GameplayResourceFactProducer.FromEffect(environmental, environmentRecord,
            new() { Kind = "actor", Id = "enemy" }, Metadata()).Value.SourceActorId);
    }
}
