using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckCompartments;
using Xunit;

namespace Core.Tests.DeckCompartments;

public sealed class CompartmentCompositionTests
{
    private static CompiledCardDefinition Base => new CardContentCompiler().Compile(new CardContentDefinition {
        CardId = "card", Components = [new CardConditionComponentDefinition { ComponentId = "base", Expression = "1" }] }).Value;
    private static CardInstanceState Instance => new() { CardInstanceId = CompartmentPlannerTests.First, DefinitionId = "card" };

    [Fact]
    public void OrdinaryAndContributedContainersHaveIdenticalComponentsWithoutLedgerMutation()
    {
        var resolver = new EffectiveCardResolver();
        var components = ImmutableArray.Create<CardComponentDefinition>(
            new CardCostComponentDefinition { ComponentId = "extra.cost", Costs = new() { Costs = [new() { ResourceId = "energy", Amount = 1 }] } },
            new CardConditionComponentDefinition { ComponentId = "extra.condition", Expression = "1" },
            new CardEffectComponentDefinition { ComponentId = "extra.effect", Effect = new() { Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF, TargetResource = "energy", FlatValue = 2 } },
            new CardTargetingComponentDefinition { ComponentId = "extra.target", MinimumTargets = 0, MaximumTargets = 0 },
            new CardTriggerComponentDefinition { ComponentId = "extra.trigger", Boundary = "test", Effects = [] });
        var source = new CardComponentContribution { SourceId = "any-source", ContentRevision = "revision", Components = components };
        var ownHash = CanonicalJson.ComputeHash(Instance);
        var composed = resolver.ResolveWithContributions(Base, Instance, [source]);
        Assert.True(composed.IsSuccess, composed.IsFailure ? composed.Error : null);
        var authored = new CardContentCompiler().Compile(new CardContentDefinition { CardId = "card", Components = Base.Components.Concat(components).ToArray() });
        Assert.True(authored.IsSuccess);
        Assert.Equal(CanonicalJson.Serialize(authored.Value.Components), CanonicalJson.Serialize(composed.Value.Components));
        Assert.Empty(composed.Value.TransformationLedger);
        Assert.Single(composed.Value.ComponentContributions!.Value);
        Assert.Equal(ownHash, CanonicalJson.ComputeHash(Instance));
        Assert.Null(resolver.Resolve(Base, Instance).Value.ComponentContributions);
    }

    [Fact]
    public void CollisionsAndSingletonConflictsAreRejectedByTheCanonicalCompiler()
    {
        var source = new CardComponentContribution { SourceId = "s", ContentRevision = "r", Components = [Base.Components[0]] };
        Assert.True(new EffectiveCardResolver().ResolveWithContributions(Base, Instance, [source]).IsFailure);
        source = source with { Components = [new CardTargetingComponentDefinition { ComponentId = "one" }, new CardTargetingComponentDefinition { ComponentId = "two" }] };
        Assert.True(new EffectiveCardResolver().ResolveWithContributions(Base, Instance, [source]).IsFailure);
    }

    [Fact]
    public void RealocationChangesOnlyDerivedContributionAndCacheCannotReuseOldSource()
    {
        var run = CompartmentPlannerTests.Run;
        var runtime = CompartmentFixture.Runtime;
        var first = DeckCompartmentPlanner.Plan(run, runtime, "hero", [new(Instance.CardInstanceId, "utility")]).Value.Candidate;
        var other = DeckCompartmentPlanner.Plan(first, runtime, "hero", [new(Instance.CardInstanceId, "other")]).Value.Candidate;
        var cached = new CachedEffectiveCardResolver();
        var a = DeckCompartmentComposition.Resolve(first, runtime, Base, Instance, cached).Value;
        var b = DeckCompartmentComposition.Resolve(other, runtime, Base, Instance, cached).Value;
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
        Assert.Equal(a.Fingerprint, DeckCompartmentComposition.Resolve(first, runtime, Base, Instance, cached).Value.Fingerprint);
        Assert.Same(run.Deck, other.Deck);
        Assert.Equal(new EffectiveCardResolver().Resolve(Base, Instance).Value.Fingerprint,
            DeckCompartmentComposition.Resolve(run, runtime, Base, Instance, cached).Value.Fingerprint);
    }
}
