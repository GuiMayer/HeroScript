using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.Common;

namespace Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationSelectionTiming { ActionStart, Current }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationEvaluationTiming { BeforeConsumption, AfterConsumption }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationOwnerBinding { TargetEntity, SourceEntity, Run, Explicit, Any }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationAbsencePolicy { Skip, Fail }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationConflictPolicy { Fail, Skip }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationZeroPolicy { Consume, Fail }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationAggregateKind { StackCount, Payload }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationScope { OncePerAction }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationConsumptionPolicy { AllSelectedStacks }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationChanceFailurePolicy { SkipWithoutConsumption }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationExhaustionPolicy { RemoveInstance }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CondensationRemovalHooksPolicy { None }

public sealed record CondensationAggregateDefinition
{
    public string ParameterId { get; init; } = string.Empty;
    public CondensationAggregateKind Kind { get; init; }
    public string? PayloadParameterId { get; init; }
}

/// <summary>Closed revisioned activation recipe. A selection produces one proc, never one proc per stack.</summary>
public sealed record CondensationRecipeDefinition
{
    public string RecipeId { get; init; } = string.Empty;
    public CondensationScope Scope { get; init; }
    public CondensationConsumptionPolicy Consumption { get; init; }
    public CondensationChanceFailurePolicy ChanceFailure { get; init; }
    public CondensationExhaustionPolicy Exhaustion { get; init; }
    public CondensationRemovalHooksPolicy RemovalHooks { get; init; }
    public AccumulatedStackSelection Selection { get; init; } = new();
    public CondensationOwnerBinding OwnerBinding { get; init; }
    public CondensationSelectionTiming SelectionTiming { get; init; }
    public CondensationEvaluationTiming EvaluationTiming { get; init; } = CondensationEvaluationTiming.AfterConsumption;
    public CondensationAbsencePolicy EmptySelection { get; init; }
    public CondensationConflictPolicy Conflict { get; init; }
    public CondensationZeroPolicy ZeroApplication { get; init; }
    public ImmutableArray<CondensationAggregateDefinition> Aggregates { get; init; } = [];
    public ImmutableArray<EffectDefinition> Effects { get; init; } = [];
}

public sealed record CondensationOutcome
{
    public StackConsumptionPlan Selection { get; init; } = new();
    public CondensationEvaluationTiming EvaluationTiming { get; init; }
    public ImmutableSortedDictionary<string, CalculationQuantity> Inputs { get; init; } =
        ImmutableSortedDictionary<string, CalculationQuantity>.Empty.WithComparers(StringComparer.Ordinal);
}

public static class CondensationRecipeValidator
{
    public static Result Validate(CondensationRecipeDefinition recipe)
    {
        if (string.IsNullOrWhiteSpace(recipe.RecipeId) || !Enum.IsDefined(recipe.Scope) ||
            !Enum.IsDefined(recipe.Consumption) || !Enum.IsDefined(recipe.ChanceFailure) ||
            !Enum.IsDefined(recipe.Exhaustion) || !Enum.IsDefined(recipe.RemovalHooks) ||
            !Enum.IsDefined(recipe.OwnerBinding) || !Enum.IsDefined(recipe.SelectionTiming) || !Enum.IsDefined(recipe.EvaluationTiming) ||
            !Enum.IsDefined(recipe.EmptySelection) || !Enum.IsDefined(recipe.Conflict) || !Enum.IsDefined(recipe.ZeroApplication) ||
            recipe.Selection.MaximumInstances is < 1 or > EffectExecutionLimits.MaximumSteps ||
            recipe.OwnerBinding == CondensationOwnerBinding.Explicit && recipe.Selection.Owner == null ||
            recipe.OwnerBinding != CondensationOwnerBinding.Explicit && recipe.Selection.Owner != null ||
            recipe.Effects.IsEmpty || recipe.Aggregates.Length > StackPayloadPolicies.MaximumParameters ||
            recipe.Aggregates.Select(item => item.ParameterId).Distinct(StringComparer.Ordinal).Count() != recipe.Aggregates.Length)
            return Result.Failure("Invalid condensation recipe policies, aggregates or activation");
        var selection = AccumulatedStackTransitions.ValidateSelection(recipe.Selection);
        if (selection.IsFailure) return selection;
        foreach (var aggregate in recipe.Aggregates)
            if (!StackPayloadPolicies.SafeId(aggregate.ParameterId) || !Enum.IsDefined(aggregate.Kind) ||
                aggregate.Kind == CondensationAggregateKind.Payload && !StackPayloadPolicies.SafeId(aggregate.PayloadParameterId) ||
                aggregate.Kind == CondensationAggregateKind.StackCount && aggregate.PayloadParameterId != null)
                return Result.Failure("Invalid condensation aggregate binding");
        var issues = new List<string>();
        issues.AddRange(EffectDefinitionValidator.Validate(recipe.Effects, (effect, _) =>
        {
            if (effect.Type == EffectType.CONDENSE_STACKS) issues.Add("Condensation recipes cannot recursively invoke condensation");
            foreach (var parameter in effect.Parameters.Where(parameter => parameter.InputQuantityId != null))
            {
                var aggregate = recipe.Aggregates.SingleOrDefault(aggregate => parameter.InputQuantityId == $"condensation.{aggregate.ParameterId}");
                if (aggregate == null)
                    issues.Add("Recipe effect references an unknown condensation input");
                else if (aggregate.Kind == CondensationAggregateKind.StackCount && parameter.UnitId != "stacks")
                    issues.Add("Stack count input requires stacks unit; no implicit unit conversion");
            }
        }));
        return issues.Count == 0 ? Result.Success() : Result.Failure(string.Join("; ", issues));
    }
}
