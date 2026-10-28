using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Resources;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResponseLifecycleTests
{
    [Fact]
    public void CombatBindingsCannotReadPersistentActorsOutsideTheEncounterRoster()
    {
        var context = GameplayResponseCompilationTests.Context();
        context = context with { Run = context.Run with { PlayerEntity = new() { InstanceId = "outside",
            Components = context.Combat!.GetActor("hero")!.Components } } };
        var registration = GameplayResponseCompilationTests.Registration() with { OwnerActorId = "outside" };
        Assert.False(context.ContainsActor("outside"));
        Assert.True(GameplayObserverCollector.Collect(context, [new GameplayResponseCompilationTests.FixedProvider(registration)]).IsFailure);
        Assert.True(GameplayResponseNumbers.Read(GameplayResponseMatcherTests.Fact(), registration, context,
            "outside", ["owner.resources.focus.current"]).IsFailure);
        var activity = context with { Combat = null };
        Assert.True(activity.ContainsActor("outside"));
        Assert.Equal(10, GameplayResponseNumbers.Read(GameplayResponseMatcherTests.Fact(), registration, activity,
            "outside", ["owner.resources.focus.current"]).Value["owner.resources.focus.current"]);
    }

    private static GameplayResponseDefinition Definition() => GameplayResponseCompilationTests.Definition() with
    { Effects = [new() { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 2 }] };

    private static GameplayEffectObservation Observation() => new()
    {
        Events = GameplayEventTransaction.Create(GameplayResponseMatcherTests.Fact().Scope, GameplayResponseCompilationTests.Catalog()).Value,
        PublisherId = GameplayCoreCatalog.PublisherId, ReasonId = "effect.application"
    };

    private static EffectTriggerExecutionRequest Request(params EffectDefinition[] effects) =>
        EffectTransactionTests.Request(effects) with { Run = GameplayResponseCompilationTests.Context().Run };

    private sealed class AppearsAfterDamage(GameplayObserverRegistration registration) : IGameplayObserverProvider
    {
        public string ProviderId => "test";
        public Result<ImmutableArray<GameplayObserverRegistration>> Collect(GameplayObserverCollectionContext context) =>
            Result<ImmutableArray<GameplayObserverRegistration>>.Success(context.Combat!.GetActor("enemy")!.GetResource("focus")!.Current < 10 ? [registration] : []);
    }

    [Theory]
    [InlineData(ResponseSnapshot.BeforeOccurrence, 1)]
    [InlineData(ResponseSnapshot.AfterOccurrence, 2)]
    public void NewlyAvailableObserversRespectExplicitSnapshotWithoutRetroactiveBeforeEligibility(ResponseSnapshot snapshot, int invocations)
    {
        var registration = GameplayResponseCompilationTests.Registration(definition: Definition() with { Availability = new() { Snapshot = snapshot } });
        var request = Request(EffectTransactionTests.Resource(EffectType.DAMAGE, 3), EffectTransactionTests.Resource(EffectType.DAMAGE, 3));
        var original = CanonicalJson.ComputeHash(request);
        var result = EffectTransactionTests.Executor().ExecuteResponsiveObserved(request, Observation(), GameplayResponseCompilationTests.Catalog(),
            [new AppearsAfterDamage(registration)]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(invocations, result.Value.Quotas.Count);
        Assert.Equal(10 + invocations * 2, result.Value.Execution.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Equal(original, CanonicalJson.ComputeHash(request));
    }

    [Theory]
    [InlineData(ResponseSnapshot.BeforeOccurrence, true, 0)]
    [InlineData(ResponseSnapshot.BeforeOccurrence, false, 1)]
    [InlineData(ResponseSnapshot.AfterOccurrence, true, 0)]
    public void DefeatUsesConfiguredResourceThresholdAndExplicitOwnerRevalidation(ResponseSnapshot snapshot, bool revalidate, int invocations)
    {
        var request = Request(EffectTransactionTests.Resource(EffectType.DAMAGE, 10)) with { SelectedTargetEntityIds = ["hero"] };
        var hero = request.Combat.GetActor("hero")!;
        var resource = hero.GetResource("focus")!;
        resource = resource with { Definition = resource.Definition! with { ThresholdPolicies = [new()
            { PolicyId = "defeat", Comparison = ResourceThresholdComparison.LessThanOrEqual,
                ThresholdSource = ResourceThresholdSource.Minimum, Consequence = ResourceThresholdConsequence.DefeatOwner }] } };
        request = request with { Combat = request.Combat.ReplaceActor(hero with { ResourceState = hero.ResourceState with
            { Resources = new Dictionary<string, ResourcePool>(hero.ResourceState.Resources) { ["focus"] = resource } } }) };
        var registration = GameplayResponseCompilationTests.Registration(definition: Definition() with
            { Availability = new() { Snapshot = snapshot, RevalidateOwner = revalidate } });
        var result = EffectTransactionTests.Executor().ExecuteResponsiveObserved(request, Observation(), GameplayResponseCompilationTests.Catalog(),
            [new GameplayResponseCompilationTests.FixedProvider(registration)]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(invocations, result.Value.Quotas.Count);
        Assert.Equal(invocations * 2, result.Value.Execution.State.GetActor("hero")!.GetResource("focus")!.Current);
    }

    [Fact]
    public void ResponseFactsCaptureContainerOriginAndDoNotLeakItIntoFollowingRootEffects()
    {
        var request = Request(EffectTransactionTests.Resource(EffectType.DAMAGE, 1), EffectTransactionTests.Resource(EffectType.DAMAGE, 1));
        var registration = GameplayResponseCompilationTests.Registration(definition: Definition());
        var result = EffectTransactionTests.Executor().ExecuteResponsiveObserved(request, Observation(), GameplayResponseCompilationTests.Catalog(),
            [new GameplayResponseCompilationTests.FixedProvider(registration)]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var facts = result.Value.Observation.Events.Occurrences;
        Assert.Equal(4, facts.Count);
        Assert.Equal("ability", facts[0].Origin!.ContainerKind); Assert.Equal("ability", facts[2].Origin!.ContainerKind);
        Assert.Equal(registration.Origin, facts[1].Origin); Assert.Equal(registration.Origin, facts[3].Origin);
        Assert.Equal(facts[0].EventId, facts[1].ParentEventId); Assert.Equal(facts[2].EventId, facts[3].ParentEventId);
        Assert.Null(result.Value.Observation.ResponseOrigin);
    }

    [Fact]
    public void ZeroAppliedChangeCanBeObservedWithoutInventingLostResource()
    {
        var definition = Definition() with { Observe = new() { Kinds = new() { Include = ["resource.change.resolved"] },
            Resource = new() { Direction = ResourceChangeDirection.Unchanged } } };
        var registration = GameplayResponseCompilationTests.Registration(definition: definition);
        var result = EffectTransactionTests.Executor().ExecuteResponsiveObserved(Request(EffectTransactionTests.Resource(EffectType.DAMAGE, 0)), Observation(),
            GameplayResponseCompilationTests.Catalog(), [new GameplayResponseCompilationTests.FixedProvider(registration)]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(0, ((ResourceChangePayload)result.Value.Observation.Events.Occurrences[0].Payload).AppliedDelta);
        Assert.Equal(1, result.Value.Quotas.Count);
        Assert.Equal(10, result.Value.Execution.State.GetActor("enemy")!.GetResource("focus")!.Current);
    }

    [Fact]
    public void CausalDepthFailureAndLateResponseFailureDiscardAllEarlierWork()
    {
        var request = Request(EffectTransactionTests.Resource(EffectType.DAMAGE, 3));
        var observation = Observation(); var original = CanonicalJson.ComputeHash(request);
        var cyclic = GameplayResponseCompilationTests.Registration(definition: Definition() with { Propagation = ResponsePropagation.AllowResponses });
        var executor = EffectTransactionTests.Executor();
        var cycle = executor.ExecuteResponsiveObserved(request, observation, GameplayResponseCompilationTests.Catalog(),
            [new GameplayResponseCompilationTests.FixedProvider(cyclic)]);
        Assert.True(cycle.IsFailure); Assert.Contains("limit", cycle.Error, StringComparison.OrdinalIgnoreCase);
        var first = GameplayResponseCompilationTests.Registration("first", Definition());
        var last = GameplayResponseCompilationTests.Registration("last", Definition() with { Order = new() { Priority = 1 },
            Effects = [Definition().Effects[0] with { TargetResource = "absent" }] });
        var late = executor.ExecuteResponsiveObserved(request, observation, GameplayResponseCompilationTests.Catalog(),
            [new GameplayResponseCompilationTests.FixedProvider(first, last)]);
        Assert.True(late.IsFailure); Assert.Empty(observation.Events.Occurrences);
        Assert.Equal(original, CanonicalJson.ComputeHash(request));
    }
}
