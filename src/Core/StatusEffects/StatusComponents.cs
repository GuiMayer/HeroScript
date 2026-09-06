using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Math;

namespace Core.StatusEffects;

public sealed record ActionConstraintDefinition
{
    public string ConstraintId { get; init; } = string.Empty;
    public ImmutableArray<string> RequiredActionTags { get; init; } = [];
    public ImmutableArray<string> ExcludedActionTags { get; init; } = [];
    public string? Condition { get; init; }
    public string Reason { get; init; } = "Action denied by status";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DispelOrder { InstanceId, OldestFirst, NewestFirst, HighestPriorityFirst }

public sealed record StatusDispelDefinition
{
    public ImmutableArray<string> StatusIds { get; init; } = [];
    public ImmutableArray<string> RequiredTags { get; init; } = [];
    public string? SourceEntityId { get; init; }
    public int MaximumInstances { get; init; } = int.MaxValue;
    public DispelOrder Order { get; init; }
}

public static class StatusActionConstraints
{
    public static Result<ImmutableArray<string>> Evaluate(CombatState combat, CombatEntity actor,
        IReadOnlySet<string> actionTags, IRuntimeFormulaEvaluator? formulas, string revision)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        foreach (var status in combat.StatusEffects.GetValueOrDefault(actor.EntityId, [])
                     .Where(item => item.IsActive).OrderBy(item => item.InstanceId))
        foreach (var constraint in status.Definition.ActionConstraints.OrderBy(item => item.ConstraintId, StringComparer.Ordinal))
        {
            if (!constraint.RequiredActionTags.All(actionTags.Contains) || constraint.ExcludedActionTags.Any(actionTags.Contains)) continue;
            if (!string.IsNullOrWhiteSpace(constraint.Condition))
            {
                if (formulas == null) return Result<ImmutableArray<string>>.Failure("Constraint formula evaluator is unavailable");
                var variables = GameplayFormulaContext.Build(combat.GetEntity(status.SourceId ?? actor.EntityId) ?? actor, actor, actor);
                variables["stacks"] = status.Stacks;
                variables["duration"] = status.Duration;
                var result = GameplayFormulaContext.Evaluate(formulas, constraint.Condition, status.ContentRevision ?? revision, variables);
                if (result.IsFailure) return Result<ImmutableArray<string>>.Failure(result.Error);
                if (result.Value <= 0) continue;
            }
            failures.Add($"{status.StatusId}/{constraint.ConstraintId}: {constraint.Reason}");
        }
        return Result<ImmutableArray<string>>.Success(failures.ToImmutable());
    }
}
