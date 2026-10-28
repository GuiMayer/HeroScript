using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.Resources;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayObservedExecutorTests
{
    private static GameplayEffectObservation Observation(int maximum = 4096)
    {
        var publisher = new GameplayPublisherContract("core.effects", HierarchySelector.Compile(["**"]).Value,
            Enum.GetValues<GameplayPayloadKind>().ToImmutableArray());
        var kinds = new[] { ("resource.change.resolved", GameplayPayloadKind.ResourceChange),
            ("attribute.change.resolved", GameplayPayloadKind.AttributeChange), ("status.applied", GameplayPayloadKind.StatusChange),
            ("status.reapplied", GameplayPayloadKind.StatusChange), ("status.removed", GameplayPayloadKind.StatusChange),
            ("status.stacks.consumed", GameplayPayloadKind.StatusChange),
            ("resource.threshold.matched", GameplayPayloadKind.ResourceThreshold), ("actor.defeat.changed", GameplayPayloadKind.ActorDefeatChange),
            ("modifier.stacks.changed", GameplayPayloadKind.ModifierChange), ("activation.sequence.changed", GameplayPayloadKind.SequenceChange),
            ("card.moved", GameplayPayloadKind.CardMovement), ("card.created", GameplayPayloadKind.CardMovement),
            ("card.destroyed", GameplayPayloadKind.CardMovement), ("zone.order.changed", GameplayPayloadKind.ZoneOrder) }
            .Select(item => new GameplayEventKindDefinition { Kind = item.Item1, Payload = item.Item2, PublisherId = publisher.PublisherId });
        var catalog = GameplayEventCatalog.Create("setting", "revision", [publisher], kinds,
            [new() { ReasonId = "effect.application" }]).Value;
        return new() { PublisherId = publisher.PublisherId, ReasonId = "effect.application",
            Events = GameplayEventTransaction.Create(GameplayOccurrenceTests.Occurrence().Scope, catalog,
                new() { MaximumOccurrences = maximum }).Value };
    }

    [Fact]
    public void ShadowExecutionUsesCanonicalExecutorAndPreservesEverySerializedResultAndRng()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var request = EffectTransactionTests.Request(EffectSequenceBudgetTests.Damage(6, 3)) with { Run = fixture.Run };
        var observation = Observation();
        var inputHash = CanonicalJson.ComputeHash(request);
        var expected = fixture.Executor.Execute(request).Value;
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        for (var repeat = 0; repeat < 10; repeat++)
        {
            var actual = fixture.Executor.ExecuteObserved(request, observation);
            Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(expected), CanonicalJson.ComputeHash(actual.Value.Execution));
            Assert.Equal(expected.State.Determinism, actual.Value.Execution.State.Determinism);
            Assert.Empty(actual.Value.Observation.Unobserved);
            Assert.Equal(3, actual.Value.Observation.Events.Occurrences.Count);
            Assert.All(actual.Value.Observation.Events.Occurrences, item => Assert.IsType<ResourceChangePayload>(item.Payload));
            fingerprints.Add(CanonicalJson.ComputeHash(actual.Value.Observation.Events.Occurrences));
        }
        Assert.Single(fingerprints);
        Assert.Empty(observation.Events.Occurrences);
        Assert.Equal(inputHash, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void FactsObserveLegacyResponseOrderAndKeepTheirImmediateCause()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var gain = EffectSequenceBudgetTests.Damage(1, 1) with { Type = EffectType.MODIFY_RESOURCE,
            Target = EffectTarget.SELF, Operation = ResourceEffectOperation.ADD };
        var run = fixture.Run;
        foreach (var id in new[] { "observer-a", "observer-b" })
        run = RelicTransitions.Acquire(run, new() { RelicId = id, Reactions = [new()
        {
            ReactionId = "gain", OwnerScope = EffectReactionOwnerScope.Source, ResourceId = "focus",
            ResourceField = ResourceValueField.Current, Operation = ResourceMutationOperation.Subtract,
            Channels = ["magnitude"], Effects = [gain]
        }] }, new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }).Value.State;
        var damage = EffectSequenceBudgetTests.Damage(1, 1);
        var request = EffectTransactionTests.Request(damage with { ChainedEffects = [damage] }) with { Run = run };
        var expected = fixture.Executor.Execute(request).Value;
        var actual = fixture.Executor.ExecuteObserved(request, Observation());
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(expected), CanonicalJson.ComputeHash(actual.Value.Execution));
        var facts = actual.Value.Observation.Events.Occurrences.ToArray();
        Assert.Equal(new[] { "enemy", "hero", "hero", "enemy", "hero", "hero" }, facts.Select(item => item.SubjectActorId));
        Assert.Equal(facts[0].EventId, facts[1].ParentEventId);
        Assert.Equal(facts[0].EventId, facts[2].ParentEventId);
        Assert.Equal(facts[3].EventId, facts[4].ParentEventId);
        Assert.Equal(facts[3].EventId, facts[5].ParentEventId);
        Assert.Equal("relic", facts[1].Origin!.ContainerKind);
        Assert.Empty(actual.Value.Observation.Unobserved);
    }

    [Fact]
    public void StatusApplicationAndRemovalUseActualSnapshotsAndBudgetFailureReturnsNoSuccessor()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var observation = Observation();
        var prefix = new ResolvedEffectCommand { EffectInstanceId = "prefix", SourceEntityId = "hero", TargetEntityIds = ["enemy"],
            ContentRevision = "revision", Definition = new() { Type = EffectType.APPLY_STATUS, StatusId = "custom",
                StatusStacks = 2, StatusDuration = 3 }, StatusDefinition = new() { StatusId = "custom", DefaultDuration = 3, MaxStacks = 9 } };
        var request = EffectTransactionTests.Request(EffectSequenceBudgetTests.Damage(3, 3)) with
        { Run = fixture.Run, PrefixCommands = [prefix, prefix with { EffectInstanceId = "remove", Definition = new()
            { Type = EffectType.REMOVE_STATUS, StatusId = "custom" }, StatusDefinition = null }] };
        var result = fixture.Executor.ExecuteObserved(request, observation);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("status.applied", result.Value.Observation.Events.Occurrences[0].Kind);
        Assert.Equal(3, Assert.IsType<StatusChangePayload>(result.Value.Observation.Events.Occurrences[0].Payload).AfterDuration);
        Assert.Equal("status.removed", result.Value.Observation.Events.Occurrences[1].Kind);
        Assert.Null(Assert.IsType<StatusChangePayload>(result.Value.Observation.Events.Occurrences[1].Payload).AfterDuration);
        Assert.Equal(CanonicalJson.ComputeHash(fixture.Executor.Execute(request).Value), CanonicalJson.ComputeHash(result.Value.Execution));
        var constrained = Observation(1);
        Assert.True(fixture.Executor.ExecuteObserved(request, constrained).IsFailure);
        Assert.Empty(constrained.Events.Occurrences);
        Assert.Empty(request.Combat.StatusEffects);
    }
    [Fact]
    public void PartialCondensationCoverageIsExplicitAndDoesNotInventAnOrdinaryEffectFact()
    {
        var fixture = StackPayloadTests.Fixture(recipe: CondensationTests.Recipe(CondensationTests.CountResource()));
        var prepared = fixture.Executor.Execute(EffectTransactionTests.Request(StackPayloadTests.Apply(2)) with { Run = fixture.Run });
        Assert.True(prepared.IsSuccess, prepared.IsFailure ? prepared.Error : null);
        var request = EffectTransactionTests.Request(CondensationTests.Condense()) with
        { Run = prepared.Value.Run, Combat = prepared.Value.State };
        var observation = Observation();
        var actual = fixture.Executor.ExecuteObserved(request, observation);
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(fixture.Executor.Execute(request).Value), CanonicalJson.ComputeHash(actual.Value.Execution));
        Assert.Equal(EffectType.CONDENSE_STACKS, Assert.Single(actual.Value.Observation.Unobserved).EffectType);
        Assert.Contains(actual.Value.Observation.Events.Occurrences, item => item.Kind == "status.stacks.consumed");
        Assert.Empty(observation.Events.Occurrences);
        Assert.True(fixture.Executor.ExecuteObserved(request with { ContentRevision = "another" }, observation).IsFailure);
    }

    [Theory]
    [InlineData(GameplayOwnerKind.Entity, "enemy")]
    [InlineData(GameplayOwnerKind.Global, "")]
    public void ModifierFactsKeepTheRealOwnerAndCanonicalStacks(GameplayOwnerKind kind, string id)
    {
        var fixture = StackPayloadTests.Fixture();
        var apply = StackPayloadTests.ApplyModifier(2) with { ModifierOwner = new() { Kind = kind, Id = id } };
        var request = EffectTransactionTests.Request(apply) with { Run = fixture.Run };
        var actual = fixture.Executor.ExecuteObserved(request, Observation());
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(fixture.Executor.Execute(request).Value), CanonicalJson.ComputeHash(actual.Value.Execution));
        var fact = Assert.Single(actual.Value.Observation.Events.Occurrences);
        var payload = Assert.IsType<ModifierChangePayload>(fact.Payload);
        Assert.Equal(new GameplayOwner { Kind = kind, Id = id }, payload.Owner);
        Assert.Equal(2, payload.AfterStacks);
        Assert.Empty(actual.Value.Observation.Unobserved);
        Assert.Empty(fixture.Run.Modifiers);
    }

    [Fact]
    public void ConfiguredCardFlowEmitsPrimitiveMovementsOnceWithoutGuessingZonePurpose()
    {
        var fixture = StackPayloadTests.Fixture();
        var effect = new EffectDefinition { Type = EffectType.CARD_ZONE_FLOW, CardZoneFlowId = "draw", CardCount = 2 };
        var request = EffectTransactionTests.Request(effect) with { Run = fixture.Run };
        var actual = fixture.Executor.ExecuteObserved(request, Observation());
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(fixture.Executor.Execute(request).Value), CanonicalJson.ComputeHash(actual.Value.Execution));
        var facts = actual.Value.Observation.Events.Occurrences;
        Assert.Equal(2, facts.Count);
        Assert.All(facts, item => { var movement = Assert.IsType<CardMovementPayload>(item.Payload);
            Assert.Equal("reserve", movement.From!.ZoneId); Assert.Equal("active", movement.To!.ZoneId); });
        Assert.Equal(2, facts.Select(item => ((CardMovementPayload)item.Payload).CardInstanceId).Distinct().Count());
        Assert.Empty(actual.Value.Observation.Unobserved);
    }

    [Fact]
    public void SequenceFactsDescribeResolvedMutationWithoutAdvancingTheTurn()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var effect = new EffectDefinition { Type = EffectType.TURN_SEQUENCE, Target = EffectTarget.SELF, FlatValue = 0,
            SequenceMutation = new() { Operation = Core.Combat.Flow.SequenceOperation.ReverseDirection } };
        var request = EffectTransactionTests.Request(effect) with { Run = fixture.Run };
        request = request with { Combat = request.Combat with { ActivationState = new() { Circular = new()
            { Direction = Core.Combat.Flow.SequenceDirection.Forward, PendingAdvance = 1, RoundLength = 2 } } } };
        var actual = fixture.Executor.ExecuteObserved(request, Observation());
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(fixture.Executor.Execute(request).Value), CanonicalJson.ComputeHash(actual.Value.Execution));
        var payload = Assert.IsType<SequenceChangePayload>(Assert.Single(actual.Value.Observation.Events.Occurrences).Payload);
        Assert.Equal(Core.Combat.Flow.SequenceDirection.Reverse, payload.After.Direction);
        Assert.Equal(payload.Before.CompletedActivations, payload.After.CompletedActivations);
        Assert.True((payload with { After = payload.After with { PendingAdvance = 7 } }).Validate().IsFailure);
        Assert.Empty(actual.Value.Observation.Unobserved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResourceThresholdsFollowCanonicalPoliciesIncludingNonlethalHigherPriorityPolicies(bool safeWins)
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var request = EffectTransactionTests.Request(EffectSequenceBudgetTests.Damage(10, 1)) with { Run = fixture.Run };
        var actor = request.Combat.GetActor("enemy")!;
        var pool = actor.GetResource("focus")!;
        pool = pool with { Definition = pool.Definition! with { ThresholdPolicies = [
            new() { PolicyId = "safe", Comparison = ResourceThresholdComparison.LessThanOrEqual,
                ThresholdSource = ResourceThresholdSource.Minimum, Consequence = ResourceThresholdConsequence.None, Priority = safeWins ? 10 : -10 },
            new() { PolicyId = "defeat", Comparison = ResourceThresholdComparison.LessThanOrEqual,
                ThresholdSource = ResourceThresholdSource.Minimum, Consequence = ResourceThresholdConsequence.DefeatOwner, Priority = 0 }] } };
        request = request with { Combat = request.Combat.ReplaceActor(actor with { ResourceState = actor.ResourceState with
            { Resources = new Dictionary<string, ResourcePool>(actor.ResourceState.Resources) { ["focus"] = pool } } }) };
        var actual = fixture.Executor.ExecuteObserved(request, Observation());
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(fixture.Executor.Execute(request).Value), CanonicalJson.ComputeHash(actual.Value.Execution));
        Assert.Equal(safeWins, actual.Value.Execution.State.GetActor("enemy")!.IsAlive);
        Assert.Equal(new[] { safeWins ? "safe" : "defeat" }, actual.Value.Observation.Events.Occurrences
            .Where(item => item.Payload is ResourceThresholdPayload).Select(item => ((ResourceThresholdPayload)item.Payload).Fact.PolicyId));
        Assert.Equal(safeWins ? 0 : 1, actual.Value.Observation.Events.Occurrences.Count(item => item.Kind == "actor.defeat.changed"));
        var resourceEvent = actual.Value.Observation.Events.Occurrences.First(item => item.Payload is ResourceChangePayload);
        Assert.All(actual.Value.Observation.Events.Occurrences.Where(item => item.Payload is ResourceThresholdPayload or ActorDefeatChangePayload),
            item => Assert.Equal(resourceEvent.EventId, item.ParentEventId));
        Assert.Empty(actual.Value.Observation.Unobserved);
    }

}
