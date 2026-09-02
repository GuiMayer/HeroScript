using System.Collections.Immutable;
using Core.Determinism;

namespace Core.Abstractions.Persistence;

/// <summary>
/// Atomic persistence unit for one root command and all deterministic internal
/// transitions it caused.
/// </summary>
public sealed record RunCheckpointBatch
{
    private ImmutableArray<RunCheckpoint> _checkpoints = [];

    public Guid RunId { get; init; }
    public Guid? RootCommandId { get; init; }
    public IReadOnlyList<RunCheckpoint> Checkpoints
    {
        get => _checkpoints;
        init => _checkpoints = value?.ToImmutableArray() ?? [];
    }

    public static RunCheckpointBatch Single(RunCheckpoint checkpoint) => new()
    {
        RunId = checkpoint.State.RunId,
        RootCommandId = checkpoint.JournalEntry.CommandId,
        Checkpoints = [checkpoint]
    };

    public void Validate()
    {
        if (RunId == Guid.Empty)
            throw new InvalidOperationException("Checkpoint batch runId is required");
        if (_checkpoints.IsEmpty)
            throw new InvalidOperationException("Checkpoint batch cannot be empty");

        RunCheckpoint? previous = null;
        foreach (var checkpoint in _checkpoints)
        {
            if (checkpoint.State.RunId != RunId || checkpoint.JournalEntry.RunId != RunId)
                throw new InvalidOperationException("Every checkpoint must belong to the batch run");
            if (checkpoint.State.Sequence != checkpoint.JournalEntry.Sequence)
                throw new InvalidOperationException("Checkpoint state and journal sequences must match");
            if (!string.Equals(
                    CanonicalJson.ComputeHash(checkpoint.State),
                    checkpoint.JournalEntry.StateHash,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Checkpoint state hash does not match its state");
            if (previous != null)
            {
                if (checkpoint.State.Sequence != previous.State.Sequence + 1)
                    throw new InvalidOperationException("Checkpoint batch sequences must be contiguous");
                if (!string.Equals(
                        checkpoint.JournalEntry.PreviousStateHash,
                        previous.JournalEntry.StateHash,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("Checkpoint batch hash chain is not contiguous");
            }
            previous = checkpoint;
        }
    }
}
