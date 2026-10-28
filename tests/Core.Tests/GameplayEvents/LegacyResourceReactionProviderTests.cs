using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Resources;
using Core.Run;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class LegacyResourceReactionProviderTests
{
    private static EffectReactionDefinition Reaction() => new()
    {
        ReactionId = "recover", ResourceId = "focus", Operation = ResourceMutationOperation.Subtract,
        Channels = ["hit"], Effects = [new() { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 2 }]
    };

    private static GameplayObserverCollectionContext Context(EffectReactionDefinition reaction, GameplayOwner? owner = null)
    {
        var context = GameplayResponseCompilationTests.Context();
        var run = RelicTransitions.Acquire(context.Run, new() { RelicId = "recovery", Reactions = [reaction] },
            owner ?? new() { Kind = GameplayOwnerKind.Global }).Value.State;
        return context with { Run = run, Snapshot = ResponseSnapshot.AfterOccurrence };
    }

    private static ImmutableArray<GameplayObserverRegistration> Collect(GameplayObserverCollectionContext context)
    {
        var result = GameplayObserverCollector.Collect(context, [new LegacyResourceReactionObserverProvider()]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    [Theory]
    [InlineData(EffectReactionOwnerScope.Source, EffectReactionSelection.Source, "enemy", "enemy")]
    [InlineData(EffectReactionOwnerScope.Target, EffectReactionSelection.Source, "hero", "enemy")]
    [InlineData(EffectReactionOwnerScope.Source, EffectReactionSelection.Target, "enemy", "hero")]
    [InlineData(EffectReactionOwnerScope.Target, EffectReactionSelection.Target, "hero", "hero")]
    public void MigrationKeepsPhysicalOwnershipSelectionRevisionAndSuppression(EffectReactionOwnerScope ownerScope,
        EffectReactionSelection selection, string owner, string target)
    {
        var context = Context(Reaction() with { OwnerScope = ownerScope, Selection = selection });
        var registrations = Collect(context);
        var candidate = Assert.Single(GameplayResponseMatcher.Create(registrations).Value.Match(GameplayResponseMatcherTests.Fact(), context).Value);
        Assert.Equal(owner, candidate.OwnerActorId); Assert.Equal(owner, candidate.SourceActorId); Assert.Equal(target, candidate.TargetActorId);
        Assert.Equal(ResponsePropagation.RecordOnly, candidate.Registration.Response.Definition.Propagation);
        Assert.Equal(context.Run.Relics[0].ContentRevision, candidate.Registration.Origin.ContentRevision);
        Assert.Equal(context.Run.Relics[0].RelicInstanceId.ToString("D"), candidate.Registration.Origin.ContainerId);
    }

    [Fact]
    public void PaymentsSettlementsForeignChannelsAndUnownedSourcesRemainExcluded()
    {
        var context = Context(Reaction(), new() { Kind = GameplayOwnerKind.Entity, Id = "hero" });
        var matcher = GameplayResponseMatcher.Create(Collect(context)).Value;
        var fact = GameplayResponseMatcherTests.Fact() with { SourceActorId = "hero", SubjectActorId = "enemy" };
        Assert.Single(matcher.Match(fact, context).Value);
        Assert.Empty(matcher.Match(fact with { SourceActorId = "ally" }, context).Value);
        Assert.Empty(matcher.Match(fact with { ReasonId = "cost.payment" }, context).Value);
        Assert.Empty(matcher.Match(fact with { ReasonId = "cost.refund" }, context).Value);
        Assert.Empty(matcher.Match(fact with { Payload = ((ResourceChangePayload)fact.Payload) with { Role = ResourceOccurrenceRole.Settlement } }, context).Value);
        Assert.Empty(matcher.Match(fact with { Payload = ((ResourceChangePayload)fact.Payload) with { CalculationChannel = "other" } }, context).Value);
    }

    [Fact]
    public void DuplicateAndContradictoryLegacyTagsKeepTheirOriginalMeaning()
    {
        var context = Context(Reaction() with { Channels = ["hit", "hit"], RequiredTags = ["fire", "fire"] });
        var matcher = GameplayResponseMatcher.Create(Collect(context)).Value;
        Assert.Single(matcher.Match(GameplayResponseMatcherTests.Fact(), context).Value);
        Assert.Empty(matcher.Match(GameplayResponseMatcherTests.Fact() with { Tags = [] }, context).Value);
        var contradictory = Context(Reaction() with { RequiredTags = ["fire"], ExcludedTags = ["fire"] });
        Assert.Empty(Collect(contradictory));
    }

    [Fact]
    public void CollectionIsRepeatableAndPreservesLegacyPriorityBeforeRelicIdentity()
    {
        var context = Context(Reaction() with { Priority = 5 });
        context = context with { Run = RelicTransitions.Acquire(context.Run,
            new() { RelicId = "second", Reactions = [Reaction() with { Priority = -1 }] }, new() { Kind = GameplayOwnerKind.Global }).Value.State };
        var original = CanonicalJson.ComputeHash(context.Run);
        var hashes = Enumerable.Range(0, 5).Select(_ => CanonicalJson.ComputeHash(Collect(context))).ToArray();
        Assert.Single(hashes.Distinct()); Assert.Equal(original, CanonicalJson.ComputeHash(context.Run));
        var candidates = GameplayResponseMatcher.Create(Collect(context)).Value.Match(GameplayResponseMatcherTests.Fact(), context).Value;
        Assert.Equal(2, candidates.Length);
        Assert.Equal(context.Run.Relics.Single(relic => relic.DefinitionId == "second").RelicInstanceId.ToString("D"), candidates[0].Registration.Origin.ContainerId);
    }
}
