using System.Collections.Immutable;
using Core.Common;
using Core.Math;
using Core.Run.Content;

namespace Core.Calculations;

public interface ICalculationInfluenceProvider
{
    string ProviderId { get; }
    Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context);
}

public sealed class CompositeCalculationInfluenceProvider : ICalculationInfluenceProvider
{
    private readonly ImmutableArray<ICalculationInfluenceProvider> _providers;

    public CompositeCalculationInfluenceProvider(IEnumerable<ICalculationInfluenceProvider> providers)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers)))
            .OrderBy(provider => provider.ProviderId, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public string ProviderId => "composite";

    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        var influences = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var provider in _providers)
        {
            var result = provider.Collect(context);
            if (result.IsFailure)
                return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                    $"Influence provider {provider.ProviderId}: {result.Error}");
            influences.AddRange(result.Value);
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(influences
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.SourceKind)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal)
            .ThenBy(item => item.InfluenceId, StringComparer.Ordinal)
            .ToImmutableArray());
    }
}

public sealed class CardComponentInfluenceProvider : ICalculationInfluenceProvider
{
    private readonly IRuntimeFormulaEvaluator? _formulas;

    public CardComponentInfluenceProvider(IRuntimeFormulaEvaluator? formulas = null)
    {
        _formulas = formulas;
    }

    public string ProviderId => "card-components";

    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        if (context.Card == null)
            return Result<IReadOnlyList<CalculationInfluence>>.Success([]);
        var result = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var component in context.Card.All<CardInfluenceComponentDefinition>())
        {
            if (component.Value.HasValue == !string.IsNullOrWhiteSpace(component.Formula))
            {
                return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                    $"Card influence {component.ComponentId} must define exactly one of value or formula");
            }
            var value = component.Value;
            if (!value.HasValue)
            {
                if (_formulas == null)
                    return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                        $"Card influence {component.ComponentId} requires a formula evaluator");
                var variables = context.Variables.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
                var evaluated = !string.IsNullOrWhiteSpace(context.ContentRevision) &&
                                _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
                    ? revisioned.EvaluateAtRevision(component.Formula!, context.ContentRevision, variables)
                    : _formulas.Evaluate(component.Formula!, variables);
                if (evaluated.IsFailure)
                    return Result<IReadOnlyList<CalculationInfluence>>.Failure(evaluated.Error);
                value = evaluated.Value;
            }
            result.Add(new CalculationInfluence
            {
                InfluenceId = component.ComponentId,
                SourceKind = CalculationSourceKind.Card,
                SourceId = context.Card.CardInstanceId.ToString(),
                Channel = component.Channel,
                Bucket = component.Bucket,
                Value = value.Value,
                Priority = component.Priority
            });
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }
}

/// <summary>
/// Resource-to-pipeline mapping is explicit content. No resource name receives
/// an implicit gameplay meaning.
/// </summary>
public sealed class EntityResourceInfluenceProvider : ICalculationInfluenceProvider
{
    private readonly ImmutableArray<ResourceInfluenceBindingDefinition> _bindings;

    public EntityResourceInfluenceProvider(IEnumerable<ResourceInfluenceBindingDefinition> bindings)
    {
        _bindings = (bindings ?? throw new ArgumentNullException(nameof(bindings)))
            .OrderBy(binding => binding.BindingId, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public string ProviderId => "entity-resources";

    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        var result = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var binding in _bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.BindingId) ||
                string.IsNullOrWhiteSpace(binding.ResourceId) ||
                string.IsNullOrWhiteSpace(binding.Channel) ||
                string.IsNullOrWhiteSpace(binding.Bucket))
                return Result<IReadOnlyList<CalculationInfluence>>.Failure("Resource influence binding is incomplete");
            var entity = binding.Scope == CalculationEntityScope.Actor
                ? context.Actor
                : context.Target;
            var resource = entity?.GetResource(binding.ResourceId);
            if (resource == null)
                continue;
            result.Add(new CalculationInfluence
            {
                InfluenceId = binding.BindingId,
                SourceKind = binding.Scope == CalculationEntityScope.Actor
                    ? CalculationSourceKind.Actor
                    : CalculationSourceKind.Target,
                SourceId = $"{entity!.EntityId}:{binding.ResourceId}",
                Channel = binding.Channel,
                Bucket = binding.Bucket,
                Value = resource.Current * binding.Scale + binding.Offset,
                Priority = binding.Priority
            });
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }
}
