using Core.Abstractions.Persistence;
using Core.Run;
using Core.Run.Branching;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunLineageIndexTests
{
    [Fact]
    public async Task Rebuild_IndexesAncestryOnceAndServesTreeQueriesWithoutRunScans()
    {
        var rootId = Guid.Parse("10000000-0000-8000-8000-000000000021");
        var firstId = Guid.Parse("20000000-0000-8000-8000-000000000021");
        var secondId = Guid.Parse("30000000-0000-8000-8000-000000000021");
        var simulationId = Guid.Parse("40000000-0000-8000-8000-000000000021");
        var commits = new Dictionary<Guid, RunCommit>
        {
            [rootId] = Initial(rootId, RunLineage.Root(rootId)),
            [firstId] = Initial(firstId, RunLineage.Branch(rootId, rootId, 2, new string('1', 64), null, "first")),
            [secondId] = Initial(secondId, RunLineage.Branch(rootId, firstId, 3, new string('2', 64), null, "second")),
            [simulationId] = Initial(
                simulationId,
                RunLineage.Branch(rootId, rootId, 2, new string('3', 64), null, "simulation:test"))
        };
        var reader = new Mock<IRunCommitReader>();
        reader.Setup(item => item.ListRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(commits.Keys.ToArray());
        reader.Setup(item => item.LoadCommitAsync(
                It.IsAny<Guid>(),
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid runId, int _, CancellationToken _) => commits.GetValueOrDefault(runId));
        using var index = new RunLineageIndex(reader.Object);

        await index.RebuildAsync();
        var visibleChildren = await index.GetChildrenAsync(rootId);
        var descendants = await index.GetDescendantsAsync(rootId, includeInternalSimulations: false);
        var allDescendants = await index.GetDescendantsAsync(rootId, includeInternalSimulations: true);

        Assert.Equal([firstId], visibleChildren.Select(entry => entry.RunId));
        Assert.Equal([firstId, secondId], descendants.Select(entry => entry.RunId));
        Assert.Equal(3, allDescendants.Count);
        Assert.Equal(3, await index.CountDescendantsAsync(rootId));
        reader.Verify(item => item.ListRunIdsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IndexAsync_AddsNewBranchWithoutRebuildingTheProjection()
    {
        var rootId = Guid.Parse("10000000-0000-8000-8000-000000000022");
        var branchId = Guid.Parse("20000000-0000-8000-8000-000000000022");
        var reader = new Mock<IRunCommitReader>();
        reader.Setup(item => item.ListRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([rootId]);
        reader.Setup(item => item.LoadCommitAsync(rootId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Initial(rootId, RunLineage.Root(rootId)));
        using var index = new RunLineageIndex(reader.Object);
        await index.RebuildAsync();

        await index.IndexAsync(Initial(
            branchId,
            RunLineage.Branch(rootId, rootId, 1, new string('a', 64), null, "new-line")));

        Assert.Equal(branchId, Assert.Single(await index.GetChildrenAsync(rootId)).RunId);
        reader.Verify(item => item.ListRunIdsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static RunCommit Initial(Guid runId, RunLineage lineage) => new()
    {
        RunId = runId,
        Sequence = 1,
        Lineage = lineage,
        StateAfter = new RunState { RunId = runId, Sequence = 1, Lineage = lineage }
    };
}
