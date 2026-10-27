using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Core.Combat.Flow;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ParticipantVictoryTieBreak { Unspecified, ActiveActorThenOrdinal, Ordinal, Draw, All }
public sealed record ParticipantVictoryPolicy
{
    public string RuleId { get; init; } = "";
    public ImmutableArray<GameplayQuery> Inputs { get; init; } = [];
    public string Condition { get; init; } = "";
    public ParticipantVictoryTieBreak TieBreak { get; init; }
}
public sealed record ParticipantVictoryResult
{
    public string RuleId { get; init; } = "";
    public ImmutableArray<string> ActorIds { get; init; } = [];
    public ImmutableArray<string> SideIds { get; init; } = [];
    public bool Draw { get; init; }
}
