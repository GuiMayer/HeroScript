using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Meta;
using Core.Run;
using Core.Tests.Run;
using Moq;
using Xunit;

namespace Core.Tests.Persistence;

public sealed class ProfileProgressRunCommitStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HeroScript", "profile-progress-tests", Guid.NewGuid().ToString("N"));

    internal static RunCommit Commit(RunState state, RunState? before = null, int command = 1)
    {
        var payload = JsonSerializer.SerializeToElement(new { command });
        var hash = CanonicalJson.ComputeHash(state);
        RunCommitFrame[] frames = [new() { Kind = "TEST", Step = state.Determinism.Step, ResultHash = hash, Resolution = payload }];
        return new() { RunId = state.RunId, Sequence = state.Sequence, StateAfter = state,
            Lineage = state.Sequence == 1 ? state.Lineage : null,
            BeforeStep = before?.Determinism.Step ?? 0, AfterStep = state.Determinism.Step,
            RootCommand = ProfileProgressTests.Command(command) with { ExpectedSequence = state.Sequence - 1,
                ExpectedStep = before?.Determinism.Step ?? 0, PayloadHash = CanonicalJson.ComputeHash(payload) },
            PreviousStateHash = before == null ? string.Empty : CanonicalJson.ComputeHash(before), StateHash = hash,
            Command = payload, Frames = frames, Facts = RunCommitFacts.FromFrames(frames) };
    }

    [Fact]
    public async Task AtomicProof_RetryRestartAndProfileProjectionAgreeWithoutASideSave()
    {
        using var raw = new FileRunCommitStore(_directory, NullLogger.Instance);
        var store = raw;
        var commit = Commit(ProfileProgressTests.State());
        var result = await store.AppendAsync(commit);
        Assert.NotNull(result.Commit.ProfileProgress);
        Assert.Equal(commit.StateHash, result.Commit.StateHash);
        Assert.Equal(CanonicalJson.ComputeHash(commit.RequireState()), CanonicalJson.ComputeHash((await store.LoadLatestStateAsync(commit.RunId))!));
        var original = await File.ReadAllBytesAsync(Path.Combine(_directory, commit.RunId.ToString("D"), "commits", "00000001.json"));
        var retry = await store.AppendAsync(commit);
        Assert.True(retry.Duplicate);
        Assert.Equal(CanonicalJson.ComputeHash(result.Commit.ProfileProgress), CanonicalJson.ComputeHash(retry.Commit.ProfileProgress));
        using var rawRestarted = new FileRunCommitStore(_directory, NullLogger.Instance);
        var restarted = rawRestarted;
        var profile = await new PlayerProfileProjectionReader(restarted).ReadAsync("player", "setting");
        Assert.Equal(1, profile.ProgressSequence);
        Assert.Equal("new-card", Assert.Single(profile.Unlocks));
        Assert.Equal("new-card", Assert.Single(profile.UnlockProofs).UnlockId);
        Assert.Equal(result.Commit.ProfileProgress!.Revision, profile.ProgressRevision);
        Assert.Empty((await restarted.ReadAsync("player", "other")).Grants);
        Assert.Empty((await restarted.ReadAsync("other", "setting")).Grants);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(_directory, commit.RunId.ToString("D"), "commits", "00000001.json")));
        Assert.All(Directory.GetFiles(Path.Combine(_directory, "_profile-progress")), path => Assert.Equal(".lease", Path.GetExtension(path)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.DeleteRunAsync(commit.RunId));
    }

    [Fact]
    public async Task TwoHostsConcurrentRuns_SerializeBasesAndGrantThresholdExactlyOnce()
    {
        using var rawA = new FileRunCommitStore(_directory, NullLogger.Instance);
        using var rawB = new FileRunCommitStore(_directory, NullLogger.Instance);
        var a = rawA;
        var b = rawB;
        // Warm both caches before any contribution; the lease marker must invalidate the other host.
        await a.ReadAsync("player", "setting");
        await b.ReadAsync("player", "setting");
        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(index => (index % 2 == 0 ? a : b)
            .AppendAsync(Commit(ProfileProgressTests.State(index, ProfileProgressTests.Policy(2)), command: index))));
        Assert.Equal(1, results.Sum(result => result.Commit.ProfileProgress!.Grants.Length));
        Assert.Equal(Enumerable.Range(1, 8).Select(index => (long)index), results.Select(result => result.Commit.ProfileProgress!.ProfileSequence).Order());
        var snapshotA = await a.ReadAsync("player", "setting");
        var snapshotB = await b.ReadAsync("player", "setting");
        Assert.Equal(8, snapshotA.Sequence);
        Assert.Equal(8, snapshotA.Contributions.Length);
        Assert.Single(snapshotA.Grants);
        Assert.Equal(CanonicalJson.ComputeHash(snapshotA), CanonicalJson.ComputeHash(snapshotB));
        // An old retry does not replace its captured basis with today's profile.
        var duplicate = await b.AppendAsync(Commit(ProfileProgressTests.State(1, ProfileProgressTests.Policy(2))));
        Assert.True(duplicate.Duplicate);
        Assert.Equal(8, (await a.ReadAsync("player", "setting")).Sequence);
    }

    [Fact]
    public async Task HistoricalMetaProofsRemainReadableWithoutReinterpretingAnUnavailableGameplayVersion()
    {
        using var first = new FileRunCommitStore(_directory, NullLogger.Instance);
        var commit = Commit(ProfileProgressTests.State());
        await first.AppendAsync(commit);
        var path = Path.Combine(_directory, commit.RunId.ToString("D"), "commits", "00000001.json");
        var historical = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        historical["EngineVersion"] = "unavailable-historical-execution";
        await File.WriteAllTextAsync(path, historical.ToJsonString());
        var preservedBytes = await File.ReadAllBytesAsync(path);
        using var restarted = new FileRunCommitStore(_directory, NullLogger.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.LoadLatestStateAsync(commit.RunId));
        var profile = await restarted.ReadAsync("player", "setting");
        Assert.Equal(1, profile.Sequence);
        Assert.Equal("new-card", Assert.Single(profile.Grants).Key);
        Assert.Equal(preservedBytes, await File.ReadAllBytesAsync(path));
        Assert.Empty((await restarted.ReadAsync("player", "different")).Grants);
    }

    [Fact]
    public async Task InterleavedHostsOnTheSameRun_RefreshTheAppendCursorAndProfileBasis()
    {
        using var a = new FileRunCommitStore(_directory, NullLogger.Instance);
        using var b = new FileRunCommitStore(_directory, NullLogger.Instance);
        var first = ProfileProgressTests.State();
        await a.AppendAsync(Commit(first));
        var second = first with { Sequence = 2, Map = new() { ResolvedNodeIds = ["first"] }, Determinism = first.Determinism.AdvanceStep() };
        await b.AppendPreparedAsync(PreparedRunCommit.CreateVerified(Commit(second, first, 2), CanonicalJson.ComputeHash(second), first));
        var third = second with { Sequence = 3, Map = new() { ResolvedNodeIds = ["first", "boss"] }, Lifecycle = RunLifecycleState.Completed,
            Determinism = second.Determinism.AdvanceStep() };
        await a.AppendPreparedAsync(PreparedRunCommit.CreateVerified(Commit(third, second, 3), CanonicalJson.ComputeHash(third), second));
        Assert.Equal(3, (await b.ReadAsync("player", "setting")).Sequence);
        Assert.Equal(CanonicalJson.ComputeHash(third), CanonicalJson.ComputeHash((await b.LoadLatestStateAsync(first.RunId))!));
    }

    [Fact]
    public async Task FailedDurableAppend_LeavesNoPhantomProgressAndInvalidatesCachedBasis()
    {
        var inner = new Mock<IRunCommitStore>();
        inner.Setup(store => store.ListRunIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Guid>());
        inner.Setup(store => store.AppendAsync(It.IsAny<RunCommit>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("disk failure"));
        var store = new ProfileProgressCommitCoordinator(inner.Object, _directory,
            (prepared, ct) => inner.Object.AppendAsync(prepared.Commit with { StateAfter = prepared.LiveState }, ct));
        Assert.Equal(0, (await store.ReadAsync("player", "setting")).Sequence);
        await Assert.ThrowsAsync<IOException>(() => store.AppendAsync(Commit(ProfileProgressTests.State())));
        var profile = await store.ReadAsync("player", "setting");
        Assert.Equal(0, profile.Sequence);
        Assert.Empty(profile.Grants);
        inner.Verify(source => source.ListRunIdsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GameplayOnlyCommits_BypassProfileIO_AndKeepPreparedStateDeltas()
    {
        using var raw = new FileRunCommitStore(_directory, NullLogger.Instance);
        var store = raw;
        var first = ProfileProgressTests.State();
        await store.AppendAsync(Commit(first));
        var marker = Directory.GetFiles(Path.Combine(_directory, "_profile-progress")).Single();
        var originalMarker = await File.ReadAllBytesAsync(marker);
        var before = first;
        for (var sequence = 2; sequence <= 5; sequence++)
        {
            var after = before with { Sequence = sequence, Determinism = before.Determinism.AdvanceStep() };
            var prepared = PreparedRunCommit.CreateVerified(Commit(after, before, sequence), CanonicalJson.ComputeHash(after), before);
            var appended = await store.AppendPreparedAsync(prepared);
            Assert.Null(appended.Commit.ProfileProgress);
            Assert.Equal(RunCommitStorageKind.Delta, appended.Commit.StorageKind);
            Assert.Equal(prepared.Bytes.ToArray(), await File.ReadAllBytesAsync(Path.Combine(_directory, first.RunId.ToString("D"), "commits", $"{sequence:D8}.json")));
            before = after;
        }
        Assert.Equal(originalMarker, await File.ReadAllBytesAsync(marker));
        Assert.Equal(1, (await store.ReadAsync("player", "setting")).Sequence);
        var envelopes = await raw.LoadCommitEnvelopesAsync(first.RunId);
        Assert.Null(envelopes[^1].StateAfter);
        using var restart = new FileRunCommitStore(_directory, NullLogger.Instance);
        Assert.Equal(1, (await restart.ReadAsync("player", "setting")).Sequence);
    }

    [Fact]
    public async Task ProfileBearingDelta_UsesSameAtomicAppendAndCanRebuildAcrossRestart()
    {
        using var raw = new FileRunCommitStore(_directory, NullLogger.Instance);
        var store = raw;
        var before = ProfileProgressTests.State();
        await store.AppendAsync(Commit(before));
        var after = before with { Sequence = 2, Lifecycle = RunLifecycleState.Completed, Determinism = before.Determinism.AdvanceStep() };
        var prepared = PreparedRunCommit.CreateVerified(Commit(after, before, 2), CanonicalJson.ComputeHash(after), before);
        var result = await store.AppendPreparedAsync(prepared);
        Assert.Equal(RunCommitStorageKind.Delta, result.Commit.StorageKind);
        Assert.Null(result.Commit.StateAfter);
        Assert.Equal(2, result.Commit.ProfileProgress!.ProfileSequence);
        var loaded = await raw.LoadCommitAsync(after.RunId, 2);
        Assert.Equal(CanonicalJson.ComputeHash(after), CanonicalJson.ComputeHash(loaded!.RequireState()));
        using var restarted = new FileRunCommitStore(_directory, NullLogger.Instance);
        Assert.Equal(2, (await restarted.ReadAsync("player", "setting")).Sequence);
        Assert.True((await store.AppendPreparedAsync(prepared)).Duplicate);
    }

    [Fact]
    public async Task InjectedProofsAndCommandCollisionsCannotAdvanceProfile()
    {
        using var raw = new FileRunCommitStore(_directory, NullLogger.Instance);
        var store = raw;
        var commit = Commit(ProfileProgressTests.State());
        var first = await store.AppendAsync(commit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(first.Commit));
        var changedState = commit.RequireState() with { CurrentNodeId = "forged" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(commit with { StateAfter = changedState, StateHash = CanonicalJson.ComputeHash(changedState) }));
        Assert.Equal(1, (await store.ReadAsync("player", "setting")).Sequence);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
