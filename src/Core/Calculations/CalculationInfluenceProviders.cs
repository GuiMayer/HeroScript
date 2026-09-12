using System.Collections.Immutable;
using Core.Common;
using Core.Math;
using Core.Run.Content;
using Core.Run;
using Core.Combat.Modifiers;
using Core.Combat.Models;
using Core.StatusEffects;
using Core.Resources;

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
            if (!component.RequiredTags.All(context.Tags.Contains) || component.ExcludedTags.Any(context.Tags.Contains))
                continue;
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
                Priority = component.Priority,
                OrderKey = component.ComponentId
            });
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }
}

public sealed class GameModeCalculationInfluenceProvider(IRuntimeFormulaEvaluator formulas) : ICalculationInfluenceProvider
{
    public string ProviderId => "game-mode";
    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        var mode = context.Run?.ResolvedMode?.Definition;
        return mode == null ? Result<IReadOnlyList<CalculationInfluence>>.Success([]) :
            ContextualInfluencePolicies.Resolve(mode.Influences, CalculationSourceKind.GameMode,
                mode.ModeId, context, formulas);
    }
}

public sealed class EncounterCalculationInfluenceProvider(IRuntimeFormulaEvaluator formulas) : ICalculationInfluenceProvider
{
    public string ProviderId => "encounter";
    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        var scenario = context.Run?.Scenario;
        return scenario == null ? Result<IReadOnlyList<CalculationInfluence>>.Success([]) :
            ContextualInfluencePolicies.Resolve(scenario.Influences, CalculationSourceKind.Encounter,
                context.Run!.ScenarioHash ?? scenario.AttemptKey, context, formulas);
    }
}

/// <summary>
/// Resource-to-pipeline mapping is explicit content. No resource name receives
/// an implicit gameplay meaning.
/// </summary>
public sealed class EntityResourceInfluenceProvider : ICalculationInfluenceProvider
{
    public string ProviderId => "entity-resources";

    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        var result = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var binding in (context.Pipeline?.ResourceInfluenceBindings ?? [])
                     .OrderBy(item => item.BindingId, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(binding.BindingId) ||
                string.IsNullOrWhiteSpace(binding.ResourceId) ||
                string.IsNullOrWhiteSpace(binding.Channel) ||
                string.IsNullOrWhiteSpace(binding.Bucket))
                return Result<IReadOnlyList<CalculationInfluence>>.Failure("Resource influence binding is incomplete");
            if (!Enum.IsDefined(binding.Scope) || !Enum.IsDefined(binding.Field) || !Enum.IsDefined(binding.MissingResource))
            {
                return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                    $"Resource influence binding {binding.BindingId} has an invalid scope or field");
            }
            if (!binding.RequiredTags.All(context.Tags.Contains) || binding.ExcludedTags.Any(context.Tags.Contains))
                continue;
            var entity = binding.Scope == CalculationEntityScope.Actor
                ? context.Actor
                : context.Target;
            var resource = entity?.GetResource(binding.ResourceId);
            if (entity == null)
                return Result<IReadOnlyList<CalculationInfluence>>.Failure($"Binding {binding.BindingId} has no scoped entity");
            if (resource == null && binding.MissingResource == MissingResourcePolicy.Error)
                return Result<IReadOnlyList<CalculationInfluence>>.Failure($"Binding {binding.BindingId}: missing resource {binding.ResourceId}");
            if (resource == null && binding.MissingResource == MissingResourcePolicy.Ignore)
                continue;
            var sourceValue = resource == null ? 0 : binding.Field switch
            {
                ResourceValueField.Current => resource.Current,
                ResourceValueField.Minimum => resource.Minimum,
                ResourceValueField.Maximum => resource.Maximum,
                _ => throw new InvalidOperationException(
                    $"Unsupported resource value field: {binding.Field}")
            };
            result.Add(new CalculationInfluence
            {
                InfluenceId = binding.BindingId,
                SourceKind = binding.Scope == CalculationEntityScope.Actor
                    ? CalculationSourceKind.Actor
                    : CalculationSourceKind.Target,
                SourceId = $"{entity!.InstanceId}:{binding.ResourceId}",
                Channel = binding.Channel,
                Bucket = binding.Bucket,
                Value = sourceValue * binding.Scale + binding.Offset,
                Priority = binding.Priority,
                OrderKey = binding.BindingId
            });
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }
}

