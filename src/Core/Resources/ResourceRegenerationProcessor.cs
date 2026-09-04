using Core.Common;
using Core.Math;

namespace Core.Resources;

/// <summary>
/// Resolves every matching regeneration rule against one immutable snapshot,
/// then applies the resulting mutations atomically through the resource reducer.
/// It does not publish events: aggregate owners publish only after their full
/// transaction commits.
/// </summary>
public sealed class ResourceRegenerationProcessor : IResourceRegenerationProcessor
{
    private readonly IRuntimeFormulaEvaluator _formulas;

    public ResourceRegenerationProcessor(IRuntimeFormulaEvaluator formulas)
    {
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
    }

    public Result<ResourceRegenerationResult> ProcessRegeneration(
        ResourceSet resourceState,
        RegenerationTiming timing,
        ResourceRegenerationContext? context = null)
    {
        if (resourceState == null)
            return Result<ResourceRegenerationResult>.Failure("ResourceSet cannot be null");

        context ??= new ResourceRegenerationContext();
        foreach (var (name, value) in context.Variables)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Result<ResourceRegenerationResult>.Failure("Regeneration context variable name is required");
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return Result<ResourceRegenerationResult>.Failure(
                    $"Regeneration context variable must be finite: {name}");
            }
        }
        var mutations = new List<ResolvedResourceMutation>();
        foreach (var (resourceId, pool) in resourceState.Resources
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (pool.Definition == null)
            {
                return Result<ResourceRegenerationResult>.Failure(
                    $"Resource pool has no pinned definition: {resourceState.OwnerId}/{resourceId}");
            }
            var regeneration = pool.Definition.Regeneration;
            if (regeneration is not { Enabled: true } || regeneration.Timing != timing)
                continue;

            var amount = ResolveAmount(resourceState, pool, regeneration, context);
            if (amount.IsFailure)
            {
                return Result<ResourceRegenerationResult>.Failure(
                    $"Regeneration failed for {resourceState.OwnerId}/{resourceId}: {amount.Error}");
            }
            mutations.Add(new ResolvedResourceMutation
            {
                MutationId = $"regeneration:{timing}:{resourceId}",
                ResourceId = resourceId,
                Operation = amount.Value >= 0
                    ? ResourceMutationOperation.Add
                    : ResourceMutationOperation.Subtract,
                Value = System.Math.Abs(amount.Value)
            });
        }

        var applied = resourceState.Apply(mutations);
        return applied.IsFailure
            ? Result<ResourceRegenerationResult>.Failure(applied.Error)
            : Result<ResourceRegenerationResult>.Success(new ResourceRegenerationResult(
                applied.Value.State,
                applied.Value.Records,
                timing));
    }

    private Result<float> ResolveAmount(
        ResourceSet resourceState,
        ResourcePool currentPool,
        RegenerationConfig regeneration,
        ResourceRegenerationContext context)
    {
        if (string.IsNullOrWhiteSpace(regeneration.Formula))
            return Result<float>.Success(regeneration.AmountPerTurn);

        var variables = BuildVariables(resourceState, currentPool, context.Variables);
        return !string.IsNullOrWhiteSpace(context.ContentRevision) &&
               _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(
                regeneration.Formula,
                context.ContentRevision,
                variables)
            : _formulas.Evaluate(regeneration.Formula, variables);
    }

    private static Dictionary<string, float> BuildVariables(
        ResourceSet resourceState,
        ResourcePool currentPool,
        IReadOnlyDictionary<string, float> external)
    {
        var variables = new Dictionary<string, float>(external, StringComparer.OrdinalIgnoreCase);
        ResourceFormulaVariables.AddUnscoped(variables, resourceState);

        // Local aliases are generic and always describe the pool whose rule is
        // being evaluated. They deliberately overwrite external values.
        variables["current"] = currentPool.Current;
        variables["minimum"] = currentPool.Minimum;
        variables["maximum"] = currentPool.Maximum;
        variables["percent"] = currentPool.GetPercentage();
        return variables;
    }
}
