using System.Collections.Immutable;
using Core.Combat.Models;

namespace Core.Combat.Flow;

public sealed record CommandConditionDefinition
{
    public string RuleId { get; init; } = "";
    public ImmutableArray<ActionType> Commands { get; init; } = [];
    public string? ActionId { get; init; }
    public FlowActorScope ActorScope { get; init; }
    public ImmutableArray<GameplayQuery> Inputs { get; init; } = [];
    public string AllowCondition { get; init; } = "";
    public string FailureCode { get; init; } = "";
    public string MessageKey { get; init; } = "";
}
