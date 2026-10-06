using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.StatusEffects;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class AccumulatedStackTransitionTests
{
    [Fact]
    public void CompleteConsumptionUsesBothAuthoritativeStoresAndProducesOnlyConsumeFacts()
    {
        var (combat, run) = State();
        var combatBefore = CanonicalJson.ComputeHash(combat);
        var runBefore = CanonicalJson.ComputeHash(run);
        var captured = Capture(combat, run);
        Assert.Equal(3, captured.Sources.Length);
        Assert.Equal(9, captured.Sources.Sum(source => source.Stacks));
        var result = AccumulatedStackTransitions.Consume(combat, run, captured);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.False(result.Value.Combat.StatusEffects.ContainsKey("hero"));
        Assert.Empty(result.Value.Run!.Modifiers);
        Assert.All(result.Value.Changes, change =>
        { Assert.Equal(EffectStackChangeReason.Consume, change.Reason); Assert.True(change.Removed); });
        Assert.Equal(new[] { 2, 4, 3 }, result.Value.Changes.Select(change => change.PreviousStacks));
        Assert.Equal(CanonicalJson.ComputeHash(combat.Determinism), CanonicalJson.ComputeHash(result.Value.Combat.Determinism));
        Assert.Equal(CanonicalJson.ComputeHash(run.Determinism), CanonicalJson.ComputeHash(result.Value.Run.Determinism));
        Assert.Equal(combatBefore, CanonicalJson.ComputeHash(combat));
        Assert.Equal(runBefore, CanonicalJson.ComputeHash(run));
    }

    [Fact]
    public void EligibilityIsExplicitNotInferredFromStacksOrPeriodicEffects()
    {
        var (combat, run) = State();
        var statuses = combat.StatusEffects["hero"].Select(status => status with
        { Definition = status.Definition with { Consumption = new(), Triggers = [new()
            { TriggerId = "tick", Boundary = "EndRound", Effects = [EffectTransactionTests.Resource(EffectType.DAMAGE, 1)] }] } }).ToImmutableArray();
        combat = combat with { StatusEffects = combat.StatusEffects.SetItem("hero", statuses) };
        Assert.Single(Capture(combat, run).Sources);
        Assert.Empty(AccumulatedStackTransitions.Capture(combat, run, "unauthorized", new()).Value.Sources);
        run = run with { Modifiers = run.Modifiers.Select(modifier => modifier with
            { Definition = modifier.Definition with { Consumption = new() } }).ToImmutableArray() };
        Assert.Empty(Capture(combat, run).Sources);
    }

    [Fact]
    public void SelectionFiltersByStoreOwnerOriginDefinitionAndTagsWithoutTruncating()
    {
        var (combat, run) = State();
        var selected = AccumulatedStackTransitions.Capture(combat, run, "proc", new()
        {
            Stores = [EffectStackStore.Status], Owner = Owner(), SourceEntityId = "caster",
            DefinitionIds = ["first"], RequiredTags = ["accumulator"], ExcludedTags = ["excluded"]
        });
        Assert.Equal("first", Assert.Single(selected.Value.Sources).DefinitionId);
        Assert.Empty(AccumulatedStackTransitions.Capture(combat, run, "proc", new() { RequiredTags = ["absent"] }).Value.Sources);
        Assert.True(AccumulatedStackTransitions.Capture(combat, run, "proc", new() { MaximumInstances = 2 }).IsFailure);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("duration")]
    [InlineData("revision")]
    [InlineData("definition")]
    [InlineData("inactive")]
    [InlineData("removed")]
    public void ChangedCaptureFailsAtomicallyIncludingNonCountChanges(string change)
    {
        var (combat, run) = State();
        var captured = Capture(combat, run);
        var modifier = run.Modifiers[0];
        run = run with { Modifiers = change == "removed" ? [] : [change switch
        {
            "count" => modifier with { Stacks = modifier.Stacks + 1 },
            "duration" => modifier with { Duration = 5 },
            "revision" => modifier with { ContentRevision = "other" },
            "definition" => modifier with { Definition = modifier.Definition with { MaxStacks = 100 } },
            "inactive" => modifier with { IsActive = false },
            _ => throw new InvalidOperationException()
        }] };
        var before = CanonicalJson.ComputeHash(new { combat, run });
        Assert.True(AccumulatedStackTransitions.Consume(combat, run, captured).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(new { combat, run }));
        Assert.Equal(2, combat.StatusEffects["hero"].Length);
    }

    [Fact]
    public void SameCapturedInstanceCannotBeConsumedTwiceOrDuplicatedInAPlan()
    {
        var (combat, run) = State();
        var plan = Capture(combat, run);
        Assert.True(AccumulatedStackTransitions.Consume(combat, run, plan with
        { Sources = plan.Sources.Add(plan.Sources[0]) }).IsFailure);
        var consumed = AccumulatedStackTransitions.Consume(combat, run, plan).Value;
        Assert.True(AccumulatedStackTransitions.Consume(consumed.Combat, consumed.Run, plan).IsFailure);
        Assert.Empty(Capture(consumed.Combat, consumed.Run!).Sources);
    }

    [Fact]
    public void BoundarySnapshotDoesNotTriggerOrExpireAnInstanceConsumedEarlierAtTheSameBoundary()
    {
        var (combat, run) = State();
        combat = combat with { StatusEffects = combat.StatusEffects.SetItem("hero", combat.StatusEffects["hero"]
            .Select(status => status with { Duration = 1, Definition = status.Definition with
            {
                DurationTickBoundary = StatusTriggerBoundary.EndRound,
                Triggers = [new() { TriggerId = status.StatusId, Boundary = "EndRound" }]
            } }).ToImmutableArray()) };
        var triggers = new Mock<IEffectTriggerExecutor>();
        triggers.Setup(executor => executor.Execute(It.IsAny<EffectTriggerExecutionRequest>()))
            .Returns((EffectTriggerExecutionRequest request) =>
            {
                Assert.Equal("first", request.Trigger.TriggerId);
                var plan = AccumulatedStackTransitions.Capture(request.Combat, request.Run, "proc", new()
                { DefinitionIds = ["second"] }).Value;
                var consumed = AccumulatedStackTransitions.Consume(request.Combat, request.Run, plan).Value;
                return Result<EffectBatchResult>.Success(new() { State = consumed.Combat, Run = consumed.Run });
            });
        var result = new CombatStatusLifecycle(triggers.Object).Process(run, combat, StatusTriggerBoundary.EndRound);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        triggers.Verify(executor => executor.Execute(It.IsAny<EffectTriggerExecutionRequest>()), Times.Once);
        Assert.DoesNotContain(result.Value.Events, item => item.StatusId == "second");
        Assert.Contains(result.Value.Events, item => item.StatusId == "first" && item.Kind == CombatStatusLifecycleEventKind.Expired);
        Assert.False(result.Value.Combat.StatusEffects.ContainsKey("hero"));
    }

    [Fact]
    public void PlanAndFactsPersistAndTenCapturesHaveIdenticalCanonicalOrder()
    {
        var (combat, run) = State();
        var plan = Capture(combat, run);
        var restored = JsonSerializer.Deserialize<StackConsumptionPlan>(JsonSerializer.Serialize(plan))!;
        Assert.Equal(CanonicalJson.ComputeHash(plan), CanonicalJson.ComputeHash(restored));
        var outputs = Enumerable.Range(0, 10).Select(index =>
        {
            var state = combat with { StatusEffects = combat.StatusEffects.SetItem("hero", index % 2 == 0
                ? combat.StatusEffects["hero"] : combat.StatusEffects["hero"].Reverse().ToImmutableArray()) };
            return AccumulatedStackTransitions.Consume(state, run, Capture(state, run)).Value;
        }).ToArray();
        Assert.Single(outputs.Select(result => CanonicalJson.ComputeHash(result)).Distinct());
        var persistedChanges = JsonSerializer.Deserialize<ImmutableArray<EffectStackChange>>(JsonSerializer.Serialize(outputs[0].Changes));
        Assert.Equal(CanonicalJson.ComputeHash(outputs[0].Changes), CanonicalJson.ComputeHash(persistedChanges));
    }

    [Fact]
    public void CapabilityAndMalformedSelectionsAreStrictlyValidated()
    {
        Assert.False(StackConsumptionPolicy.IsValid(new() { AllowedRecipeIds = ["proc", "proc"] }));
        Assert.False(StackConsumptionPolicy.IsValid(new() { AllowedRecipeIds = [""] }));
        Assert.False(StackConsumptionPolicy.IsValid(null));
        var (combat, run) = State();
        Assert.True(AccumulatedStackTransitions.Capture(combat, run, "", new()).IsFailure);
        Assert.True(AccumulatedStackTransitions.Capture(combat, run, "proc", new() { MaximumInstances = 0 }).IsFailure);
        Assert.True(AccumulatedStackTransitions.Capture(combat, run, "proc", new() { Stores = [(EffectStackStore)999] }).IsFailure);
        Assert.True(AccumulatedStackTransitions.Capture(combat, run, "proc", new() { Owner = new() { Kind = GameplayOwnerKind.Entity } }).IsFailure);
    }

    private static StackConsumptionPlan Capture(CombatState combat, RunState? run) =>
        AccumulatedStackTransitions.Capture(combat, run, "proc", new()).Value;

    private static GameplayOwner Owner() => new() { Kind = GameplayOwnerKind.Entity, Id = "hero" };

    private static (CombatState Combat, RunState Run) State()
    {
        var combat = GameplayOwnershipTests.State();
        var statuses = ImmutableArray.Create(Status("first", 2, "10000000-0000-0000-0000-000000000001"),
            Status("second", 4, "20000000-0000-0000-0000-000000000002"));
        combat = combat with { StatusEffects = combat.StatusEffects.SetItem("hero", statuses) };
        var run = new RunState
        {
            PlayerEntityId = "hero", Determinism = DeterministicContext.Create(1, "revision"), Modifiers = [new()
            {
                InstanceId = Guid.Parse("30000000-0000-0000-0000-000000000003"), ModifierId = "third", Owner = Owner(),
                SourceId = "caster", ContentRevision = "revision", Stacks = 3, Definition = new()
                { ModifierId = "third", Consumption = new() { AllowedRecipeIds = ["proc"] }, Tags = ["accumulator"] }
            }]
        };
        return (combat, run);
    }

    private static StatusEffectInstance Status(string id, int stacks, string instanceId) => new()
    {
        InstanceId = Guid.Parse(instanceId), StatusId = id, SourceId = "caster", TargetId = "hero",
        ContentRevision = "revision", Stacks = stacks, Duration = -1,
        Definition = new() { StatusId = id, Consumption = new() { AllowedRecipeIds = ["proc"] }, Tags = ["accumulator"] }
    };
}