/// <summary>
/// Projects explicitly bound numeric stat component values. Stat IDs have no
/// implicit gameplay meaning; a pinned pipeline opts into every value it uses.
/// </summary>
public sealed class EntityStatInfluenceProvider : ICalculationInfluenceProvider
{
    public string ProviderId => "entity-stats";

    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        var result = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var binding in (context.Pipeline?.StatInfluenceBindings ?? [])
                     .OrderBy(item => item.BindingId, StringComparer.Ordinal))
        {
            if (!binding.RequiredTags.All(context.Tags.Contains) || binding.ExcludedTags.Any(context.Tags.Contains))
                continue;
            var entity = binding.Scope == CalculationEntityScope.Actor ? context.Actor : context.Target;
            if (entity == null)
                return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                    $"Stat binding {binding.BindingId} has no scoped entity");
            var component = entity.Component<StatEntityComponentState>(binding.ComponentId);
            var raw = 0f;
            var found = component?.Values.TryGetValue(binding.ValueId, out raw) == true;
            if (!found && binding.MissingValue == MissingResourcePolicy.Error)
                return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                    $"Stat binding {binding.BindingId}: missing value {binding.ComponentId}.{binding.ValueId}");
            if (!found && binding.MissingValue == MissingResourcePolicy.Ignore)
                continue;
            result.Add(new CalculationInfluence
            {
                InfluenceId = binding.BindingId,
                SourceKind = binding.Scope == CalculationEntityScope.Actor
                    ? CalculationSourceKind.Actor
                    : CalculationSourceKind.Target,
                SourceId = $"{entity.InstanceId}:{binding.ComponentId}:{binding.ValueId}",
                Channel = binding.Channel,
                Bucket = binding.Bucket,
                Value = (found ? raw : 0) * binding.Scale + binding.Offset,
                Priority = binding.Priority,
                OrderKey = binding.BindingId
            });
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }
}

/// <summary>
/// Reads modifier instances exclusively from the immutable run snapshot. The
/// definition pinned in each instance is used, so hot reload cannot rewrite an
/// already-running timeline.
/// </summary>
public sealed class RunModifierInfluenceProvider : ICalculationInfluenceProvider
{
    private readonly IRuntimeFormulaEvaluator _formulas;

