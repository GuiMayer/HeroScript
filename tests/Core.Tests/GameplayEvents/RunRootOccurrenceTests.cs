using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class RunRootOccurrenceTests
{
    private sealed record Pay(int Amount);
    private sealed class Handler(GameplayCommandDescriptor descriptor, IRunGameplayOccurrenceExecutor executor) : IRunCommandHandler<Pay>
    {
        public GameplayCommandDescriptor Descriptor => descriptor;
        public int Calls { get; private set; }
        public Result<RunTransitionPlan> Plan(RunState state, Pay payload, GameplayCommandExecutionContext context)
        {
            Calls++;
            Assert.NotNull(context.Observation);
            Assert.Equal(context.Identity.CommandId.ToString("D"), context.Observation.Current.Events.Scope.CommandId);
            context.Observation.Bind(executor);
            var paid = RunResourceTransitions.Pay(state, [new() { ResourceId = "focus", Amount = payload.Amount }], "payment", context.Observation);
            if (paid.IsFailure) return Result<RunTransitionPlan>.Failure(paid.Error);
            var candidate = paid.Value.Run with { Determinism = paid.Value.Run.Determinism.AdvanceStep() };
            return Result<RunTransitionPlan>.Success(new() { PreviousState = state, CandidateState = candidate, Context = candidate.Determinism,
                Observation = context.Observation.Current });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalRunCommandSharesObservedPlanningAndRetryNeverExecutesTheResponseAgain(bool invalid)
    {
        var context = GameplayResponseCompilationTests.Context(); var actor = context.Combat!.GetActor("hero")!;
        var response = GameplayResponseCompilationTests.Definition() with { Observe = new() { Kinds = new() {
            Include = ["resource.change.resolved"] }, Reasons = new() { Include = ["cost.payment"] } }, Bindings = new() {
            Target = new() { Kind = ResponseActorBindingKind.Owner } }, Effects = invalid
                ? [new() { Type = EffectType.REMOVE_STATUS, StatusId = "transient", Target = EffectTarget.SELF }]
                : [new() { Type = EffectType.HEAL, TargetResource = "focus", FlatValue = 2, Target = EffectTarget.SELF }] };
        var run = context.Run with { RunId = Guid.Parse("86ef4796-0178-45cd-b4e5-0278121ff2e5"), ResourceState = actor.ResourceState with { OwnerId = "wallet" }, PlayerEntity = new() {
            InstanceId = "hero", ContentRevision = "revision", Components = actor.Components.ToImmutableDictionary(StringComparer.Ordinal)
                .Add("watch", new EntityResponseComponentState { ComponentId = "watch", Responses = [response] }) } };
        run = run with { Sequence = 1, Lineage = Core.Run.Branching.RunLineage.Root(run.RunId) };
        var original = run; var input = CanonicalJson.ComputeHash(run); var publications = 0;
        var queries = new Mock<IRunQueryService>();
        queries.Setup(item => item.GetRun(run.RunId)).Returns(() => Result<RunState>.Success(run));
        var executor = new RunGameplayOccurrenceExecutor(EffectTransactionTests.Executor());
        var descriptor = new GameplayCommandDescriptor("TEST_PAYMENT", typeof(Pay), GameplayCommandRoute.Run);
        var handler = new Handler(descriptor, executor);
        var durable = new Mock<Core.Abstractions.Persistence.IRunCommitStore>();
        Core.Abstractions.Persistence.RunCommit? committed = null;
        var rejectAppend = !invalid;
        durable.Setup(item => item.AppendAsync(It.IsAny<Core.Abstractions.Persistence.RunCommit>(), It.IsAny<CancellationToken>()))
            .Returns((Core.Abstractions.Persistence.RunCommit value, CancellationToken _) =>
            {
                if (rejectAppend) throw new IOException("append unavailable");
                committed = value;
                return Task.FromResult(new Core.Abstractions.Persistence.RunCommitAppendResult(value, false));
            });
        var coordinator = new RunSessionCoordinator(queries.Object, new GameplayCommandCodec([descriptor]), new([handler]),
            state => { publications++; run = state; }, store: durable.Object, occurrences: executor);
        var command = new GameplayCommandEnvelope(new(Guid.Parse("77f58528-884b-432c-b719-d3c755a26775"), descriptor.Type,
            run.Sequence, run.Determinism.Step), JsonSerializer.SerializeToElement(new Pay(3)));
        var planned = coordinator.PlanObserved(run.RunId, command);
        Assert.Equal(0, publications); Assert.Equal(input, CanonicalJson.ComputeHash(original));
        if (!invalid)
        {
            var failed = coordinator.Execute(run.RunId, command);
            Assert.True(failed.IsFailure); Assert.Null(committed); Assert.Equal(0, publications);
            Assert.Equal(input, CanonicalJson.ComputeHash(run));
            rejectAppend = false;
        }
        var executed = coordinator.Execute(run.RunId, command);
        if (invalid)
        {
            Assert.True(planned.IsFailure); Assert.True(executed.IsFailure);
            Assert.Equal(0, publications); Assert.Equal(input, CanonicalJson.ComputeHash(run));
            return;
        }
        Assert.True(planned.IsSuccess, planned.IsFailure ? planned.Error : null);
        Assert.True(executed.IsSuccess, executed.IsFailure ? executed.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(planned.Value.Execution.CandidateState with { Sequence = run.Sequence }), CanonicalJson.ComputeHash(run));
        Assert.Equal(7, run.ResourceState.Get("focus")!.Current);
        Assert.Equal(12, run.PlayerEntity!.Component<ResourceEntityComponentState>()!.State.Get("focus")!.Current);
        Assert.Equal(1, publications); Assert.Equal(3, handler.Calls);
        Assert.NotNull(committed!.GameplayEvents);
        var batch = committed.GameplayEvents;
        Assert.True(batch.Validate(run.RunId, command.Identity.CommandId).IsSuccess);
        Assert.Single(batch.Invocations);
        Assert.Equal(CanonicalJson.ComputeHash(batch), CanonicalJson.ComputeHash(executed.Value.JournalEntry!.GameplayEvents));
        Assert.Equal(CanonicalJson.ComputeHash(GameplayEventBatch.Capture(planned.Value.Observation)), CanonicalJson.ComputeHash(batch));
        var retry = coordinator.Execute(run.RunId, command);
        Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error : null);
        Assert.True(retry.Value.Duplicate); Assert.Equal(3, handler.Calls); Assert.Equal(1, publications);
    }
}
