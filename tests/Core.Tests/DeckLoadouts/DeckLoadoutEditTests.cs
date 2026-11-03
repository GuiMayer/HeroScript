using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckLoadouts;
using Moq;
using Xunit;

namespace Core.Tests.DeckLoadouts;

public sealed class DeckLoadoutEditTests
{
    internal static RunState Run => DeckLoadoutPlannerTests.Initialized with
    { ResolvedMode = new() { Definition = new() { DeckCompositionPolicyId = "build" } } };

    [Fact]
    public void ReplacementAndModuleAreWholeGesturesWithoutChangingZonesOrRng()
    {
        var run = Run; var hash = CanonicalJson.ComputeHash(run);
        var swap = DeckLoadoutEdits.Plan(run, DeckLoadoutPlannerTests.Runtime,
            new("hero", "primary", DeckLoadoutPlannerTests.Id(1), DeckLoadoutPlannerTests.Id(4)));
        Assert.True(swap.IsSuccess, swap.IsFailure ? swap.Error : null);
        Assert.Contains(DeckLoadoutPlannerTests.Id(4), swap.Value.After.Groups["primary"]);
        Assert.DoesNotContain(DeckLoadoutPlannerTests.Id(1), swap.Value.After.Groups["primary"]);
        Assert.Same(run.Deck, swap.Value.Candidate.Deck); Assert.Same(run.Determinism, swap.Value.Candidate.Determinism);
        var module = DeckLoadoutEdits.Plan(run, DeckLoadoutPlannerTests.Runtime, new("hero", "secondary", ModuleId: "different"));
        Assert.True(module.IsSuccess, module.IsFailure ? module.Error : null);
        Assert.Equal("different", module.Value.After.Modules["secondary"]);
        Assert.Equal(new[] { DeckLoadoutPlannerTests.Id(6) }, module.Value.After.Groups["secondary"]);
        var customized = DeckLoadoutEdits.Plan(run, DeckLoadoutPlannerTests.Runtime,
            new("hero", "secondary", DeckLoadoutPlannerTests.Id(3), DeckLoadoutPlannerTests.Id(4))).Value.Candidate;
        var unchanged = DeckLoadoutEdits.Plan(customized, DeckLoadoutPlannerTests.Runtime, new("hero", "secondary", ModuleId: "alternate"));
        Assert.True(unchanged.IsSuccess);
        Assert.Contains(DeckLoadoutPlannerTests.Id(4), unchanged.Value.After.Groups["secondary"]);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
    }

    [Fact]
    public void InvalidMixedOrForeignGesturesNeverPublishIncompleteSelection()
    {
        var run = Run; var hash = CanonicalJson.ComputeHash(run);
        DeckLoadoutEdit[] edits = [new("foreign", "primary", DeckLoadoutPlannerTests.Id(1), DeckLoadoutPlannerTests.Id(4)),
            new("hero", "primary", DeckLoadoutPlannerTests.Id(1), DeckLoadoutPlannerTests.Id(3)),
            new("hero", "primary", DeckLoadoutPlannerTests.Id(1), DeckLoadoutPlannerTests.Id(6)),
            new("hero", "secondary", DeckLoadoutPlannerTests.Id(3), DeckLoadoutPlannerTests.Id(6), "different"),
            new("hero", "primary"), new("hero", "missing", ModuleId: "different")];
        foreach (var edit in edits) Assert.True(DeckLoadoutEdits.Plan(run, DeckLoadoutPlannerTests.Runtime, edit).IsFailure);
        Assert.True(DeckLoadoutEdits.Plan(run with { ActiveEncounterId = Guid.NewGuid() }, DeckLoadoutPlannerTests.Runtime,
            new("hero", "secondary", ModuleId: "different")).IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
    }

    [Fact]
    public void QueryAndPreviewUsePinnedPresentationsAndRejectStaleVersionsWithoutMutation()
    {
        var run = Run; var runtime = DeckLoadoutPlannerTests.Runtime;
        var runs = new Mock<IRunQueryService>(); runs.Setup(r => r.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(r => r.Resolve(runtime.Manifest.Revision, "test")).Returns(Result<ContentRuntime>.Success(runtime));
        var query = new DeckLoadoutQueryService(runs.Object, runtimes.Object, new CardContentCompiler(), new EffectiveCardResolver());
        var hash = CanonicalJson.ComputeHash(run);
        var request = new DeckLoadoutPreviewRequest(run.Sequence, run.Determinism.Step,
            new("hero", "primary", DeckLoadoutPlannerTests.Id(1), DeckLoadoutPlannerTests.Id(4)));
        var preview = query.Preview(run.RunId, request);
        Assert.True(preview.IsSuccess, preview.IsFailure ? preview.Error : null);
        Assert.Equal(6, preview.Value.After.Cards.Length);
        Assert.Equal(3, preview.Value.After.Cards.Count(c => c.GroupId == null));
        Assert.Equal("primary", preview.Value.After.Cards.Single(c => c.CardInstanceId == DeckLoadoutPlannerTests.Id(4)).GroupId);
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
        Assert.StartsWith(RunCommandErrors.VersionConflictPrefix, query.Preview(run.RunId, request with { ExpectedStep = run.Determinism.Step + 1 }).Error);
        Assert.False(query.Read(run with { ResolvedMode = null }).Value.Enabled);
    }
}
