using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Common;

namespace Core.Entity;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AttributeOperation { Add, Multiply, Set }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AttributeLifetime { Encounter, RunBase }

public sealed record AttributeValueRule
{
    public ImmutableArray<AttributeOperation> AllowedOperations { get; init; } = [];
    public float? Minimum { get; init; }
    public float? Maximum { get; init; }
}

public sealed record AttributeMutationDefinition
{
    public string ComponentId { get; init; } = string.Empty;
    public string ValueId { get; init; } = string.Empty;
    public AttributeOperation Operation { get; init; }
    public AttributeLifetime Lifetime { get; init; }
}

public sealed record AttributeMutationOutcome(string ComponentId, string ValueId, AttributeOperation Operation,
    AttributeLifetime Lifetime, float PreviousValue, float CurrentValue);

/// <summary>Pure typed mutation. Authorization and bounds are captured in the immutable component.</summary>
public static class EntityAttributeTransitions
{
    public static Result Validate(IReadOnlyDictionary<string, float> values, IReadOnlyDictionary<string, AttributeValueRule> rules)
    {
        if (values.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || !float.IsFinite(pair.Value)))
            return Result.Failure("Attribute values must have identifiers and finite values");
        foreach (var (id, rule) in rules)
        {
            if (!values.TryGetValue(id, out var value) || rule == null ||
                rule.AllowedOperations.Any(operation => !Enum.IsDefined(operation)) ||
                rule.AllowedOperations.Distinct().Count() != rule.AllowedOperations.Length ||
                rule.Minimum is { } min && !float.IsFinite(min) || rule.Maximum is { } max && !float.IsFinite(max) ||
                rule.Minimum > rule.Maximum || value < rule.Minimum || value > rule.Maximum)
                return Result.Failure($"Invalid attribute rule or value: {id}");
        }
        return Result.Success();
    }

    public static Result<(EntityState State, AttributeMutationOutcome Outcome)> Apply(EntityState entity,
        AttributeMutationDefinition mutation, float amount)
    {
        if (!float.IsFinite(amount) || !Enum.IsDefined(mutation.Operation) || !Enum.IsDefined(mutation.Lifetime))
            return Result<(EntityState, AttributeMutationOutcome)>.Failure("Invalid attribute mutation");
        var component = entity.Component<StatEntityComponentState>(mutation.ComponentId);
        if (component == null || !component.Values.TryGetValue(mutation.ValueId, out var before) ||
            !component.ValueRules.TryGetValue(mutation.ValueId, out var rule) || !rule.AllowedOperations.Contains(mutation.Operation))
            return Result<(EntityState, AttributeMutationOutcome)>.Failure("Attribute mutation is not authorized by its component");
        var after = mutation.Operation switch { AttributeOperation.Add => before + amount,
            AttributeOperation.Multiply => before * amount, AttributeOperation.Set => amount, _ => float.NaN };
        if (!float.IsFinite(after) || after < rule.Minimum || after > rule.Maximum)
            return Result<(EntityState, AttributeMutationOutcome)>.Failure("Attribute mutation exceeds its finite bounds");
        var next = entity with { Components = entity.Components.ToImmutableDictionary(StringComparer.Ordinal).SetItem(component.ComponentId,
            component with { Values = component.Values.ToImmutableDictionary(StringComparer.Ordinal).SetItem(mutation.ValueId, after) }) };
        return Result<(EntityState, AttributeMutationOutcome)>.Success((next,
            new(mutation.ComponentId, mutation.ValueId, mutation.Operation, mutation.Lifetime, before, after)));
    }
}