    public RunModifierInfluenceProvider(IRuntimeFormulaEvaluator formulas) =>
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));

    public string ProviderId => "run-modifiers";

    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        if (context.Run == null)
            return Result<IReadOnlyList<CalculationInfluence>>.Success([]);

        var result = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var modifier in context.Run.Modifiers
                     .Where(item => item.IsActive)
                     .OrderBy(item => item.InstanceId))
        {
            foreach (var definition in modifier.Definition.Influences
                         .OrderByDescending(item => item.Priority)
                         .ThenBy(item => item.InfluenceId, StringComparer.Ordinal))
            {
                var validation = Validate(definition);
                if (validation != null)
                    return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                        $"Modifier {modifier.ModifierId}: {validation}");
                if (!AppliesToOwner(modifier, definition, context) ||
                    !TagsMatch(definition, context.Tags))
                    continue;

                var value = ResolveValue(modifier, definition, context);
                if (value.IsFailure)
                    return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                        $"Modifier {modifier.ModifierId}/{definition.InfluenceId}: {value.Error}");
                result.Add(new CalculationInfluence
                {
                    InfluenceId = definition.InfluenceId,
                    SourceKind = CalculationSourceKind.Modifier,
                    SourceId = modifier.InstanceId.ToString(),
                    Channel = definition.Channel,
                    Bucket = definition.Bucket,
                    Value = value.Value,
                    Priority = definition.Priority
                });
            }
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }

    private Result<float> ResolveValue(
        ScriptModifierInstance modifier,
        ContextualInfluenceDefinition definition,
        CalculationSourceContext context)
    {
        if (definition.Value.HasValue)
            return Result<float>.Success(definition.Value.Value * modifier.Stacks);
        var variables = context.Variables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        variables["stacks"] = modifier.Stacks;
        variables["duration"] = modifier.Duration;
        var owner = context.Combat?.GetAllActors().OrderBy(entity => entity.InstanceId, StringComparer.Ordinal)
            .FirstOrDefault(entity => modifier.Owner.Includes(entity, context.Combat, context.Run));
        if (owner != null) ResourceFormulaVariables.AddOwner(variables, "owner", owner.ResourceState);
        return !string.IsNullOrWhiteSpace(modifier.ContentRevision) &&
               _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(definition.Formula!, modifier.ContentRevision, variables)
            : _formulas.Evaluate(definition.Formula!, variables);
    }

    private static bool AppliesToOwner(
        ScriptModifierInstance modifier,
        ContextualInfluenceDefinition definition,
        CalculationSourceContext context)
    {
        var scopedEntity = definition.Scope == CalculationEntityScope.Actor
            ? context.Actor
            : context.Target;
        return scopedEntity != null && context.Combat != null && modifier.Owner.Includes(scopedEntity, context.Combat, context.Run);
    }

    private static bool TagsMatch(
        ContextualInfluenceDefinition definition,
        IReadOnlySet<string> tags) =>
        definition.RequiredTags.All(tags.Contains) &&
        !definition.ExcludedTags.Any(tags.Contains);

    private static string? Validate(ContextualInfluenceDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.InfluenceId) ||
            string.IsNullOrWhiteSpace(definition.Channel) ||
            string.IsNullOrWhiteSpace(definition.Bucket))
            return "influence is incomplete";
        if (definition.Value.HasValue == !string.IsNullOrWhiteSpace(definition.Formula))
            return $"influence {definition.InfluenceId} must define exactly one of value or formula";
        return null;
    }
}

/// <summary>Projects passive status components from the combat snapshot.</summary>
public sealed class StatusCalculationInfluenceProvider : ICalculationInfluenceProvider
{
    private readonly IRuntimeFormulaEvaluator _formulas;

    public StatusCalculationInfluenceProvider(IRuntimeFormulaEvaluator formulas) =>
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));

    public string ProviderId => "combat-statuses";

    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        if (context.Combat == null)
            return Result<IReadOnlyList<CalculationInfluence>>.Success([]);
        var result = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var (ownerId, statuses) in context.Combat.StatusEffects
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var status in statuses
                         .Where(item => item.IsActive)
                         .OrderBy(item => item.InstanceId))
            {
                foreach (var definition in status.Definition.Influences
                             .OrderByDescending(item => item.Priority)
                             .ThenBy(item => item.InfluenceId, StringComparer.Ordinal))
                {
                    var scoped = definition.Scope == CalculationEntityScope.Actor
                        ? context.Actor
                        : context.Target;
                    if (!string.Equals(ownerId, scoped?.InstanceId, StringComparison.Ordinal) ||
                        !definition.RequiredTags.All(context.Tags.Contains) ||
                        definition.ExcludedTags.Any(context.Tags.Contains))
                        continue;
                    if (string.IsNullOrWhiteSpace(definition.InfluenceId) ||
                        string.IsNullOrWhiteSpace(definition.Channel) ||
                        string.IsNullOrWhiteSpace(definition.Bucket) ||
                        definition.Value.HasValue == !string.IsNullOrWhiteSpace(definition.Formula))
                    {
                        return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                            $"Status {status.StatusId} contains an invalid influence component");
                    }
                    var value = ResolveValue(status, definition, context);
                    if (value.IsFailure)
                        return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                            $"Status {status.StatusId}/{definition.InfluenceId}: {value.Error}");
                    result.Add(new CalculationInfluence
                    {
                        InfluenceId = definition.InfluenceId,
                        SourceKind = CalculationSourceKind.Status,
                        SourceId = status.InstanceId.ToString(),
                        Channel = definition.Channel,
                        Bucket = definition.Bucket,
                        Value = value.Value,
                        Priority = definition.Priority
                    });
                }
            }
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }

    private Result<float> ResolveValue(
        StatusEffectInstance status,
        ContextualInfluenceDefinition definition,
        CalculationSourceContext context)
    {
        if (definition.Value.HasValue)
            return Result<float>.Success(definition.Value.Value * status.Stacks);
        var variables = context.Variables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        variables["stacks"] = status.Stacks;
        variables["duration"] = status.Duration;
        return !string.IsNullOrWhiteSpace(status.ContentRevision) &&
               _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(definition.Formula!, status.ContentRevision, variables)
            : _formulas.Evaluate(definition.Formula!, variables);
    }
}

