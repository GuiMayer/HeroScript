using System.Text.Json;
using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Run;
using Core.Run.Branching;
using Core.Combat.Models;
using Xunit;

namespace Core.Tests.Persistence;

public sealed class FileRunCommitStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"heroscript-commits-{Guid.NewGuid():N}");

    [Fact]
    public async Task Append_RoundTripsAnAuthoritativeCommitAcrossRestart()
    {
        var runId = Guid.NewGuid();
        var first = CreateCommit(runId, 1, null, 10);
        await usingScope(async store => await store.AppendAsync(first));

        using var restarted = new FileRunCommitStore(_directory, NullLogger.Instance);
        var loaded = await restarted.LoadCommitAsync(runId, 1);

        Assert.NotNull(loaded);
        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(loaded));
        Assert.Equal(1, (await restarted.LoadLatestStateAsync(runId))!.Sequence);
    }

    [Fact]
    public async Task AppendPrepared_ReusesValidatedCanonicalBytes()
    {
        var commit = CreateCommit(Guid.NewGuid(), 1, null, 10);
        var prepared = PreparedRunCommit.CreateVerified(commit, commit.StateHash);
        using var store = new FileRunCommitStore(_directory, NullLogger.Instance);

        var appended = await store.AppendPreparedAsync(prepared);
        var path = Path.Combine(
            _directory,
            commit.RunId.ToString("D"),
            "commits",
            "00000001.json");
        var persisted = await File.ReadAllBytesAsync(path);

        Assert.False(appended.Duplicate);
        Assert.Equal(prepared.Bytes.ToArray(), persisted);
        Assert.Equal(commit.StateHash, (await store.LoadCommitAsync(commit.RunId, 1))!.StateHash);
    }

    [Fact]
    public async Task Append_RoundTripsLifecycleBoundarySetInCanonicalOrder()
    {
        var runId = Guid.NewGuid();
        var original = CreateCommit(runId, 1, null, 10);
        var combat = new CombatState
        {
            CombatId = Guid.NewGuid(),
            CompletedLifecycleBoundaries = ImmutableSortedSet.Create(
                StringComparer.Ordinal,
                "CombatEnd",
                "CombatStart")
        };
        var state = original.StateAfter with
        {
            Encounters = [new RunEncounterState { NodeId = "combat", Combat = combat }]
        };
        var commit = original with
        {
            StateAfter = state,
            StateHash = CanonicalJson.ComputeHash(state)
        };
        await usingScope(async store => await store.AppendAsync(commit));

        using var restarted = new FileRunCommitStore(_directory, NullLogger.Instance);
        var loaded = await restarted.LoadCommitAsync(runId, 1);

        Assert.NotNull(loaded);
        Assert.Equal(commit.StateHash, CanonicalJson.ComputeHash(loaded.StateAfter));
        Assert.Equal(
            new[] { "CombatEnd", "CombatStart" },
            loaded.StateAfter.Encounters[0].Combat.CompletedLifecycleBoundaries);
    }

    [Fact]
    public async Task Append_IdenticalRetryIsIdempotent()
    {
        using var store = new FileRunCommitStore(_directory, NullLogger.Instance);
        var commit = CreateCommit(Guid.NewGuid(), 1, null, 1);

        var first = await store.AppendAsync(commit);
        var retry = await store.AppendAsync(commit);

        Assert.False(first.Duplicate);
        Assert.True(retry.Duplicate);
        Assert.Single(await store.LoadCommitsAsync(commit.RunId));
    }

    [Fact]
    public async Task RangeRead_ReturnsOnlyRequestedSequences()
    {
        using var store = new FileRunCommitStore(_directory, NullLogger.Instance);
        var runId = Guid.NewGuid();
        var first = CreateCommit(runId, 1, null, 1);
        var second = CreateCommit(runId, 2, first.StateHash, 2);
        var third = CreateCommit(runId, 3, second.StateHash, 3);
        await store.AppendAsync(first);
        await store.AppendAsync(second);
        await store.AppendAsync(third);

        var page = await store.LoadCommitsAsync(runId, afterSequence: 1, limit: 1);

        Assert.Single(page);
        Assert.Equal(2, page[0].Sequence);
    }

    [Fact]
    public async Task Append_DifferentCommitAtSameSequenceFails()
    {
        using var store = new FileRunCommitStore(_directory, NullLogger.Instance);
        var runId = Guid.NewGuid();
        await store.AppendAsync(CreateCommit(runId, 1, null, 1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AppendAsync(CreateCommit(runId, 1, null, 2)));

        Assert.Contains("collision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Append_RequiresContiguousHashChain()
    {
        using var store = new FileRunCommitStore(_directory, NullLogger.Instance);
        var runId = Guid.NewGuid();
        var first = CreateCommit(runId, 1, null, 1);
        await store.AppendAsync(first);
        var second = CreateCommit(runId, 2, "wrong", 2);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AppendAsync(second));

        Assert.Contains("hash chain", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await store.LoadCommitsAsync(runId));
    }

    [Fact]
    public async Task Load_CorruptStateBytesFailFastAfterRestart()
    {
        var runId = Guid.NewGuid();
        var commit = CreateCommit(runId, 1, null, 1);
        await usingScope(async store => await store.AppendAsync(commit));
        var path = Path.Combine(
            _directory,
            runId.ToString("D"),
            "commits",
            "00000001.json");
        var json = await File.ReadAllTextAsync(path);
        var corruptHash = new string('0', 64);
        var corrupted = json.Replace(
            $"\"StateHash\":\"{commit.StateHash}\"",
            $"\"StateHash\":\"{corruptHash}\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, corrupted);
        await File.WriteAllTextAsync(path, corrupted);

        using var restarted = new FileRunCommitStore(_directory, NullLogger.Instance);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            restarted.LoadCommitAsync(runId, 1));

        Assert.Contains("stateHash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Append_RejectsUnknownSchemaAndEngine()
    {
        using var store = new FileRunCommitStore(_directory, NullLogger.Instance);
        var current = CreateCommit(Guid.NewGuid(), 1, null, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AppendAsync(current with { SchemaVersion = 99 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AppendAsync(current with { EngineVersion = "future" }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public async Task FindCommand_MatchesFullHistoryAcrossAppendEvictionAndRestart(int capacity)
    {
        var runId = Guid.NewGuid();
        var first = CreateCommit(runId, 1, null, 1);
        var second = CreateCommit(runId, 2, first.StateHash, 2);
        using (var store = new FileRunCommitStore(_directory, NullLogger.Instance, capacity))
        {
            await store.AppendAsync(first);
            Assert.Null(await store.FindCommandAsync(runId, second.RootCommand.CommandId));
            await store.AppendAsync(second);
            for (var i = 0; i < 2; i++)
            {
                Assert.Equal(first.StateHash, (await store.FindCommandAsync(runId, first.RootCommand.CommandId))!.StateHash);
                Assert.Equal(second.StateHash, (await store.FindCommandAsync(runId, second.RootCommand.CommandId))!.StateHash);
                Assert.Null(await store.FindCommandAsync(runId, Guid.NewGuid()));
            }
        }
        using var restarted = new FileRunCommitStore(_directory, NullLogger.Instance, capacity);
        Assert.Equal(second.StateHash, (await restarted.FindCommandAsync(runId, second.RootCommand.CommandId))!.StateHash);
        // Direct reads remain authoritative even though negative command lookups
        // can be answered by the complete derived index without scanning history.
        var path = Path.Combine(_directory, runId.ToString("D"), "commits", "00000001.json");
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace(first.StateHash, new string('0', first.StateHash.Length), StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.LoadCommitAsync(runId, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public async Task ValidationCache_RechecksChangedBytesEvenWithSameFileMetadata(int capacity)
    {
        using var store = new FileRunCommitStore(_directory, NullLogger.Instance, capacity);
        var commit = CreateCommit(Guid.NewGuid(), 1, null, 1);
        await store.AppendAsync(commit);
        var first = await store.LoadCommitAsync(commit.RunId, 1);
        var second = await store.LoadCommitAsync(commit.RunId, 1);
        Assert.NotSame(first, second);
        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(second));
        var path = Path.Combine(_directory, commit.RunId.ToString("D"), "commits", "00000001.json");
        var timestamp = File.GetLastWriteTimeUtc(path);
        var json = await File.ReadAllTextAsync(path);
        var corrupted = json.Replace(commit.StateHash, new string('0', commit.StateHash.Length), StringComparison.Ordinal);
        Assert.Equal(json.Length, corrupted.Length);
        await File.WriteAllTextAsync(path, corrupted);
        File.SetLastWriteTimeUtc(path, timestamp);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadCommitAsync(commit.RunId, 1));
    }

    [Fact]
    public async Task ConcurrentIdenticalRetriesCreateOneFile()
    {
        using var store = new FileRunCommitStore(_directory, NullLogger.Instance);
        var commit = CreateCommit(Guid.NewGuid(), 1, null, 1);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => store.AppendAsync(commit)));

        Assert.Single(results, result => !result.Duplicate);
        Assert.Equal(7, results.Count(result => result.Duplicate));
        Assert.Single(await store.ListCommitSequencesAsync(commit.RunId));
    }

    private async Task usingScope(Func<FileRunCommitStore, Task> action)
    {
        using var store = new FileRunCommitStore(_directory, NullLogger.Instance);
        await action(store);
    }

    private static RunCommit CreateCommit(
        Guid runId,
        int commitSequence,
        string? previousHash,
        int stateSequence)
    {
        var payload = JsonSerializer.SerializeToElement(new { value = stateSequence });
        var context = DeterministicContext.Create(123, "revision");
        for (var index = 0; index < commitSequence; index++)
            context = context.AdvanceStep();
        var state = new RunState
        {
            RunId = runId,
            Sequence = commitSequence,
            Lineage = RunLineage.Root(runId),
            Determinism = context,
            Metadata = new Dictionary<string, JsonElement>
            {
                ["value"] = JsonSerializer.SerializeToElement(stateSequence)
            }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
        };
        var hash = CanonicalJson.ComputeHash(state);
        var frames = new[]
        {
            new RunCommitFrame
            {
                FrameIndex = 0,
                Step = context.Step,
                Kind = "TEST",
                ResultHash = hash,
                Resolution = payload
            }
        };
        return new RunCommit
        {
            RunId = runId,
            Sequence = commitSequence,
            RootCommand = new RunCommandIdentity(
                DeterministicId.Create(123, (ulong)commitSequence, "test-command"),
                "TEST",
                commitSequence - 1,
                context.Step - 1,
                CanonicalJson.ComputeHash(payload)),
            Command = payload,
            PreviousStateHash = previousHash ?? string.Empty,
            StateHash = hash,
            BeforeStep = context.Step - 1,
            AfterStep = context.Step,
            LogicalTimestamp = context.LogicalTimestamp.UtcDateTime,
            StateAfter = state,
            Lineage = commitSequence == 1 ? state.Lineage : null,
            Frames = frames,
            Facts = RunCommitFacts.FromFrames(frames)
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
