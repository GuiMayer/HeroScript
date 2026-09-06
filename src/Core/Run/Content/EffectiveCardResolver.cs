using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;

namespace Core.Run.Content;

public interface IEffectiveCardResolver
{
    Result<EffectiveCardDefinition> Resolve(
        CompiledCardDefinition definition,
        CardInstanceState instance);

    Result ValidateUpgrade(
        CompiledCardDefinition definition,
        CardUpgradeDefinition upgrade);
}

/// <summary>
/// Pure resolver for definition base plus permanent instance upgrades.
/// Contextual scaling is intentionally a later, separate pipeline.
/// </summary>
public sealed class EffectiveCardResolver : IEffectiveCardResolver
{
    public Result<EffectiveCardDefinition> Resolve(
        CompiledCardDefinition definition,
        CardInstanceState instance)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(instance);
        if (!string.Equals(definition.CardId, instance.DefinitionId, StringComparison.Ordinal))
        {
            return Result<EffectiveCardDefinition>.Failure(
                $"Card instance definition mismatch: {instance.DefinitionId} != {definition.CardId}");
        }

        var components = definition.Components.ToImmutableArray();
        foreach (var upgrade in instance.Upgrades)
        {
            foreach (var patch in upgrade.Patches)
            {
                var applied = ApplyPatch(components, patch);
                if (applied.IsFailure)
                {
                    return Result<EffectiveCardDefinition>.Failure(
                        $"Upgrade {upgrade.UpgradeId}: {applied.Error}");
                }
                components = applied.Value;
            }
        }

