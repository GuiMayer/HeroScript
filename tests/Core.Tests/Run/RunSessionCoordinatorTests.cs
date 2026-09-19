using System.Collections.Concurrent;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Core.Run.Branching;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunSessionCoordinatorTests
{
    private const string TestCommandType = "TEST_SET_VALUE";

    [Fact]
    public async Task DifferentRuns_CanAppendConcurrently()
    {
        var first = CreateState(Guid.NewGuid(), 11);
        var second = CreateState(Guid.NewGuid(), 22);
        var queries = new TestQueries(first, second);
        var store = new TestCommitStore(delayMilliseconds: 75);
        var coordinator = CreateCoordinator(queries, store);

        var results = await Task.WhenAll(
            coordinator.ExecuteAsync(first.RunId, Command(first, Guid.NewGuid(), 1)),
            coordinator.ExecuteAsync(second.RunId, Command(second, Guid.NewGuid(), 2)));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.True(store.MaximumConcurrentAppends >= 2);
    }

    [Fact]
    public async Task SameRun_IsSerializedAndRejectsStaleSecondCommand()
    {
        var state = CreateState(Guid.NewGuid(), 33);
        var queries = new TestQueries(state);
        var store = new TestCommitStore(delayMilliseconds: 50);
        var coordinator = CreateCoordinator(queries, store);

        var results = await Task.WhenAll(
            coordinator.ExecuteAsync(state.RunId, Command(state, Guid.NewGuid(), 1)),
            coordinator.ExecuteAsync(state.RunId, Command(state, Guid.NewGuid(), 2)));

        Assert.Single(results, result => result.IsSuccess);
        var failure = Assert.Single(results, result => result.IsFailure);
        Assert.StartsWith(RunCommandErrors.VersionConflictPrefix, failure.Error);
        Assert.Equal(1, store.MaximumConcurrentAppends);
        Assert.Equal(2, queries.GetRun(state.RunId).Value.Sequence);
    }

    [Fact]
    public async Task AppendFailure_DoesNotPublishCandidateOrReceipt()
    {
        var state = CreateState(Guid.NewGuid(), 44);
        var queries = new TestQueries(state);
        var store = new TestCommitStore { FailAppend = true };
        var coordinator = CreateCoordinator(queries, store);
        var command = Command(state, Guid.NewGuid(), 1);

        var result = await coordinator.ExecuteAsync(state.RunId, command);
        var receipt = coordinator.FindReceipt(state.RunId, command.Identity.CommandId);

        Assert.True(result.IsFailure);
        Assert.Equal(1, queries.GetRun(state.RunId).Value.Sequence);
        Assert.True(receipt.IsSuccess);
        Assert.Null(receipt.Value);
    }

    [Fact]
    public async Task RetryAfterCoordinatorRestart_ReturnsDurableReceipt()
    {
        var state = CreateState(Guid.NewGuid(), 55);
        var queries = new TestQueries(state);
        var store = new TestCommitStore();
        var command = Command(state, Guid.NewGuid(), 7);
        var first = CreateCoordinator(queries, store);

        var committed = await first.ExecuteAsync(state.RunId, command);
        var restarted = CreateCoordinator(queries, store);
        var retry = await restarted.ExecuteAsync(state.RunId, command);

        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error : null);
        Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error : null);
        Assert.True(retry.Value.Duplicate);
        Assert.Equal(committed.Value.StateHash, retry.Value.StateHash);
        Assert.Equal(1, store.AppendCount);
    }

    [Fact]
    public async Task NewCommands_QueryDurableReceiptsByCommandId()
    {
        var state = CreateState(Guid.NewGuid(), 66);
        var queries = new TestQueries(state);
        var store = new TestCommitStore();
        var coordinator = CreateCoordinator(queries, store);

        var first = await coordinator.ExecuteAsync(state.RunId, Command(state, Guid.NewGuid(), 1));
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        var current = queries.GetRun(state.RunId).Value;
        var second = await coordinator.ExecuteAsync(current.RunId, Command(current, Guid.NewGuid(), 2));

        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(2, store.FindCommandCount);
        Assert.Equal(0, store.LoadCommitsCount);
        Assert.Equal(2, store.AppendCount);
    }

    private static RunSessionCoordinator CreateCoordinator(TestQueries queries, TestCommitStore store)
    {
        var descriptor = new GameplayCommandDescriptor(
            TestCommandType,
            typeof(TestValueCommand),
            GameplayCommandRoute.Run);
        return new RunSessionCoordinator(
            queries,
            new GameplayCommandCodec([descriptor]),
            new RunCommandHandlerRegistry([new TestHandler(descriptor)]),
            queries.Publish,
            store,
            store);
    }

    private static GameplayCommandEnvelope Command(RunState state, Guid commandId, int count)
    {
        var payload = JsonSerializer.SerializeToElement(new TestValueCommand(count));
        return new GameplayCommandEnvelope(
            new RunCommandIdentity(
                commandId,
                TestCommandType,
                state.Sequence,
                state.Determinism.Step),
            payload);
    }

    private static RunState CreateState(Guid runId, ulong seed) => new()
    {
        RunId = runId,
        Sequence = 1,
        Lineage = RunLineage.Root(runId),
        Determinism = DeterministicContext.Create(seed, new string('a', 64)).AdvanceStep()
    };

    private sealed record TestValueCommand(int Count);

    private sealed class TestHandler(GameplayCommandDescriptor descriptor) : IRunCommandHandler<TestValueCommand>
    {
        public GameplayCommandDescriptor Descriptor { get; } = descriptor;

        public Result<RunTransitionPlan> Plan(
            RunState state,
            TestValueCommand payload,
            GameplayCommandExecutionContext context)
        {
            var candidate = state with
            {
                CurrentNodeId = payload.Count.ToString(),
                Determinism = context.Determinism.AdvanceStep()
            };
            return Result<RunTransitionPlan>.Success(new RunTransitionPlan
            {
                PreviousState = state,
                CandidateState = candidate,
                Context = candidate.Determinism,
                Value = payload.Count
            });
        }
    }

    private sealed class TestQueries(params RunState[] states) : IRunQueryService
    {
        private readonly ConcurrentDictionary<Guid, RunState> _states = new(
            states.ToDictionary(state => state.RunId));

        public void Publish(RunState state) => _states[state.RunId] = state;

        public Result<RunState> GetRun(Guid runId) =>
            _states.TryGetValue(runId, out var state)
                ? Result<RunState>.Success(state)
                : Result<RunState>.Failure($"Run not found: {runId}");

        public Result<RunState> GetRunByCombat(Guid combatId) =>
            Result<RunState>.Failure($"Run-owned combat not found: {combatId}");

        public Result<IReadOnlyList<RunAvailableCommand>> GetAvailableCommands(Guid runId) =>
            Result<IReadOnlyList<RunAvailableCommand>>.Success([]);
    }

    private sealed class TestCommitStore(int delayMilliseconds = 0) : IRunCommitStore
    {
        private readonly object _sync = new();
        private readonly List<RunCommit> _commits = [];
        private int _activeAppends;
        private int _maximumConcurrentAppends;
        private int _appendCount;
        private int _loadCommitsCount;
        private int _findCommandCount;

        public bool FailAppend { get; init; }
        public int MaximumConcurrentAppends => Volatile.Read(ref _maximumConcurrentAppends);
        public int AppendCount => Volatile.Read(ref _appendCount);
        public int LoadCommitsCount => Volatile.Read(ref _loadCommitsCount);
        public int FindCommandCount => Volatile.Read(ref _findCommandCount);

        public Task<RunCommit?> FindCommandAsync(
            Guid runId,
            Guid commandId,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _findCommandCount);
            return Task.FromResult(Snapshot().LastOrDefault(commit =>
                commit.RunId == runId && commit.RootCommand.CommandId == commandId));
        }

        public async Task<RunCommitAppendResult> AppendAsync(
            RunCommit commit,
            CancellationToken ct = default)
        {
            if (FailAppend)
                throw new IOException("simulated append failure");
            var active = Interlocked.Increment(ref _activeAppends);
            UpdateMaximum(active);
            try
            {
                if (delayMilliseconds > 0)
                    await Task.Delay(delayMilliseconds, ct);
                commit.Validate();
                lock (_sync)
                {
                    _commits.Add(commit);
                    Interlocked.Increment(ref _appendCount);
                }
                return new RunCommitAppendResult(commit, false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeAppends);
            }
        }

        public Task<RunCommit?> LoadCommitAsync(Guid runId, int sequence, CancellationToken ct = default) =>
            Task.FromResult(Snapshot().SingleOrDefault(commit =>
                commit.RunId == runId && commit.Sequence == sequence));

        public Task<IReadOnlyList<RunCommit>> LoadCommitsAsync(Guid runId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _loadCommitsCount);
            return Task.FromResult<IReadOnlyList<RunCommit>>(Snapshot()
                .Where(commit => commit.RunId == runId)
                .OrderBy(commit => commit.Sequence)
                .ToArray());
        }

        public async Task<RunState?> LoadStateAsync(Guid runId, int sequence, CancellationToken ct = default) =>
            (await LoadCommitAsync(runId, sequence, ct))?.StateAfter;

        public Task<RunState?> LoadLatestStateAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult(Snapshot()
                .Where(commit => commit.RunId == runId)
                .MaxBy(commit => commit.Sequence)?.StateAfter);

        public Task<IReadOnlyList<int>> ListCommitSequencesAsync(Guid runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<int>>(Snapshot()
                .Where(commit => commit.RunId == runId)
                .Select(commit => commit.Sequence)
                .OrderBy(sequence => sequence)
                .ToArray());

        public Task<IReadOnlyList<Guid>> ListRunIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(Snapshot()
                .Select(commit => commit.RunId)
                .Distinct()
                .OrderBy(runId => runId)
                .ToArray());

        public Task DeleteRunAsync(Guid runId, CancellationToken ct = default)
        {
            lock (_sync)
                _commits.RemoveAll(commit => commit.RunId == runId);
            return Task.CompletedTask;
        }

        private RunCommit[] Snapshot()
        {
            lock (_sync)
                return _commits.ToArray();
        }

        private void UpdateMaximum(int active)
        {
            while (true)
            {
                var maximum = Volatile.Read(ref _maximumConcurrentAppends);
                if (active <= maximum ||
                    Interlocked.CompareExchange(ref _maximumConcurrentAppends, active, maximum) == maximum)
                    return;
            }
        }
    }
}
