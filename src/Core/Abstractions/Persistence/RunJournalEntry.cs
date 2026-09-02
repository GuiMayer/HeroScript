using System.Text.Json;

namespace Core.Abstractions.Persistence;

public sealed record RunJournalEntry
{
    public Guid RunId { get; init; }
    public Guid? CommandId { get; init; }
    public Guid? RootCommandId { get; init; }
    public Guid? CausationId { get; init; }
    public int TransitionIndex { get; init; }
    public int TransitionCount { get; init; } = 1;
    public int Sequence { get; init; }
    public ulong Step { get; init; }
    public int? ExpectedSequence { get; init; }
    public ulong? ExpectedStep { get; init; }
    public string CommandPayloadHash { get; init; } = string.Empty;
    public string CommandType { get; init; } = string.Empty;
    public JsonElement Command { get; init; }
    public string PreviousStateHash { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
    public DateTime LogicalTimestamp { get; init; } = DateTime.UnixEpoch;
}
