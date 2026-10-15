using Core.CardZones;
using Core.Determinism;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneFlowExecutorTests
{
    [Theory]
    [InlineData(CardZoneFlowActorScope.Any, "hero", true)]
    [InlineData(CardZoneFlowActorScope.Any, "enemy", true)]
    [InlineData(CardZoneFlowActorScope.RunPlayer, "hero", true)]
    [InlineData(CardZoneFlowActorScope.RunPlayer, "enemy", false)]
    [InlineData(CardZoneFlowActorScope.RunPlayer, null, false)]
    [InlineData(CardZoneFlowActorScope.OtherActors, "hero", false)]
    [InlineData(CardZoneFlowActorScope.OtherActors, "enemy", true)]
    [InlineData(CardZoneFlowActorScope.Explicit, "enemy", true)]
    [InlineData(CardZoneFlowActorScope.Explicit, "hero", false)]
    public void ActorEligibility_IsPerFlowAndPreservesSkippedStateAndRandomCursor(
        CardZoneFlowActorScope scope, string? actor, bool eligible)
    {
        var definition = Definition();
        definition = definition with { Flows = definition.Flows.Select(flow =>
            flow.FlowId == "draw" ? flow with { ActorScope = scope, EligibleActorIds = ["enemy"] } : flow).ToArray() };
        var system = CardZoneSystemCompiler.Compile(definition).Value;
        var empty = CardZoneTransitions.CreateEmpty([Address("library"), Address("spent"), Address("ready")]).Value;
        var cards = CardZoneTransitions.CreateInstances(empty, Address("spent"), ["a", "b", "c"],
            new(), new(), null, CardZoneOrdering.Ordered, DeterministicContext.Create(44, "content")).Value;
        var context = new CardZoneFlowContext { Invocation = CardZoneFlowInvocation.Boundary,
            FlowOwnerId = "mage", RunOwnerId = "mage", ActiveActorId = actor, RunPlayerActorId = "hero" };
        var executor = new CardZoneFlowExecutor();
        var result = executor.ExecuteBoundary(system, cards.State, cards.Context, "activation.started", context).Value;
        var repeated = executor.ExecuteBoundary(system, cards.State, cards.Context, "activation.started", context).Value;
        Assert.Equal(result.Fingerprint, repeated.Fingerprint);
        Assert.Equal(eligible ? 2 : 0, result.State.GetZone(Address("ready"))!.InstanceIds.Count);
        Assert.Empty(cards.State.GetZone(Address("ready"))!.InstanceIds);
        if (!eligible)
        {
            Assert.Empty(result.Steps);
            Assert.Same(cards.State, result.State);
            Assert.Equal(cards.Context.RandomState.DrawCount, result.Context.RandomState.DrawCount);
        }
    }

    [Fact]
    public void ExplicitActorScope_RequiresActorIds()
    {
        var definition = Definition();
        Assert.True(CardZoneSystemCompiler.Compile(definition with { Flows = definition.Flows.Select(
            flow => flow with { ActorScope = CardZoneFlowActorScope.Explicit }).ToArray() }).IsFailure);
    }
    [Fact]
    public void Execute_TargetZoneCountMovesOnlyTheMissingAmount()
    {
        var definition = Definition() with
        {
            Flows =
            [
                new CardZoneFlowDefinition
                {
                    FlowId = "fill-ready",
                    AllowedInvocations = [CardZoneFlowInvocation.Boundary],
                    Steps =
                    [
                        new CardZoneFlowStepDefinition
                        {
                            StepId = "fill",
                            Operation = CardZoneOperation.Move,
                            SourceZoneId = "library",
                            TargetZoneId = "ready",
                            Selection = new()
                            {
                                Strategy = CardZoneSelectionStrategy.Top,
                                TargetZoneCount = 4
                            },
                            OnInsufficient = CardZoneInsufficientPolicy.AllowPartial
                        }
                    ]
                }
            ]
        };
        var system = CardZoneSystemCompiler.Compile(definition).Value;
        var topology = CardZoneTransitions.CreateEmpty(
            [Address("library"), Address("spent"), Address("ready")]).Value;
        var library = CardZoneTransitions.CreateInstances(topology, Address("library"), ["a", "b", "c"],
            new(), new(), null, CardZoneOrdering.Ordered, DeterministicContext.Create(1, "content")).Value;
        var ready = CardZoneTransitions.CreateInstances(library.State, Address("ready"), ["d", "e"],
            new(), new(), 10, CardZoneOrdering.Ordered, library.Context).Value;

        var result = new CardZoneFlowExecutor().Execute(system, ready.State, ready.Context,
            "fill-ready", new CardZoneFlowContext
            {
                Invocation = CardZoneFlowInvocation.Boundary,
                FlowOwnerId = "mage",
                RunOwnerId = "mage"
            });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(4, result.Value.State.GetZone(Address("ready"))!.InstanceIds.Count);
        Assert.Single(result.Value.State.GetZone(Address("library"))!.InstanceIds);
        Assert.Equal(2, Assert.Single(result.Value.Steps).InstanceIds.Length);
    }

    [Fact]
    public void Execute_FallbackRecycleAndRetryIsAtomicAndDeterministic()
    {
        var system = CardZoneSystemCompiler.Compile(Definition()).Value;
        var library = Address("library");
        var spent = Address("spent");
        var ready = Address("ready");
        var empty = CardZoneTransitions.CreateEmpty([library, spent, ready]).Value;
        var context = DeterministicContext.Create(44, "content");
        var cards = CardZoneTransitions.CreateInstances(empty, spent, ["a", "b", "c"], new(), new(), null,
            CardZoneOrdering.Ordered, context).Value;
        var executor = new CardZoneFlowExecutor();
        var flowContext = new CardZoneFlowContext
        {
            Invocation = CardZoneFlowInvocation.Boundary,
            FlowOwnerId = "mage",
            RunOwnerId = "mage"
        };

        var first = executor.Execute(system, cards.State, cards.Context, "draw", flowContext);
        var repeated = executor.Execute(system, cards.State, cards.Context, "draw", flowContext);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(2, first.Value.State.GetZone(ready)!.InstanceIds.Count);
        Assert.Empty(first.Value.State.GetZone(spent)!.InstanceIds);
        Assert.Single(first.Value.State.GetZone(library)!.InstanceIds);
        Assert.Equal(3, first.Value.Steps[0].InstanceIds.Length);
        Assert.Equal(3, first.Value.Steps[0].TargetOrderAfter.Length);
        Assert.True(first.Value.Steps[0].RandomDrawEnd > first.Value.Steps[0].RandomDrawStart);
        Assert.Equal(2, first.Value.Steps[^1].InstanceIds.Length);
        Assert.Equal(first.Value.Fingerprint, repeated.Value.Fingerprint);
        Assert.Equal(first.Value.Context, repeated.Value.Context);
        Assert.Empty(cards.State.GetZone(ready)!.InstanceIds);
    }

    [Fact]
    public void ExecuteBoundary_DestroysExpiredInstancesBeforeFlows()
    {
        var definition = Definition() with { Flows = [] };
        var system = CardZoneSystemCompiler.Compile(definition).Value;
        var zones = CardZoneTransitions.CreateEmpty([Address("library"), Address("spent"), Address("ready")]).Value;
        var created = CardZoneTransitions.CreateInstances(zones, Address("ready"), ["temporary"],
            new() { Strategy = CardInstanceLifetimeStrategy.UntilBoundary, Boundary = "encounter.ended" },
            new(), null, CardZoneOrdering.Ordered, DeterministicContext.Create(5, "content")).Value;

        var result = new CardZoneFlowExecutor().ExecuteBoundary(system, created.State, created.Context,
            "encounter.ended", new() { FlowOwnerId = "mage", RunOwnerId = "mage" });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Empty(result.Value.State.Instances);
        Assert.Single(result.Value.Steps);
        Assert.Equal("$lifetime", result.Value.Steps[0].FlowId);
    }

    private static CardZoneAddress Address(string zone) => new() { ZoneId = zone, OwnerId = "mage" };

    private static CardZoneSystemDefinition Definition() => new()
    {
        CardZoneSystemId = "cycle",
        Zones = [Zone("library"), Zone("spent"), Zone("ready", 10)],
        Flows =
        [
            new()
            {
                FlowId = "recycle",
                AllowedInvocations = [CardZoneFlowInvocation.Boundary],
                Steps =
                [
                    new()
                    {
                        StepId = "move-all",
                        Operation = CardZoneOperation.Move,
                        SourceZoneId = "spent",
                        TargetZoneId = "library",
                        Selection = new() { Strategy = CardZoneSelectionStrategy.All },
                        Insertion = new() { Strategy = CardZoneInsertionStrategy.ShuffleAfterInsert },
                        OnInsufficient = CardZoneInsufficientPolicy.AllowPartial
                    }
                ]
            },
            new()
            {
                FlowId = "draw",
                Triggers = ["activation.started"],
                AllowedInvocations = [CardZoneFlowInvocation.Boundary],
                Steps =
                [
                    new()
                    {
                        StepId = "take-two",
                        Operation = CardZoneOperation.Move,
                        SourceZoneId = "library",
                        TargetZoneId = "ready",
                        Selection = new() { Strategy = CardZoneSelectionStrategy.Top, Count = 2 },
                        OnInsufficient = CardZoneInsufficientPolicy.ExecuteFallbackAndRetry,
                        FallbackFlowId = "recycle",
                        RetryAfterFallback = true
                    }
                ]
            }
        ]
    };

    private static CardZoneDefinition Zone(string id, int? capacity = null) => new()
    {
        ZoneId = id,
        OwnerScope = CardZoneOwnerScope.Actor,
        Ordering = CardZoneOrdering.Ordered,
        Capacity = capacity
    };
}
