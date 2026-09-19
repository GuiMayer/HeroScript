using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.Determinism;

namespace Core.Run.Replay;

public sealed record RunReplayVerification(
    bool IsValid,
    RunState? FinalState,
    ImmutableArray<string> Errors);

/// <summary>
/// Reconstitui uma run a partir dos commits e valida sequência, cadeia de
/// hashes e progressão do passo lógico antes de aceitar o estado final.
/// </summary>
public static class RunReplayVerifier
{
    public static RunReplayVerification Verify(
        IEnumerable<RunCommit> commits)
    {
        var ordered = commits.OrderBy(item => item.Sequence).ToArray();
        var errors = ImmutableArray.CreateBuilder<string>();
        RunState? previousState = null;
        var previousHash = string.Empty;
        Core.Run.Branching.RunLineage? lineage = null;

        foreach (var commit in ordered)
        {
            var state = commit.RequireState();
            var hash = CanonicalJson.ComputeHash(state);

            try
            {
                commit.Validate();
            }
            catch (Exception exception)
            {
                errors.Add($"Invalid commit at sequence {state.Sequence}: {exception.Message}");
            }
            if (commit.RunId != state.RunId || commit.Sequence != state.Sequence)
                errors.Add($"Commit identity mismatch at sequence {state.Sequence}");
            if (previousState != null && state.Sequence != previousState.Sequence + 1)
                errors.Add($"Non-contiguous sequence {previousState.Sequence} -> {state.Sequence}");
            if (previousState != null && state.Determinism.Step < previousState.Determinism.Step)
                errors.Add($"Deterministic step regressed at sequence {state.Sequence}");
            if (previousState != null
                && !string.Equals(commit.PreviousStateHash, previousHash, StringComparison.Ordinal))
                errors.Add($"Previous hash mismatch at sequence {state.Sequence}");
            if (!string.Equals(commit.StateHash, hash, StringComparison.Ordinal))
                errors.Add($"State hash mismatch at sequence {state.Sequence}");
            lineage ??= commit.Lineage;
            if (lineage == null || state.Lineage != lineage)
                errors.Add($"Run lineage changed at sequence {state.Sequence}");

            previousState = state;
            previousHash = hash;
        }

        return new RunReplayVerification(
            errors.Count == 0,
            previousState,
            errors.ToImmutable());
    }
}
