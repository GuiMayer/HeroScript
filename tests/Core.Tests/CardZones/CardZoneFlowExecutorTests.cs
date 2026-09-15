using Core.CardZones;
using Core.Determinism;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneFlowExecutorTests
{
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
