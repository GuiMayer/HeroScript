using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.Determinism;

namespace Core.Run.Replay;

public sealed record RunReplayVerification(
    bool IsValid,
    RunState? FinalState,
    ImmutableArray<string> Errors);

/// <summary>
/// Reconstitui uma run a partir dos checkpoints e valida sequência, cadeia de
/// hashes e progressão do passo lógico antes de aceitar o estado final.
/// </summary>
public static class RunReplayVerifier
{
    public static RunReplayVerification Verify(
        IEnumerable<RunCheckpoint> checkpoints)
    {
        var ordered = checkpoints.OrderBy(item => item.State.Sequence).ToArray();
        var errors = ImmutableArray.CreateBuilder<string>();
        RunState? previousState = null;
        var previousHash = string.Empty;

        foreach (var checkpoint in ordered)
        {
            var state = checkpoint.State;
            var entry = checkpoint.JournalEntry;
            var hash = CanonicalJson.ComputeHash(state);

            if (entry.RunId != state.RunId || entry.Sequence != state.Sequence)
                errors.Add($"Checkpoint identity mismatch at sequence {state.Sequence}");
            if (previousState != null && state.Sequence != previousState.Sequence + 1)
                errors.Add($"Non-contiguous sequence {previousState.Sequence} -> {state.Sequence}");
            if (previousState != null && state.Determinism.Step < previousState.Determinism.Step)
                errors.Add($"Deterministic step regressed at sequence {state.Sequence}");
            if (previousState != null
                && !string.Equals(entry.PreviousStateHash, previousHash, StringComparison.Ordinal))
                errors.Add($"Previous hash mismatch at sequence {state.Sequence}");
            if (!string.Equals(entry.StateHash, hash, StringComparison.Ordinal))
                errors.Add($"State hash mismatch at sequence {state.Sequence}");

            previousState = state;
            previousHash = hash;
        }

        return new RunReplayVerification(
            errors.Count == 0,
            previousState,
            errors.ToImmutable());
    }
}
