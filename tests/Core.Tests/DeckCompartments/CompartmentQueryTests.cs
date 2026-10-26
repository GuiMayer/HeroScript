using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckCompartments;
using Moq;
using Xunit;

namespace Core.Tests.DeckCompartments;

public sealed class CompartmentQueryTests
{
    [Fact]
    public void PreviewUsesSamePlannerWithoutMutationAndRejectsStaleDraft()
    {
        var run = CompartmentPlannerTests.Run;
        var runtime = CompartmentFixture.Runtime;
        var runs = new Mock<IRunQueryService>();
        runs.Setup(r => r.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(r => r.Resolve(runtime.Manifest.Revision, "test")).Returns(Result<ContentRuntime>.Success(runtime));
        var query = new DeckCompartmentQueryService(runs.Object, runtimes.Object, new CardContentCompiler(), new EffectiveCardResolver());
        var before = CanonicalJson.ComputeHash(run);
        var request = new DeckCompartmentPreviewRequest(run.Sequence, run.Determinism.Step, [new(CompartmentPlannerTests.First, "utility")]);
        var preview = query.Preview(run.RunId, request);
        Assert.True(preview.IsSuccess, preview.IsFailure ? preview.Error : null);
        Assert.Empty(preview.Value.Before.Allocations);
        Assert.Single(preview.Value.After.Allocations);
        Assert.Equal(2, preview.Value.After.Cards.First(c => c.CardInstanceId == CompartmentPlannerTests.First).Presentation.Conditions.Length);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.StartsWith(RunCommandErrors.VersionConflictPrefix, query.Preview(run.RunId, request with { ExpectedSequence = run.Sequence + 1 }).Error);
        Assert.True(query.Preview(run.RunId, request with { Allocations = default }).IsFailure);
        var disabled = query.Read(run with { ResolvedMode = null });
        Assert.False(disabled.Value.Enabled);
        Assert.Empty(disabled.Value.Cards);
    }
}
