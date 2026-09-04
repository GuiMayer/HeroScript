using Core.Combat.Models;
using Core.Common;
using Core.Math;
using Core.Resources;

namespace Core.Combat;

public interface IActionCostEvaluator
{
    Result<float> CalculateCost(ResourceCost cost, IReadOnlyDictionary<string, ResourcePool> resources, string? contentRevision = null);
    Result<bool> CanAfford(ResourceCost cost, IReadOnlyDictionary<string, ResourcePool> resources, string? contentRevision = null);
}

public sealed class ActionCostEvaluator : IActionCostEvaluator
{
    private readonly IRuntimeFormulaEvaluator _formulaEvaluator;

    public ActionCostEvaluator(IRuntimeFormulaEvaluator formulaEvaluator)
    {
        _formulaEvaluator = formulaEvaluator ?? throw new ArgumentNullException(nameof(formulaEvaluator));
    }

    public Result<float> CalculateCost(ResourceCost cost, IReadOnlyDictionary<string, ResourcePool> resources, string? contentRevision = null)
    {
        if (string.IsNullOrWhiteSpace(cost.Formula))
            return IsValidCost(cost.Amount)
                ? Result<float>.Success(cost.Amount)
                : Result<float>.Failure("Resource cost must be finite and non-negative");

        var variables = BuildVariables(resources);
        variables["amount"] = cost.Amount;
        variables["cost_amount"] = cost.Amount;

        var result = !string.IsNullOrWhiteSpace(contentRevision) &&
                     _formulaEvaluator is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(cost.Formula, contentRevision, variables)
            : _formulaEvaluator.Evaluate(cost.Formula, variables);
        if (result.IsFailure)
            return Result<float>.Failure(result.Error);

        return IsValidCost(result.Value)
            ? Result<float>.Success(result.Value)
            : Result<float>.Failure("Calculated resource cost must be finite and non-negative");
    }

    public Result<bool> CanAfford(ResourceCost cost, IReadOnlyDictionary<string, ResourcePool> resources, string? contentRevision = null)
    {
        if (!resources.TryGetValue(cost.ResourceId, out var pool))
            return Result<bool>.Failure($"Resource not found: {cost.ResourceId}");

        var amount = CalculateCost(cost, resources, contentRevision);
        if (amount.IsFailure)
            return Result<bool>.Failure(amount.Error);

        return Result<bool>.Success(cost.AllowOverdraft || pool.CanAfford(amount.Value));
    }

    private static Dictionary<string, float> BuildVariables(IReadOnlyDictionary<string, ResourcePool> resources)
    {
        var variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var (resourceId, pool) in resources)
        {
            variables[$"{resourceId}_current"] = pool.Current;
            variables[$"{resourceId}_max"] = pool.Maximum;
            variables[$"{resourceId}_min"] = pool.Minimum;
        }

        return variables;
    }

    private static bool IsValidCost(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
}
