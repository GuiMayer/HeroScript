using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Core.Tests.Content;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardPresentationQueryTests
{
    [Fact]
    public async Task OffersAndOwnedCardsShareBaseFactsWithoutManufacturingInstancesOrConsumingRandomness()
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var run = new RunState { RunId = Guid.NewGuid(), ConfigName = "ascendant", CurrentNodeId = "reward",
            Determinism = DeterministicContext.Create(1, runtime.Manifest.Revision),
            Deck = TestCardZones.FromDefinitions(("collection", new[] { "w1_strike" })),
            CardSelections = [new() { SelectionInstanceId = Guid.NewGuid(), NodeId = "reward",
                Options = [new() { CardId = "w1_strike" }, new() { CardId = "w1_flurry", Decomposed = true }] },
                new() { NodeId = "past", Options = [new() { CardId = "w1_flurry" }] }],
            Shops = [new() { ShopInstanceId = Guid.NewGuid(), NodeId = "reward", Items = [
                new() { ItemId = "sold", CardId = "w1_flurry", Purchased = true }]}] };
        var before = CanonicalJson.ComputeHash(run);
        var runs = new Mock<IRunQueryService>();
        runs.Setup(value => value.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(value => value.Resolve(runtime.Manifest.Revision, "ascendant")).Returns(Result<ContentRuntime>.Success(runtime));
        var service = new CardPresentationQueryService(runs.Object, runtimes.Object, new CardContentCompiler(), new EffectiveCardResolver());
        var result = service.Read(run.RunId);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.Cards.Length);
        var owned = result.Value.Cards.Single(value => value.Identity.Kind == CardPresentationSubjectKind.Instance);
        var offer = result.Value.Cards.Single(value => value.Identity.Kind == CardPresentationSubjectKind.Offer);
        Assert.Null(offer.Identity.CardInstanceId);
        Assert.Equal(CanonicalJson.ComputeHash(owned.Effects), CanonicalJson.ComputeHash(offer.Effects));
        Assert.Equal(CanonicalJson.ComputeHash(owned.Costs), CanonicalJson.ComputeHash(offer.Costs));
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.Same(result.Value, service.Read(run.RunId).Value);
        runs.Setup(value => value.GetRun(run.RunId)).Returns(Result<RunState>.Success(run with { Sequence = run.Sequence + 1 }));
        Assert.Equal(run.Sequence + 1, service.Read(run.RunId).Value.RunSequence);
    }

    [Fact]
    public async Task HiddenZonesNeverLeakIdentityOrMechanics()
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var graph = TestCardZones.Graph(["secret"]);
        var run = new RunState { RunId = Guid.NewGuid(), ConfigName = "ascendant",
            Determinism = DeterministicContext.Create(1, runtime.Manifest.Revision),
            Deck = TestCardZones.FromDefinitions(("secret", new[] { "w1_strike" })),
            ResolvedMode = new() { CardZoneSystem = graph with { Zones = graph.Zones.Select(zone => zone with {
                Visibility = new() { Contents = Core.CardZones.CardZoneVisibility.None } }).ToImmutableArray() } } };
        var runs = new Mock<IRunQueryService>();
        runs.Setup(value => value.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(value => value.Resolve(runtime.Manifest.Revision, "ascendant")).Returns(Result<ContentRuntime>.Success(runtime));
        var compiler = new Mock<ICardContentCompiler>(MockBehavior.Strict);
        var result = new CardPresentationQueryService(runs.Object, runtimes.Object, compiler.Object, new EffectiveCardResolver()).Read(run.RunId);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Empty(result.Value.Cards);
        compiler.VerifyNoOtherCalls();
    }
}
