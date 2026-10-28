using System.Text.Json;
using Core.Common;
using Core.Config;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.Resources;
using Core.Run;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayRunObservationTests
{
    private static RunState State() => new()
    {
        RunId = Guid.Parse("10000000-0000-0000-0000-000000000001"), Sequence = 1, PlayerEntityId = "hero",
        Determinism = DeterministicContext.Create(42, "revision"),
        ResolvedMode = new() { CapabilityPolicy = new() { AllowRunResourceCheats = true } },
        ResourceState = new() { OwnerId = "wallet", Resources = new Dictionary<string, ResourcePool>
        { ["focus"] = ResourcePool.Materialize(new() { ResourceId = "focus", DisplayName = "Focus", DefaultCurrent = 10, DefaultMax = 20 }) } }
    };

    [Fact]
    public void WalletCostsAndGainsKeepExactOperandsOwnerAndCanonicalResults()
    {
        var state = State();
        var before = CanonicalJson.ComputeHash(state);
        var initial = GameplayObservedExecutorTests.Observation() with { CostReasonId = "cost.payment" };
        var cursor = new GameplayRunObservation(initial);
        var paid = RunResourceTransitions.Pay(state, [new() { ResourceId = "focus", Amount = 3 }], "pay", cursor);
        Assert.True(paid.IsSuccess, paid.IsFailure ? paid.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(RunResourceTransitions.Spend(state.ResourceState,
            [new() { ResourceId = "focus", Amount = 3 }], "pay").Value.State), CanonicalJson.ComputeHash(paid.Value.State));
        var payment = Assert.Single(cursor.Current.Events.Occurrences);
        Assert.Equal("cost.payment", payment.ReasonId);
        Assert.Null(payment.SubjectActorId);
        Assert.Equal("wallet", Assert.IsType<ResourceChangePayload>(payment.Payload).Owner!.Id);
        Assert.Equal(3, Assert.IsType<ResourceChangePayload>(payment.Payload).Operand);
        var rewarded = RunResourceTransitions.Reward(paid.Value.Run, [new() { ResourceId = "focus", Amount = 30 }], "gain", cursor);
        Assert.True(rewarded.IsSuccess, rewarded.IsFailure ? rewarded.Error : null);
        var gain = Assert.IsType<ResourceChangePayload>(cursor.Current.Events.Occurrences.Last().Payload);
        Assert.Equal(30, gain.Operand);
        Assert.Equal(20, gain.After);
        Assert.Empty(initial.Events.Occurrences);
        Assert.Equal(before, CanonicalJson.ComputeHash(state));
        var limited = new GameplayRunObservation(GameplayObservedExecutorTests.Observation(1) with { CostReasonId = "cost.payment" });
        Assert.True(RunResourceTransitions.Pay(state, [new() { ResourceId = "focus", Amount = 1 },
            new() { ResourceId = "focus", Amount = 1 }], "limited", limited).IsFailure);
        Assert.Empty(limited.Current.Events.Occurrences);
    }

    [Fact]
    public void ActivityWalletAdapterDoesNotMisidentifyAnActorAndRestoresBindings()
    {
        var state = State() with { ResolvedMode = null };
        var node = new RunMapNodeState { NodeId = "activity", EntryEffects = [new()
        { EffectId = "reward", Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF,
            TargetResource = "focus", FlatValue = 3, Operation = ResourceEffectOperation.ADD }] };
        var executor = new RunActivityEffectExecutor(EffectTransactionTests.Executor());
        var initial = GameplayObservedExecutorTests.Observation();
        var observed = executor.ExecuteObserved(state, node, RunActivityBoundary.Entry, initial);
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(executor.Execute(state, node, RunActivityBoundary.Entry).Value),
            CanonicalJson.ComputeHash(observed.Value.Execution));
        var fact = Assert.Single(observed.Value.Observation.Events.Occurrences);
        Assert.Equal("hero", fact.SourceActorId);
        Assert.Null(fact.SubjectActorId);
        var payload = Assert.IsType<ResourceChangePayload>(fact.Payload);
        Assert.Equal("run", payload.Owner!.Kind);
        Assert.Equal("wallet", payload.Owner.Id);
        Assert.Empty(observed.Value.Observation.ResourceOwnerBindings);
        Assert.Empty(initial.Events.Occurrences);
    }

    [Fact]
    public void RootRunCandidateUsesActualIdentityWithoutPublishingOrMutatingState()
    {
        var state = State();
        var manager = new RunManager(Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>());
        Assert.True(manager.HydrateForReplay(state).IsSuccess);
        var codec = new GameplayCommandCodec(GameplayCommandDescriptors.All);
        var handlers = RunCommandHandlers.Create(codec,
            () => new RunManager(Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>()), RunActivityRegistry.CreateDefault());
        var coordinator = new RunSessionCoordinator(manager, codec, handlers, _ => throw new Exception("Must not publish"));
        var command = new GameplayCommandEnvelope(new(Guid.Parse("20000000-0000-0000-0000-000000000001"),
            RunCommandTypes.ApplyRunResource, state.Sequence, state.Determinism.Step),
            JsonSerializer.SerializeToElement(new RunResourceCommand("focus", 2, ResourceEffectOperation.SUBTRACT)));
        var hashes = new HashSet<string>();
        for (var index = 0; index < 5; index++)
        {
            var observed = coordinator.PlanObserved(state.RunId, command);
            Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
            Assert.Equal(8, observed.Value.Execution.CandidateState.ResourceState.Get("focus")!.Current);
            Assert.Equal(new[] { "command.started", "resource.change.resolved", "command.completed" },
                observed.Value.Observation.Events.Occurrences.Select(item => item.Kind));
            Assert.Equal(command.Identity.CommandId.ToString("D"), observed.Value.Observation.Events.Scope.CommandId);
            hashes.Add(CanonicalJson.ComputeHash(observed.Value.Observation.Events.Occurrences));
        }
        Assert.Single(hashes);
        Assert.Equal(CanonicalJson.ComputeHash(state), CanonicalJson.ComputeHash(manager.GetRun(state.RunId).Value));
        Assert.Null(coordinator.FindReceipt(state.RunId, command.Identity.CommandId).Value);
        Assert.True(coordinator.PlanObserved(state.RunId, command with { Identity = command.Identity with { ExpectedSequence = 0 } }).IsFailure);
        Assert.True(coordinator.PlanObserved(state.RunId, command with { Payload = JsonSerializer.SerializeToElement(
            new RunResourceCommand("missing", 2, ResourceEffectOperation.ADD)) }).IsFailure);
    }
}