/// <summary>Projects relic components pinned at acquisition into calculations.</summary>
public sealed class RelicCalculationInfluenceProvider : ICalculationInfluenceProvider
{
    private readonly IRuntimeFormulaEvaluator _formulas;

    public RelicCalculationInfluenceProvider(IRuntimeFormulaEvaluator formulas) =>
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));

    public string ProviderId => "run-relics";

    public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context)
    {
        if (context.Run == null)
            return Result<IReadOnlyList<CalculationInfluence>>.Success([]);
        var result = ImmutableArray.CreateBuilder<CalculationInfluence>();
        foreach (var relic in context.Run.Relics.OrderBy(item => item.RelicInstanceId))
        {
            foreach (var definition in relic.Influences
                         .OrderByDescending(item => item.Priority)
                         .ThenBy(item => item.InfluenceId, StringComparer.Ordinal))
            {
                var scoped = definition.Scope == CalculationEntityScope.Actor
                    ? context.Actor
                    : context.Target;
                if (scoped == null || context.Combat == null || !relic.Owner.Includes(scoped, context.Combat, context.Run) ||
                    !definition.RequiredTags.All(context.Tags.Contains) ||
                    definition.ExcludedTags.Any(context.Tags.Contains))
                    continue;
                if (string.IsNullOrWhiteSpace(definition.InfluenceId) ||
                    string.IsNullOrWhiteSpace(definition.Channel) ||
                    string.IsNullOrWhiteSpace(definition.Bucket) ||
                    definition.Value.HasValue == !string.IsNullOrWhiteSpace(definition.Formula))
                {
                    return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                        $"Relic {relic.DefinitionId} contains an invalid influence component");
                }
                var value = ResolveValue(relic, definition, context);
                if (value.IsFailure)
                    return Result<IReadOnlyList<CalculationInfluence>>.Failure(
                        $"Relic {relic.DefinitionId}/{definition.InfluenceId}: {value.Error}");
                result.Add(new CalculationInfluence
                {
                    InfluenceId = definition.InfluenceId,
                    SourceKind = CalculationSourceKind.Relic,
                    SourceId = relic.RelicInstanceId.ToString(),
                    Channel = definition.Channel,
                    Bucket = definition.Bucket,
                    Value = value.Value,
                    Priority = definition.Priority
                });
            }
        }
        return Result<IReadOnlyList<CalculationInfluence>>.Success(result.ToImmutable());
    }

    private Result<float> ResolveValue(
        RunRelicState relic,
        ContextualInfluenceDefinition definition,
        CalculationSourceContext context)
    {
        if (definition.Value.HasValue)
            return Result<float>.Success(definition.Value.Value * relic.Stacks);
        var variables = context.Variables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        variables["stacks"] = relic.Stacks;
        if (context.Combat != null)
        {
            var owner = context.Combat.GetAllActors().OrderBy(entity => entity.InstanceId, StringComparer.Ordinal)
                .FirstOrDefault(entity => relic.Owner.Includes(entity, context.Combat, context.Run));
            if (owner != null) ResourceFormulaVariables.AddOwner(variables, "owner", owner.ResourceState);
        }
        return !string.IsNullOrWhiteSpace(relic.ContentRevision) &&
               _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(definition.Formula!, relic.ContentRevision, variables)
            : _formulas.Evaluate(definition.Formula!, variables);
    }
}
