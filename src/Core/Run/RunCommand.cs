using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Common;

namespace Core.Run;

/// <summary>
/// Identifies a client command and the aggregate version it was based on.
/// The identity is persisted atomically with the state transition.
/// </summary>
public sealed record RunCommandIdentity(
    Guid CommandId,
    string Type,
    int ExpectedSequence,
    ulong ExpectedStep,
    string PayloadHash = "");

public sealed record RunCommandReceipt
{
    private ImmutableArray<RunCommitFrame> _frames = [];

    public Guid CommandId { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public ulong Step { get; init; }
    public string PreviousStateHash { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
    public RunState State { get; init; } = new();
    public RunJournalEntry JournalEntry { get; init; } = new();
    public IReadOnlyList<RunCommitFrame> Frames
    {
        get => _frames;
        init => _frames = value?.ToImmutableArray() ?? [];
    }
    public bool Duplicate { get; init; }
}

public interface IRunCommandProcessor
{
    Result<RunCommandReceipt?> FindReceipt(Guid runId, Guid commandId);
    Result<RunCommandReceipt> Execute(Guid runId, GameplayCommandEnvelope command);
}

public static class RunCommandErrors
{
    public const string VersionConflictPrefix = "VERSION_CONFLICT:";

    public static string VersionConflict(int expectedSequence, ulong expectedStep, RunState current) =>
        $"{VersionConflictPrefix} Expected sequence/step {expectedSequence}/{expectedStep}, " +
        $"current is {current.Sequence}/{current.Determinism.Step}";
}
