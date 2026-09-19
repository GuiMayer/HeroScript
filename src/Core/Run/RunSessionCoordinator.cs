using System.Collections.Concurrent;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Common;
using Core.Determinism;

namespace Core.Run;

public sealed class RunSessionGateProvider
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public IDisposable Enter(Guid runId, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(runId, static _ => new SemaphoreSlim(1, 1));
        gate.Wait(cancellationToken);
        return new Lease(gate);
    }

    public async ValueTask<IDisposable> EnterAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(runId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

/// <summary>
/// Application-level transaction boundary for run commands. Domain handlers
/// only calculate immutable plans; this coordinator exclusively owns
/// per-aggregate serialization, idempotency, durable append and state publish.
/// </summary>
public sealed class RunSessionCoordinator : IRunCommandProcessor
{
    private readonly IRunQueryService _queries;
    private readonly IRunCommitStore? _store;
    private readonly IRunCommitReader? _history;
    private readonly IRunCommandReceiptReader? _receiptFallback;
    private readonly IGameplayCommandCodec _codec;
    private readonly RunCommandHandlerRegistry _handlers;
    private readonly Action<RunState> _publish;
    private readonly RunSessionGateProvider _gates;
    private readonly ConcurrentDictionary<(Guid RunId, Guid CommandId), RunCommandReceipt> _receipts = new();
    private readonly ConcurrentDictionary<Guid, byte> _indexedReceiptRuns = new();

    public RunSessionCoordinator(
        IRunQueryService queries,
        IGameplayCommandCodec codec,
        RunCommandHandlerRegistry handlers,
        Action<RunState> publish,
        IRunCommitStore? store = null,
        IRunCommitReader? history = null,
        IRunCommandReceiptReader? receiptFallback = null,
        RunSessionGateProvider? gates = null)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _store = store;
        _history = history ?? store;
        _receiptFallback = receiptFallback;
        _gates = gates ?? new RunSessionGateProvider();
    }

    public Result<RunCommandReceipt?> FindReceipt(Guid runId, Guid commandId) =>
        FindReceiptAsync(runId, commandId).ConfigureAwait(false).GetAwaiter().GetResult();

    public Result<RunCommandReceipt> Execute(Guid runId, GameplayCommandEnvelope command) =>
        ExecuteAsync(runId, command).ConfigureAwait(false).GetAwaiter().GetResult();

    public async Task<Result<RunCommandReceipt>> ExecuteAsync(
        Guid runId,
        GameplayCommandEnvelope command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var decoded = _codec.Decode(command);
        if (decoded.IsFailure)
            return Result<RunCommandReceipt>.Failure(decoded.Error);
        if (decoded.Value.Descriptor.Route != GameplayCommandRoute.Run)
        {
            return Result<RunCommandReceipt>.Failure(
                $"Command {decoded.Value.Envelope.Identity.Type} is not a run-only command");
        }

        using var lease = await _gates.EnterAsync(runId, cancellationToken).ConfigureAwait(false);
        try
        {
            var identity = decoded.Value.Envelope.Identity;
            var existing = await FindReceiptCoreAsync(runId, identity.CommandId, cancellationToken)
                .ConfigureAwait(false);
            if (existing.IsFailure)
                return Result<RunCommandReceipt>.Failure(existing.Error);
            if (existing.Value is { } receipt)
            {
                return IsSameCommand(receipt.JournalEntry, decoded.Value.Envelope)
                    ? Result<RunCommandReceipt>.Success(receipt with { Duplicate = true })
                    : Result<RunCommandReceipt>.Failure(
                        $"Command id {identity.CommandId} was already used with a different envelope");
            }

            var loaded = _queries.GetRun(runId);
            if (loaded.IsFailure)
                return Result<RunCommandReceipt>.Failure(loaded.Error);
            var state = loaded.Value;
            if (state.Sequence != identity.ExpectedSequence ||
                state.Determinism.Step != identity.ExpectedStep)
            {
                return Result<RunCommandReceipt>.Failure(
                    RunCommandErrors.VersionConflict(identity.ExpectedSequence, identity.ExpectedStep, state));
            }

            var handler = _handlers.Resolve(identity.Type);
            if (handler.IsFailure)
                return Result<RunCommandReceipt>.Failure(handler.Error);
            var plan = handler.Value.Plan(
                state,
                decoded.Value.Payload,
                new GameplayCommandExecutionContext(identity, state.Determinism));
            if (plan.IsFailure)
                return Result<RunCommandReceipt>.Failure(plan.Error);

            return await CommitAsync(decoded.Value.Envelope, plan.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<RunCommandReceipt>.Failure("Run command was cancelled");
        }
        catch (Exception exception)
        {
            return Result<RunCommandReceipt>.Failure(
                $"Failed to execute run command '{command.Identity.Type}': {exception.Message}",
                exception);
        }
    }

    private async Task<Result<RunCommandReceipt>> CommitAsync(
        GameplayCommandEnvelope command,
        RunTransitionPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.PreviousState.RunId != plan.CandidateState.RunId)
            return Result<RunCommandReceipt>.Failure("A run transition cannot replace its aggregate identity");
        if (plan.PreviousState.Sequence != command.Identity.ExpectedSequence ||
            plan.PreviousState.Determinism.Step != command.Identity.ExpectedStep)
            return Result<RunCommandReceipt>.Failure("Run transition plan was produced from a stale snapshot");
        if (plan.Context != plan.CandidateState.Determinism)
            return Result<RunCommandReceipt>.Failure("Run transition plan context must match its candidate state");

        var nextSequence = checked(plan.PreviousState.Sequence + 1);
        var stateAfter = Snapshot(plan.CandidateState with
        {
            Sequence = nextSequence,
            Lineage = plan.PreviousState.Lineage
        });
        var payload = command.Payload.Clone();
        var identity = command.Identity with
        {
            PayloadHash = CanonicalJson.ComputeHash(payload)
        };
        var frames = plan.Frames.Count == 0
            ? new[]
            {
                new RunCommitFrame
                {
                    FrameIndex = 0,
                    Step = stateAfter.Determinism.Step,
                    Scope = "run",
                    Kind = identity.Type,
                    ResultHash = CanonicalJson.ComputeHash(stateAfter),
                    Resolution = payload
                }
            }
            : plan.Frames.Select(frame => new RunCommitFrame
            {
                FrameIndex = frame.FrameIndex,
                Step = frame.Step,
                Scope = frame.Scope,
                Kind = frame.Kind,
                ResultHash = CanonicalJson.ComputeHash(new
                {
                    frame.FrameIndex,
                    frame.Step,
                    frame.Scope,
                    frame.Kind,
                    frame.Resolution
                }),
                Resolution = frame.Resolution.Clone()
            }).ToArray();
        var facts = plan.Facts.Count == 0
            ? RunCommitFacts.FromFrames(frames)
            : plan.Facts.Select(fact => new RunCommitFact
            {
                FactIndex = fact.FactIndex,
                Step = fact.Step,
                Scope = fact.Scope,
                Type = fact.Type,
                Payload = fact.Payload.Clone()
            }).ToArray();
        var commit = new RunCommit
        {
            RunId = stateAfter.RunId,
            Sequence = nextSequence,
            RootCommand = identity,
            Command = payload,
            PreviousStateHash = CanonicalJson.ComputeHash(plan.PreviousState),
            StateHash = CanonicalJson.ComputeHash(stateAfter),
            BeforeStep = plan.PreviousState.Determinism.Step,
            AfterStep = stateAfter.Determinism.Step,
            LogicalTimestamp = stateAfter.Determinism.LogicalTimestamp.UtcDateTime,
            StateAfter = stateAfter,
            Frames = frames,
            Facts = facts
        };

        if (_store != null)
            await _store.AppendAsync(commit, cancellationToken).ConfigureAwait(false);

        // Publication is deliberately after the durable append. If append
        // fails, neither the live aggregate nor the receipt cache changes.
        _publish(stateAfter);
        var receipt = CreateReceipt(commit, duplicate: false);
        _receipts[(stateAfter.RunId, identity.CommandId)] = receipt;
        return Result<RunCommandReceipt>.Success(receipt);
    }

    private async Task<Result<RunCommandReceipt?>> FindReceiptAsync(
        Guid runId,
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        if (commandId == Guid.Empty)
            return Result<RunCommandReceipt?>.Failure("Command id is required");
        return await FindReceiptCoreAsync(runId, commandId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<RunCommandReceipt?>> FindReceiptCoreAsync(
        Guid runId,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        if (_receipts.TryGetValue((runId, commandId), out var cached))
            return Result<RunCommandReceipt?>.Success(cached with { Duplicate = true });
        if (_history == null)
            return _receiptFallback?.FindReceipt(runId, commandId)
                ?? Result<RunCommandReceipt?>.Success(null);

        try
        {
            if (!_indexedReceiptRuns.ContainsKey(runId))
            {
                var commits = await _history.LoadCommitsAsync(runId, cancellationToken).ConfigureAwait(false);
                foreach (var commit in commits)
                {
                    var receipt = CreateReceipt(commit, duplicate: false);
                    _receipts[(runId, commit.RootCommand.CommandId)] = receipt;
                }
                _indexedReceiptRuns.TryAdd(runId, 0);
            }

            if (_receipts.TryGetValue((runId, commandId), out cached))
                return Result<RunCommandReceipt?>.Success(cached with { Duplicate = true });
            return _receiptFallback?.FindReceipt(runId, commandId)
                ?? Result<RunCommandReceipt?>.Success(null);
        }
        catch (Exception exception)
        {
            return Result<RunCommandReceipt?>.Failure(
                $"Failed to read command receipt {commandId}: {exception.Message}",
                exception);
        }
    }

    private static RunCommandReceipt CreateReceipt(RunCommit commit, bool duplicate)
    {
        var entry = commit.ToJournalEntry();
        return new RunCommandReceipt
        {
            CommandId = commit.RootCommand.CommandId,
            CommandType = entry.CommandType,
            Sequence = entry.Sequence,
            Step = entry.Step,
            PreviousStateHash = entry.PreviousStateHash,
            StateHash = entry.StateHash,
            State = commit.StateAfter,
            CombatResolution = Projections.CombatResolutionProjection.FromCommit(commit),
            JournalEntry = entry,
            Frames = commit.Frames,
            Duplicate = duplicate
        };
    }

    private static bool IsSameCommand(RunJournalEntry entry, GameplayCommandEnvelope command) =>
        string.Equals(entry.CommandType, command.Identity.Type, StringComparison.Ordinal) &&
        entry.ExpectedSequence == command.Identity.ExpectedSequence &&
        entry.ExpectedStep == command.Identity.ExpectedStep &&
        string.Equals(
            string.IsNullOrWhiteSpace(entry.CommandPayloadHash)
                ? CanonicalJson.ComputeHash(entry.Command)
                : entry.CommandPayloadHash,
            string.IsNullOrWhiteSpace(command.Identity.PayloadHash)
                ? CanonicalJson.ComputeHash(command.Payload)
                : command.Identity.PayloadHash,
            StringComparison.Ordinal);

    private static RunState Snapshot(RunState state)
    {
        var json = JsonSerializer.Serialize(state);
        return JsonSerializer.Deserialize<RunState>(json)
            ?? throw new InvalidOperationException("Failed to create a run-state snapshot");
    }
}
