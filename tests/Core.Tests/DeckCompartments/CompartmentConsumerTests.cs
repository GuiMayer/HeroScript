using Core.CardZones;
using Core.Common;
using Core.Content;
using Core.Run.Content;
using Core.Run.DeckCompartments;
using Moq;
using Xunit;

namespace Core.Tests.DeckCompartments;

public sealed class CompartmentConsumerTests
{
    [Fact]
    public void RunResolverZoneMetadataAndPresentationShareTheComposedContainer()
    {
        var runtime = CompartmentFixture.Runtime;
        var source = CompartmentPlannerTests.Run;
        var allocated = DeckCompartmentPlanner.Plan(source, runtime, "hero", [new(CompartmentPlannerTests.First, "utility")]).Value.Candidate;
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(r => r.Resolve(runtime.Manifest.Revision, "test")).Returns(Result<ContentRuntime>.Success(runtime));
        var compiler = new CardContentCompiler();
        var effective = new CachedEffectiveCardResolver();
        var instance = source.Deck.GetCard(CompartmentPlannerTests.First)!;
        var resolved = new RunCardResolver(runtimes.Object, compiler, effective).Resolve(allocated, instance);
        Assert.True(resolved.IsSuccess);
        Assert.Equal(2, resolved.Value.All<CardConditionComponentDefinition>().Count);
        Assert.Single(resolved.Value.All<CardTargetingComponentDefinition>());
        var metadata = new RevisionedCardZoneCardMetadataResolver(runtimes.Object, compiler, effective).ResolveTags(instance,
            new() { ContentRevision = runtime.Manifest.Revision, ConfigName = "test", RunSnapshot = allocated });
        Assert.True(metadata.IsSuccess);
        Assert.Equal(resolved.Value.Tags, metadata.Value);
        var projection = CardBasePresentationProjector.Project(compiler.Compile("card", runtime).Value, resolved.Value,
            new() { Kind = CardPresentationSubjectKind.Instance, DefinitionId = "card", CardInstanceId = instance.CardInstanceId },
            new() { ContentRevision = runtime.Manifest.Revision });
        Assert.True(projection.IsSuccess);
        Assert.Equal(2, projection.Value.Conditions.Length);
        Assert.Equal(resolved.Value.ComponentContributions, projection.Value.ComponentContributions);
    }
}
