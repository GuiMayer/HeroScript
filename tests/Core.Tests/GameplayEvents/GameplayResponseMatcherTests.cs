using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Resources;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResponseMatcherTests
{
    internal static GameplayOccurrence Fact() => new()
    {
        EventId = "event", RootEventId = "event", Kind = "resource.change.resolved", ReasonId = "effect.application",
        Scope = new() { SettingId = "setting", ContentRevision = "revision", RunId = "run", BranchId = "branch", ActionId = "action", CommandId = "command" },
        SourceActorId = "enemy", SubjectActorId = "hero", AttributedActorId = "enemy", ImpactId = "impact", ProcId = "proc", Tags = ["fire"],
        Payload = new ResourceChangePayload { ResourceId = "focus", Role = ResourceOccurrenceRole.PrimaryEffect,
            Field = ResourceValueField.Current, Operation = ResourceMutationOperation.Subtract,
            Before = 10, Operand = 3, RequestedValue = 7, After = 7, CalculationChannel = "hit" }
    };

    private static GameplayResponseMatcher Matcher(GameplayResponseDefinition definition)
    {
        var context = GameplayResponseCompilationTests.Context();
        var collection = GameplayObserverCollector.Collect(context,
            [new GameplayResponseCompilationTests.FixedProvider(GameplayResponseCompilationTests.Registration(definition: definition))]);
        Assert.True(collection.IsSuccess, collection.IsFailure ? collection.Error : null);
        return GameplayResponseMatcher.Create(collection.Value).Value;
    }

    [Fact]
    public void OverlappingTopicsInvokeOnceAndConjunctiveFiltersUseTypedFacts()
    {
        var definition = GameplayResponseCompilationTests.Definition() with
        {
            Observe = new()
            {
                Kinds = new() { Include = ["resource.**", "resource.change.*", "resource.change.resolved"] },
                RequiredTags = ["fire"], Source = new() { RelationshipToOwner = SideRelationship.Enemy },
                Subject = new() { MatchesOwner = true }, Attributed = new() { ActorIds = ["enemy"] },
                SourceSubjectRelationship = SideRelationship.Enemy,
                Resource = new() { ResourceIds = ["FOCUS"], Direction = ResourceChangeDirection.Decrease,
                    Role = ResourceOccurrenceRole.PrimaryEffect, CalculationChannels = ["hit"] }
            }
        };
        var matcher = Matcher(definition);
        var context = GameplayResponseCompilationTests.Context();
        var before = CanonicalJson.ComputeHash(context.Run);
        for (var i = 0; i < 5; i++)
        {
            var candidate = Assert.Single(matcher.Match(Fact(), context).Value);
            Assert.Equal("hero", candidate.OwnerActorId); Assert.Equal("hero", candidate.SourceActorId); Assert.Equal("hero", candidate.TargetActorId);
        }
        Assert.Equal(before, CanonicalJson.ComputeHash(context.Run));
        Assert.Empty(matcher.Match(Fact() with { Tags = ["cold"] }, context).Value);
        Assert.Empty(matcher.Match(Fact() with { AttributedActorId = null }, context).Value);
        Assert.Empty(matcher.Match(Fact() with { Payload = ((ResourceChangePayload)Fact().Payload) with { Role = ResourceOccurrenceRole.Settlement } }, context).Value);
        Assert.Empty(matcher.Match(Fact() with { ReasonId = "cost.payment", Tags = [] }, context).Value);
    }

    [Fact]
    public void ExclusionsWinAndAbsentActorNeverBecomesTheFirstActor()
    {
        var definition = GameplayResponseCompilationTests.Definition() with
        { Observe = new() { Kinds = new() { Include = ["resource.**"], Exclude = ["resource.change.resolved"] } } };
        Assert.Empty(Matcher(definition).Match(Fact(), GameplayResponseCompilationTests.Context()).Value);
        definition = GameplayResponseCompilationTests.Definition() with { Bindings = new() { Source = new() { Kind = ResponseActorBindingKind.ObserverSource } } };
        Assert.Empty(Matcher(definition).Match(Fact(), GameplayResponseCompilationTests.Context()).Value);
        definition = definition with { Bindings = definition.Bindings with { Missing = MissingResponseBinding.RejectAction } };
        Assert.True(Matcher(definition).Match(Fact(), GameplayResponseCompilationTests.Context()).IsFailure);
    }

    [Fact]
    public void LogicalOwnerCanBeDifferentFromPhysicalOwnerAndSnapshotIsExplicit()
    {
        var definition = GameplayResponseCompilationTests.Definition() with
        {
            Bindings = new() { Owner = new() { Kind = ResponseActorBindingKind.EventSource }, Target = new() { Kind = ResponseActorBindingKind.ObserverOwner } },
            Observe = new() { Source = new() { MatchesOwner = true } }
        };
        var context = GameplayResponseCompilationTests.Context();
        var matcher = Matcher(definition);
        var candidate = Assert.Single(matcher.Match(Fact(), context).Value);
        Assert.Equal("enemy", candidate.OwnerActorId); Assert.Equal("enemy", candidate.SourceActorId); Assert.Equal("hero", candidate.TargetActorId);
        Assert.Empty(matcher.Match(Fact(), context with { Snapshot = ResponseSnapshot.AfterOccurrence }).Value);
        Assert.True(matcher.Match(Fact() with { Scope = Fact().Scope with { ContentRevision = "foreign" } }, context).IsFailure);
        Assert.True(matcher.Match(Fact(), context with { Run = context.Run with { SettingId = "foreign" } }).IsFailure);
    }

    [Theory]
    [InlineData(ResponseFrequencyScope.Occurrence)]
    [InlineData(ResponseFrequencyScope.Impact)]
    [InlineData(ResponseFrequencyScope.Proc)]
    [InlineData(ResponseFrequencyScope.RootAction)]
    public void QuotasAreImmutableScopedAndDeduplicateOccurrences(ResponseFrequencyScope scope)
    {
        var definition = GameplayResponseCompilationTests.Definition() with { Frequency = new() { Scope = scope, Maximum = 2 } };
        var context = GameplayResponseCompilationTests.Context();
        var matcher = Matcher(definition);
        GameplayResponseCandidate Candidate(string id) => Assert.Single(matcher.Match(Fact() with { EventId = id, RootEventId = id }, context).Value);
        var empty = GameplayResponseQuotas.Begin(Fact().Scope).Value;
        var first = empty.Reserve(Candidate("one")).Value;
        Assert.True(first.Accepted); Assert.Equal(0, empty.Count); Assert.Equal(1, first.Quotas.Count);
        Assert.False(first.Quotas.Reserve(Candidate("one")).Value.Accepted);
        var second = first.Quotas.Reserve(Candidate("two")).Value;
        Assert.True(second.Accepted);
        var third = second.Quotas.Reserve(Candidate("three")).Value;
        Assert.Equal(scope == ResponseFrequencyScope.Occurrence, third.Accepted);
        Assert.Equal(first.InvocationId, empty.Reserve(Candidate("one")).Value.InvocationId);
        var foreign = Candidate("one") with { Occurrence = Fact() with { Scope = Fact().Scope with { BranchId = "other" } } };
        Assert.True(first.Quotas.Reserve(foreign).IsFailure);
        Assert.Equal(1, first.Quotas.Count);
    }

    [Fact]
    public void BudgetFailureAndMissingImpactDoNotConsumeCounters()
    {
        var context = GameplayResponseCompilationTests.Context();
        var definition = GameplayResponseCompilationTests.Definition();
        var matcher = Matcher(definition);
        var first = Assert.Single(matcher.Match(Fact(), context).Value);
        var quota = GameplayResponseQuotas.Begin(Fact().Scope, 1).Value.Reserve(first).Value.Quotas;
        var second = first with { Occurrence = Fact() with { EventId = "two", RootEventId = "two" } };
        Assert.True(quota.Reserve(second).IsFailure); Assert.Equal(1, quota.Count);
        var impact = first with { Registration = first.Registration with { Response = first.Registration.Response with
            { Definition = definition with { Frequency = new() { Scope = ResponseFrequencyScope.Impact } } } }, Occurrence = Fact() with { ImpactId = null } };
        Assert.True(GameplayResponseQuotas.Begin(Fact().Scope).Value.Reserve(impact).IsFailure);
        Assert.True(GameplayResponseQuotas.Begin(Fact().Scope, 0).IsFailure);
    }
}