        var validation = ValidateEffectiveComponents(components);
        if (validation.IsFailure)
            return Result<EffectiveCardDefinition>.Failure(validation.Error);
        var upgrades = instance.Upgrades.ToImmutableArray();
        var payload = new EffectiveCardFingerprintPayload(
            instance.CardInstanceId,
            instance.DefinitionId,
            definition.Fingerprint,
            upgrades,
            components);
        return Result<EffectiveCardDefinition>.Success(new EffectiveCardDefinition
        {
            CardInstanceId = instance.CardInstanceId,
            DefinitionId = instance.DefinitionId,
            DefinitionFingerprint = definition.Fingerprint,
            Tags = definition.Tags.ToImmutableArray(),
            AppliedUpgrades = upgrades,
            Components = components,
            Fingerprint = payload.ComputeFingerprint()
        });
    }

    public Result ValidateUpgrade(
        CompiledCardDefinition definition,
        CardUpgradeDefinition upgrade)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(upgrade);
        if (string.IsNullOrWhiteSpace(upgrade.UpgradeId))
            return Result.Failure("UpgradeId is required");
        if (upgrade.MaxApplications < 1)
            return Result.Failure($"Upgrade {upgrade.UpgradeId} maxApplications must be positive");
        if (!upgrade.AppliesTo(definition.CardId))
            return Result.Failure($"Upgrade {upgrade.UpgradeId} does not apply to {definition.CardId}");
        if (upgrade.Patches.Count == 0)
            return Result.Failure($"Upgrade {upgrade.UpgradeId} requires at least one typed patch");

        var instance = new CardInstanceState
        {
            CardInstanceId = Guid.Parse("00000000-0000-8000-8000-000000000001"),
            DefinitionId = definition.CardId,
            Upgrades =
            [
                new CardUpgradeState
                {
                    UpgradeId = upgrade.UpgradeId,
                    Patches = upgrade.Patches
                }
            ]
        };
        var resolved = Resolve(definition, instance);
        return resolved.IsFailure ? Result.Failure(resolved.Error) : Result.Success();
    }

    private static Result<ImmutableArray<CardComponentDefinition>> ApplyPatch(
        ImmutableArray<CardComponentDefinition> components,
        CardUpgradePatchDefinition patch)
    {
        if (string.IsNullOrWhiteSpace(patch.ComponentId))
            return Result<ImmutableArray<CardComponentDefinition>>.Failure("Patch componentId is required");
        var index = -1;
        for (var candidate = 0; candidate < components.Length; candidate++)
        {
            if (!string.Equals(
                    components[candidate].ComponentId,
                    patch.ComponentId,
                    StringComparison.Ordinal))
                continue;
            index = candidate;
            break;
        }
        if (index < 0)
            return Result<ImmutableArray<CardComponentDefinition>>.Failure(
                $"Component not found: {patch.ComponentId}");

        var transformed = patch switch
        {
            CardEffectNumericPatchDefinition effect => PatchEffect(components[index], effect),
            CardCostAmountPatchDefinition cost => PatchCost(components[index], cost),
            CardInfluenceNumericPatchDefinition influence => PatchInfluence(components[index], influence),
            CardTargetingNumericPatchDefinition targeting => PatchTargeting(components[index], targeting),
            CardDispositionPatchDefinition disposition => PatchDisposition(components[index], disposition),
            _ => Result<CardComponentDefinition>.Failure(
                $"Unsupported patch type: {patch.GetType().Name}")
        };
        return transformed.IsFailure
            ? Result<ImmutableArray<CardComponentDefinition>>.Failure(transformed.Error)
            : Result<ImmutableArray<CardComponentDefinition>>.Success(
                components.SetItem(index, transformed.Value));
    }

    private static Result<CardComponentDefinition> PatchEffect(
        CardComponentDefinition component,
        CardEffectNumericPatchDefinition patch)
    {
        if (component is not CardEffectComponentDefinition effectComponent)
            return WrongType(component, "effect");
        var effect = effectComponent.Effect;
        effect = patch.Attribute switch
        {
            CardEffectNumericAttribute.FlatValue => effect with
            {
                FlatValue = Apply(effect.FlatValue ?? 0, patch.Operation, patch.Value)
            },
            CardEffectNumericAttribute.Chance => effect with
            {
                Chance = Apply(effect.Chance, patch.Operation, patch.Value)
            },
            CardEffectNumericAttribute.Repeat => effect with
            {
                Repeat = ApplyInteger(effect.Repeat, patch.Operation, patch.Value)
            },
            CardEffectNumericAttribute.StatusStacks => effect with
            {
                StatusStacks = ApplyInteger(effect.StatusStacks ?? 0, patch.Operation, patch.Value)
            },
            CardEffectNumericAttribute.StatusDuration => effect with
            {
                StatusDuration = ApplyInteger(effect.StatusDuration ?? 0, patch.Operation, patch.Value)
            },
            CardEffectNumericAttribute.ModifierValue => effect with
            {
                ModifierValue = Apply(effect.ModifierValue ?? 0, patch.Operation, patch.Value)
            },
            _ => effect
        };
        return Result<CardComponentDefinition>.Success(effectComponent with { Effect = effect });
    }

    private static Result<CardComponentDefinition> PatchCost(
        CardComponentDefinition component,
        CardCostAmountPatchDefinition patch)
    {
        if (component is not CardCostComponentDefinition costComponent)
            return WrongType(component, "cost");
        if (string.IsNullOrWhiteSpace(patch.ResourceId))
            return Result<CardComponentDefinition>.Failure("Cost patch resourceId is required");
        var matches = costComponent.Costs.Costs.Count(cost =>
            string.Equals(cost.ResourceId, patch.ResourceId, StringComparison.Ordinal));
        if (matches != 1)
        {
            return Result<CardComponentDefinition>.Failure(
                $"Cost component {component.ComponentId} must contain exactly one {patch.ResourceId} cost");
        }
        var costs = costComponent.Costs.Costs.Select(cost =>
            string.Equals(cost.ResourceId, patch.ResourceId, StringComparison.Ordinal)
                ? cost with { Amount = Apply(cost.Amount, patch.Operation, patch.Value) }
                : cost).ToArray();
        return Result<CardComponentDefinition>.Success(costComponent with
        {
            Costs = costComponent.Costs with { Costs = costs }
        });
    }

    private static Result<CardComponentDefinition> PatchInfluence(
        CardComponentDefinition component,
        CardInfluenceNumericPatchDefinition patch)
    {
        if (component is not CardInfluenceComponentDefinition influence)
            return WrongType(component, "influence");
        return Result<CardComponentDefinition>.Success(influence with
        {
            Value = Apply(influence.Value ?? 0, patch.Operation, patch.Value)
        });
    }

    private static Result<CardComponentDefinition> PatchTargeting(
        CardComponentDefinition component,
        CardTargetingNumericPatchDefinition patch)
    {
        if (component is not CardTargetingComponentDefinition targeting)
            return WrongType(component, "targeting");
        return Result<CardComponentDefinition>.Success(patch.Attribute switch
        {
            CardTargetingNumericAttribute.MinimumTargets => targeting with
            {
                MinimumTargets = ApplyInteger(targeting.MinimumTargets, patch.Operation, patch.Value)
            },
            CardTargetingNumericAttribute.MaximumTargets => targeting with
            {
                MaximumTargets = ApplyInteger(targeting.MaximumTargets, patch.Operation, patch.Value)
            },
            _ => targeting
        });
    }

    private static Result<CardComponentDefinition> PatchDisposition(
        CardComponentDefinition component,
        CardDispositionPatchDefinition patch) =>
        component is CardDispositionComponentDefinition disposition
            ? Result<CardComponentDefinition>.Success(
                disposition with { Destination = patch.Destination })
            : WrongType(component, "disposition");

    private static Result ValidateEffectiveComponents(
        ImmutableArray<CardComponentDefinition> components)
    {
        foreach (var cost in components.OfType<CardCostComponentDefinition>()
                     .SelectMany(component => component.Costs.Costs))
        {
            if (cost.Amount < 0 || float.IsNaN(cost.Amount) || float.IsInfinity(cost.Amount))
                return Result.Failure($"Effective cost {cost.ResourceId} is invalid: {cost.Amount}");
        }
        foreach (var effect in components.OfType<CardEffectComponentDefinition>()
                     .Select(component => component.Effect))
        {
            if (effect.FlatValue is { } value && (float.IsNaN(value) || float.IsInfinity(value)))
                return Result.Failure("Effective effect flatValue must be finite");
            if (effect.Chance is < 0 or > 1)
                return Result.Failure($"Effective effect chance is invalid: {effect.Chance}");
            if (effect.Repeat < 1)
                return Result.Failure($"Effective effect repeat is invalid: {effect.Repeat}");
        }
        foreach (var targeting in components.OfType<CardTargetingComponentDefinition>())
        {
            if (targeting.MinimumTargets < 0 || targeting.MaximumTargets < targeting.MinimumTargets)
                return Result.Failure($"Effective targeting {targeting.ComponentId} is invalid");
        }
        return Result.Success();
    }

    private static Result<CardComponentDefinition> WrongType(
        CardComponentDefinition component,
        string expected) =>
        Result<CardComponentDefinition>.Failure(
            $"Component {component.ComponentId} is {component.GetType().Name}, expected {expected}");

    private static float Apply(
        float current,
        CardNumericPatchOperation operation,
        float value) => operation switch
        {
            CardNumericPatchOperation.Add => current + value,
            CardNumericPatchOperation.Multiply => current * value,
            CardNumericPatchOperation.Set => value,
            _ => current
        };

    private static int ApplyInteger(
        int current,
        CardNumericPatchOperation operation,
        float value) => checked((int)MathF.Round(Apply(current, operation, value)));
}
