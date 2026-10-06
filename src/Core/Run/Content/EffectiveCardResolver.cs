using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Determinism;
using System.Text.Json;

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
        CardInstanceState instance) => Resolve(definition, instance, null);

    public Result<EffectiveCardDefinition> Resolve(CompiledCardDefinition definition, CardInstanceState instance,
        ICollection<CardCompositionDiagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(instance);
        if (!string.Equals(definition.CardId, instance.DefinitionId, StringComparison.Ordinal))
        {
            return Result<EffectiveCardDefinition>.Failure(
                $"Card instance definition mismatch: {instance.DefinitionId} != {definition.CardId}");
        }

        var active = CardTransformationLedger.Project(instance.Upgrades);
        if (active.IsFailure) return Result<EffectiveCardDefinition>.Failure(active.Error);
        var occupancy = CardBundleCompiler.ValidateOccupancy(definition, active.Value);
        if (occupancy.IsFailure) return Result<EffectiveCardDefinition>.Failure(occupancy.Error);
        var components = definition.Components.ToImmutableArray();
        var tags = definition.Tags.ToImmutableArray();
        var upgradeTrace = ImmutableArray.CreateBuilder<CardUpgradeApplicationTrace>();
        foreach (var upgrade in active.Value)
        {
            foreach (var patch in upgrade.Patches)
            {
                if (patch == null) return Result<EffectiveCardDefinition>.Failure("Transformation patch cannot be null");
                if (patch is CardBundleSnapshotPatchDefinition bundle)
                {
                    var bundled = PatchBundle(components, bundle);
                    if (bundled.IsFailure) return Result<EffectiveCardDefinition>.Failure($"Upgrade {upgrade.UpgradeId}: {bundled.Error}");
                    // Structural member traces preserve the ordinary numeric-base provenance contract.
                    foreach (var id in components.Concat(bundled.Value).Where(item => item.ComponentId.StartsWith(bundle.Namespace + ".", StringComparison.Ordinal))
                        .Select(item => item.ComponentId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
                    {
                        var memberBefore = components.FirstOrDefault(item => item.ComponentId == id);
                        var memberAfter = bundled.Value.FirstOrDefault(item => item.ComponentId == id);
                        try { upgradeTrace.Add(CreateTrace(upgrade, new CardComponentPatchDefinition
                        {
                            ComponentId = id, Operation = memberBefore == null ? CardComponentPatchOperation.Add :
                                memberAfter == null ? CardComponentPatchOperation.Remove : CardComponentPatchOperation.Replace
                        }, memberBefore, memberAfter)); }
                        catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException)
                        { return Result<EffectiveCardDefinition>.Failure($"Upgrade {upgrade.UpgradeId}: invalid bundle snapshot: {exception.Message}"); }
                    }
                    components = bundled.Value;
                    continue;
                }
                if (patch is CardTagsPatchDefinition tagPatch)
                {
                    var changedTags = PatchTags(tags, tagPatch);
                    if (changedTags.IsFailure)
                        return Result<EffectiveCardDefinition>.Failure($"Upgrade {upgrade.UpgradeId}: {changedTags.Error}");
                    upgradeTrace.Add(new()
                    {
                        UpgradeId = upgrade.UpgradeId, TransformationId = upgrade.TransformationId,
                        Category = upgrade.Category, Attribute = "Tags",
                        PreviousChoice = string.Join(",", tags), CurrentChoice = string.Join(",", changedTags.Value)
                    });
                    tags = changedTags.Value;
                    continue;
                }
                Result<ImmutableArray<CardComponentDefinition>> applied;
                try { applied = ApplyPatch(components, patch); }
                catch (OverflowException)
                { return Result<EffectiveCardDefinition>.Failure($"Upgrade {upgrade.UpgradeId}: numeric overflow in patch {patch.ComponentId}"); }
                if (applied.IsFailure)
                {
                    return Result<EffectiveCardDefinition>.Failure(
                        $"Upgrade {upgrade.UpgradeId}: {applied.Error}");
                }
                var before = components.FirstOrDefault(item => string.Equals(item.ComponentId, patch.ComponentId, StringComparison.Ordinal));
                components = applied.Value;
                var after = components.FirstOrDefault(item => string.Equals(item.ComponentId, patch.ComponentId, StringComparison.Ordinal));
                try { upgradeTrace.Add(CreateTrace(upgrade, patch, before, after)); }
                catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException)
                {
                    return Result<EffectiveCardDefinition>.Failure($"Upgrade {upgrade.UpgradeId}: invalid component snapshot: {exception.Message}");
                }
            }
        }

        // Use the same closed-container compiler as authored cards. Structural
        // mutations cannot bypass binding, alias, trigger or singleton checks.
        var recompiled = new CardContentCompiler().Compile(new CardContentDefinition
        {
            CardId = definition.CardId, Rarity = definition.Rarity,
            BasePrices = definition.BasePrices, DecomposeRewards = definition.DecomposeRewards,
            Tags = tags, Components = components, TransformationSlots = definition.TransformationSlots
        });
        if (recompiled.IsFailure) return Result<EffectiveCardDefinition>.Failure(recompiled.Error);
        components = recompiled.Value.Components.ToImmutableArray();
        tags = recompiled.Value.Tags.ToImmutableArray();
        var composition = CardCompositionGrammar.Compose(tags, components, active.Value);
        foreach (var diagnostic in composition.Diagnostics) diagnostics?.Add(diagnostic);
        if (!composition.Diagnostics.IsEmpty)
            return Result<EffectiveCardDefinition>.Failure(string.Join("; ", composition.Diagnostics.Select(item => item.Message)));
        if (!composition.Trace.IsEmpty)
        {
            var lowered = new CardContentCompiler().Compile(new CardContentDefinition
            {
                CardId = definition.CardId, Rarity = definition.Rarity,
                BasePrices = definition.BasePrices, DecomposeRewards = definition.DecomposeRewards,
                Tags = tags, Components = composition.Components, TransformationSlots = definition.TransformationSlots
            });
            if (lowered.IsFailure)
            {
                diagnostics?.Add(new("invalid_composition", lowered.Error));
                return Result<EffectiveCardDefinition>.Failure(lowered.Error);
            }
            components = lowered.Value.Components.ToImmutableArray();
        }
        var upgrades = active.Value;
        var payload = new EffectiveCardFingerprintPayload(
            instance.CardInstanceId,
            instance.DefinitionId,
            definition.Fingerprint,
            tags,
            instance.Upgrades.ToImmutableArray(),
            upgrades,
            upgradeTrace.ToImmutable(),
            composition.Trace,
            components);
        string fingerprint;
        try { fingerprint = payload.ComputeFingerprint(); }
        catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException)
        { return Result<EffectiveCardDefinition>.Failure($"Invalid transformation ledger snapshot: {exception.Message}"); }
        return Result<EffectiveCardDefinition>.Success(new EffectiveCardDefinition
        {
            CardInstanceId = instance.CardInstanceId,
            DefinitionId = instance.DefinitionId,
            DefinitionFingerprint = definition.Fingerprint,
            TransformationSlots = definition.TransformationSlots,
            Tags = tags,
            AppliedUpgrades = upgrades,
            TransformationLedger = instance.Upgrades,
            UpgradeTrace = upgradeTrace.ToImmutable(),
            CompositionTrace = composition.Trace,
            Components = components,
            Fingerprint = fingerprint
        });
    }

    public Result ValidateUpgrade(
        CompiledCardDefinition definition,
        CardUpgradeDefinition upgrade) => ValidateUpgrade(definition, upgrade, null);

    public Result ValidateUpgrade(CompiledCardDefinition definition, CardUpgradeDefinition upgrade,
        ICollection<CardCompositionDiagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(upgrade);
        if (string.IsNullOrWhiteSpace(upgrade.UpgradeId))
            return Result.Failure("UpgradeId is required");
        if (upgrade.MaxApplications < 1)
            return Result.Failure($"Upgrade {upgrade.UpgradeId} maxApplications must be positive");
        if (!upgrade.AppliesTo(definition.CardId))
            return Result.Failure($"Upgrade {upgrade.UpgradeId} does not apply to {definition.CardId}");
        if (upgrade.CompositionRules.Count != upgrade.ClosedCompositionRules.Length)
            return Result.Failure("Composition rules must be sealed from pinned content before validation");
        if (upgrade.Patches.Count == 0 && upgrade.ClosedCompositionRules.IsEmpty)
            return Result.Failure($"Upgrade {upgrade.UpgradeId} requires at least one typed patch");
        if (!Enum.IsDefined(upgrade.Category))
            return Result.Failure($"Upgrade {upgrade.UpgradeId} has an invalid category");

        var instance = new CardInstanceState
        {
            CardInstanceId = Guid.Parse("00000000-0000-8000-8000-000000000001"),
            DefinitionId = definition.CardId,
            Upgrades =
            [
                new CardUpgradeState
                {
                    TransformationId = 1, ContentRevision = "validation", Category = upgrade.Category, SlotId = upgrade.SlotId,
                    UpgradeId = upgrade.UpgradeId,
                    Patches = upgrade.Patches, Requirements = upgrade.Requirements,
                    CompositionRules = upgrade.ClosedCompositionRules
                }
            ]
        };
        var resolved = Resolve(definition, instance, diagnostics);
        return resolved.IsFailure ? Result.Failure(resolved.Error) : Result.Success();
    }

    private static Result<ImmutableArray<CardComponentDefinition>> ApplyPatch(
        ImmutableArray<CardComponentDefinition> components,
        CardUpgradePatchDefinition patch)
    {
        if (string.IsNullOrWhiteSpace(patch.ComponentId))
            return Result<ImmutableArray<CardComponentDefinition>>.Failure("Patch componentId is required");
        if (patch is CardComponentPatchDefinition structural)
            return PatchComponent(components, structural);
        var validPolicy = patch switch
        {
            CardEffectNumericPatchDefinition effect => Enum.IsDefined(effect.Attribute) && Enum.IsDefined(effect.Operation) && float.IsFinite(effect.Value),
            CardEffectParameterNumericPatchDefinition parameter => Enum.IsDefined(parameter.Parameter) && Enum.IsDefined(parameter.Operation) && float.IsFinite(parameter.Value),
            CardCostAmountPatchDefinition cost => Enum.IsDefined(cost.Operation) && float.IsFinite(cost.Value),
            CardInfluenceNumericPatchDefinition influence => Enum.IsDefined(influence.Operation) && float.IsFinite(influence.Value),
            CardTargetingNumericPatchDefinition targeting => Enum.IsDefined(targeting.Attribute) && Enum.IsDefined(targeting.Operation),
            CardDispositionPatchDefinition disposition =>
                !string.IsNullOrWhiteSpace(disposition.CardZoneResolutionFlowId),
            _ => false
        };
        if (!validPolicy) return Result<ImmutableArray<CardComponentDefinition>>.Failure("Invalid patch policy or non-finite value");
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
            CardEffectParameterNumericPatchDefinition parameter => PatchParameter(components[index], parameter),
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

    private static Result<ImmutableArray<CardComponentDefinition>> PatchBundle(
        ImmutableArray<CardComponentDefinition> components, CardBundleSnapshotPatchDefinition patch)
    {
        var prefix = patch.Namespace + ".";
        if (!CardBundleCompiler.SafeNamespace(patch.Namespace) || !string.IsNullOrEmpty(patch.ComponentId) ||
            !Enum.IsDefined(patch.Operation) || (patch.Operation == CardComponentPatchOperation.Remove
                ? patch.BundleId != null || patch.Components.Count != 0
                : string.IsNullOrWhiteSpace(patch.BundleId) || patch.Components.Count == 0 ||
                  patch.Components.Any(item => item == null || !item.ComponentId.StartsWith(prefix, StringComparison.Ordinal))))
            return Result<ImmutableArray<CardComponentDefinition>>.Failure("Invalid closed bundle snapshot");
        var exists = components.Any(item => item.ComponentId.StartsWith(prefix, StringComparison.Ordinal));
        if (patch.Operation == CardComponentPatchOperation.Add ? exists : !exists)
            return Result<ImmutableArray<CardComponentDefinition>>.Failure($"Bundle namespace collision or missing namespace: {patch.Namespace}");
        return Result<ImmutableArray<CardComponentDefinition>>.Success(components
            .Where(item => !item.ComponentId.StartsWith(prefix, StringComparison.Ordinal))
            .Concat(patch.Components).ToImmutableArray());
    }

    private static Result<CardComponentDefinition> PatchEffect(
        CardComponentDefinition component,
        CardEffectNumericPatchDefinition patch)
    {
        if (component is not CardEffectComponentDefinition effectComponent)
            return WrongType(component, "effect");
        var effect = effectComponent.Effect;
        if (effect == null || effect.Parameters.Any(item => item == null))
            return Result<CardComponentDefinition>.Failure("Effect or parameter cannot be null");
        var overridden = patch.Attribute switch
        {
            CardEffectNumericAttribute.FlatValue => EffectNumericParameter.Amount,
            CardEffectNumericAttribute.StatusStacks => EffectNumericParameter.StatusStacks,
            CardEffectNumericAttribute.StatusDuration => EffectNumericParameter.StatusDuration,
            _ => (EffectNumericParameter?)null
        };
        if (overridden != null && effect.Parameters.Any(item => item.Parameter == overridden))
            return Result<CardComponentDefinition>.Failure("Use effect_parameter_numeric to patch an overridden parameter base");
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
        if (costComponent.Costs == null || costComponent.Costs.Costs.Any(cost => cost == null))
            return Result<CardComponentDefinition>.Failure("Cost payload cannot be null");
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
        if (influence.Value == null || !string.IsNullOrWhiteSpace(influence.Formula))
            return Result<CardComponentDefinition>.Failure("Numeric influence patches require a flat base, not a formula");
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
                disposition with
                {
                    CardZoneResolutionFlowId = patch.CardZoneResolutionFlowId
                })
            : WrongType(component, "disposition");

    private static Result<CardComponentDefinition> WrongType(
        CardComponentDefinition component,
        string expected) =>
        Result<CardComponentDefinition>.Failure(
            $"Component {component.ComponentId} is {component.GetType().Name}, expected {expected}");

    private static CardUpgradeApplicationTrace CreateTrace(CardUpgradeState upgrade, CardUpgradePatchDefinition patch,
        CardComponentDefinition? before, CardComponentDefinition? after)
    {
        var trace = new CardUpgradeApplicationTrace
        {
            UpgradeId = upgrade.UpgradeId, ComponentId = patch.ComponentId,
            TransformationId = upgrade.TransformationId, Category = upgrade.Category
        };
        if (patch is CardComponentPatchDefinition structural)
            return trace with
            {
                Attribute = $"Component.{structural.Operation}",
                PreviousChoice = before == null ? null : CanonicalJson.ComputeHash<CardComponentDefinition>(before),
                CurrentChoice = after == null ? null : CanonicalJson.ComputeHash<CardComponentDefinition>(after)
            };
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return patch switch
        {
            CardEffectParameterNumericPatchDefinition parameter => trace with
            {
                Attribute = $"{parameter.Parameter}.FlatValue", Operation = parameter.Operation,
                PreviousValue = ((CardEffectComponentDefinition)before!).Effect.Parameters.Single(item => item.Parameter == parameter.Parameter).FlatValue ?? 0,
                CurrentValue = ((CardEffectComponentDefinition)after!).Effect.Parameters.Single(item => item.Parameter == parameter.Parameter).FlatValue
            },
            CardEffectNumericPatchDefinition numeric => trace with
            {
                Attribute = numeric.Attribute.ToString(), Operation = numeric.Operation,
                PreviousValue = EffectValue(((CardEffectComponentDefinition)before).Effect, numeric.Attribute),
                CurrentValue = EffectValue(((CardEffectComponentDefinition)after).Effect, numeric.Attribute)
            },
            CardCostAmountPatchDefinition cost => trace with
            {
                Attribute = $"cost:{cost.ResourceId}", Operation = cost.Operation,
                PreviousValue = ((CardCostComponentDefinition)before).Costs.Costs.Single(item => item.ResourceId == cost.ResourceId).Amount,
                CurrentValue = ((CardCostComponentDefinition)after).Costs.Costs.Single(item => item.ResourceId == cost.ResourceId).Amount
            },
            CardInfluenceNumericPatchDefinition numeric => trace with
            {
                Attribute = "Value", Operation = numeric.Operation,
                PreviousValue = ((CardInfluenceComponentDefinition)before).Value,
                CurrentValue = ((CardInfluenceComponentDefinition)after).Value
            },
            CardTargetingNumericPatchDefinition numeric => trace with
            {
                Attribute = numeric.Attribute.ToString(), Operation = numeric.Operation,
                PreviousValue = numeric.Attribute == CardTargetingNumericAttribute.MinimumTargets
                    ? ((CardTargetingComponentDefinition)before).MinimumTargets : ((CardTargetingComponentDefinition)before).MaximumTargets,
                CurrentValue = numeric.Attribute == CardTargetingNumericAttribute.MinimumTargets
                    ? ((CardTargetingComponentDefinition)after).MinimumTargets : ((CardTargetingComponentDefinition)after).MaximumTargets
            },
            CardDispositionPatchDefinition => trace with
            {
                Attribute = "CardZoneResolutionFlowId",
                PreviousChoice = ((CardDispositionComponentDefinition)before).CardZoneResolutionFlowId,
                CurrentChoice = ((CardDispositionComponentDefinition)after).CardZoneResolutionFlowId
            },
            _ => throw new InvalidOperationException($"Unsupported upgrade trace: {patch.GetType().Name}")
        };
    }

    private static Result<ImmutableArray<string>> PatchTags(ImmutableArray<string> tags, CardTagsPatchDefinition patch)
    {
        if (!string.IsNullOrEmpty(patch.ComponentId) || patch.Add.Count + patch.Remove.Count == 0 ||
            patch.Add.Concat(patch.Remove).Any(string.IsNullOrWhiteSpace) ||
            patch.Add.Concat(patch.Remove).Distinct(StringComparer.Ordinal).Count() != patch.Add.Count + patch.Remove.Count)
            return Result<ImmutableArray<string>>.Failure("Tag changes require nonempty, unique, disjoint tags and no componentId");
        if (patch.Remove.Any(tag => !tags.Contains(tag, StringComparer.Ordinal)) ||
            patch.Add.Any(tag => tags.Contains(tag, StringComparer.Ordinal)))
            return Result<ImmutableArray<string>>.Failure("Tag change conflicts with the current composition");
        return Result<ImmutableArray<string>>.Success(tags.Where(tag => !patch.Remove.Contains(tag, StringComparer.Ordinal))
            .Concat(patch.Add).OrderBy(tag => tag, StringComparer.Ordinal).ToImmutableArray());
    }

    private static Result<ImmutableArray<CardComponentDefinition>> PatchComponent(
        ImmutableArray<CardComponentDefinition> components, CardComponentPatchDefinition patch)
    {
        if (!Enum.IsDefined(patch.Operation) ||
            (patch.Operation == CardComponentPatchOperation.Remove ? patch.Component != null :
                patch.Component == null || patch.Component.ComponentId != patch.ComponentId))
            return Result<ImmutableArray<CardComponentDefinition>>.Failure("Invalid structural component operation or identity");
        var existing = components.FirstOrDefault(item => item.ComponentId == patch.ComponentId);
        if (patch.Operation == CardComponentPatchOperation.Add)
            return existing != null
                ? Result<ImmutableArray<CardComponentDefinition>>.Failure($"Component id collision: {patch.ComponentId}")
                : Result<ImmutableArray<CardComponentDefinition>>.Success(components.Add(patch.Component!));
        if (existing == null)
            return Result<ImmutableArray<CardComponentDefinition>>.Failure($"Component not found: {patch.ComponentId}");
        return Result<ImmutableArray<CardComponentDefinition>>.Success(patch.Operation == CardComponentPatchOperation.Remove
            ? components.Remove(existing) : components.SetItem(components.IndexOf(existing), patch.Component!));
    }

    private static Result<CardComponentDefinition> PatchParameter(CardComponentDefinition component,
        CardEffectParameterNumericPatchDefinition patch)
    {
        if (component is not CardEffectComponentDefinition effect) return WrongType(component, "effect");
        if (effect.Effect == null || effect.Effect.Parameters.Any(item => item == null))
            return Result<CardComponentDefinition>.Failure("Effect or parameter cannot be null");
        var matches = effect.Effect.Parameters.Where(item => item.Parameter == patch.Parameter).ToArray();
        if (matches.Length != 1) return Result<CardComponentDefinition>.Failure("Numeric parameter must exist exactly once");
        var parameter = matches[0];
        if (parameter == null || parameter.InputQuantityId != null)
            return Result<CardComponentDefinition>.Failure("Parameter base must exist and cannot be an input quantity");
        var value = Apply(parameter.FlatValue ?? 0, patch.Operation, patch.Value);
        if (!float.IsFinite(value)) return Result<CardComponentDefinition>.Failure("Parameter base must be finite");
        return Result<CardComponentDefinition>.Success(effect with
        {
            Effect = effect.Effect with
            {
                Parameters = effect.Effect.Parameters.Select(item => item.Parameter == patch.Parameter
                    ? item with { FlatValue = value } : item).ToImmutableArray()
            }
        });
    }

    private static float? EffectValue(EffectDefinition effect, CardEffectNumericAttribute attribute) => attribute switch
    {
        CardEffectNumericAttribute.FlatValue => effect.FlatValue ?? 0,
        CardEffectNumericAttribute.Chance => effect.Chance,
        CardEffectNumericAttribute.Repeat => effect.Repeat,
        CardEffectNumericAttribute.StatusStacks => effect.StatusStacks,
        CardEffectNumericAttribute.StatusDuration => effect.StatusDuration,
        _ => null
    };

    private static float Apply(
        float current,
        CardNumericPatchOperation operation,
        float value)
    {
        var result = operation switch
        {
            CardNumericPatchOperation.Add => current + value,
            CardNumericPatchOperation.Multiply => current * value,
            CardNumericPatchOperation.Set => value,
            _ => current
        };
        if (!float.IsFinite(result)) throw new OverflowException("Non-finite permanent base");
        return result;
    }

    private static int ApplyInteger(
        int current,
        CardNumericPatchOperation operation,
        float value) => checked((int)MathF.Round(Apply(current, operation, value)));
}
