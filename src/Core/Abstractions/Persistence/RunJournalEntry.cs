using System.Text.Json;

namespace Core.Abstractions.Persistence;

public sealed record RunJournalEntry
{
    public Guid RunId { get; init; }
    public int Sequence { get; init; }
    public ulong Step { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public JsonElement Command { get; init; }
    public string PreviousStateHash { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
    public DateTime LogicalTimestamp { get; init; } = DateTime.UnixEpoch;
}
