using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Effects;
using System.Text.RegularExpressions;

namespace Core.Run.Content;

public sealed record CardBundleReference(string BundleId, string Namespace);

public sealed record CardTransformationSlotDefinition
{
    private ImmutableArray<CardTransformationCategory> _allowedCategories = [];
    public string SlotId { get; init; } = string.Empty;
    public int Capacity { get; init; } = 1;
    public IReadOnlyList<CardTransformationCategory> AllowedCategories
    {
        get => _allowedCategories;
        init => _allowedCategories = value?.ToImmutableArray() ?? [];
    }
}

/// <summary>Authored reference. Only the pinned compiler may turn it into a ledger snapshot.</summary>
public sealed record CardBundlePatchDefinition : CardUpgradePatchDefinition
{
    public string Namespace { get; init; } = string.Empty;
    public string? BundleId { get; init; }
    public CardComponentPatchOperation Operation { get; init; }
}

/// <summary>Closed immutable operation stored in a ledger; never accepted in authored upgrades.</summary>
public sealed record CardBundleSnapshotPatchDefinition : CardUpgradePatchDefinition
{
    private ImmutableArray<CardComponentDefinition> _components = [];
    public string Namespace { get; init; } = string.Empty;
    public string? BundleId { get; init; }
    public CardComponentPatchOperation Operation { get; init; }
    public IReadOnlyList<CardComponentDefinition> Components
    {
        get => _components;
        init => _components = value?.ToImmutableArray() ?? [];
    }
}

public static class CardBundleCompiler
{
    public static bool SafeNamespace(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 64 &&
        (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    public static Result<ImmutableArray<CardComponentDefinition>> Expand(
        CardComponentBundleDefinition bundle, string scope)
    {
        if (!SafeNamespace(scope) || string.IsNullOrWhiteSpace(bundle.BundleId) || bundle.Components.Count == 0 ||
            bundle.Components.Any(component => component == null || string.IsNullOrWhiteSpace(component.ComponentId)) ||
            bundle.Components.Select(component => component.ComponentId).Distinct(StringComparer.Ordinal).Count() != bundle.Components.Count)
            return Result<ImmutableArray<CardComponentDefinition>>.Failure("Invalid bundle identity, namespace or members");
        // Validate before traversing child effects, including bounded expansion and null payloads.
        var validated = new CardContentCompiler().Compile(new CardContentDefinition
            { CardId = bundle.BundleId, Components = bundle.Components });
        if (validated.IsFailure) return Result<ImmutableArray<CardComponentDefinition>>.Failure(validated.Error);
        var ids = bundle.Components.ToDictionary(component => component.ComponentId,
            component => $"{scope}.{component.ComponentId}", StringComparer.Ordinal);
        var effects = bundle.Components.OfType<CardEffectComponentDefinition>().Select(component => component.Effect)
            .Concat(bundle.Components.OfType<CardTriggerComponentDefinition>().SelectMany(component => component.Effects));
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Collect(EffectDefinition effect)
        {
            if (effect.OutputId != null) aliases.Add(effect.OutputId);
            foreach (var child in effect.ChainedEffects ?? []) Collect(child);
        }
        foreach (var effect in effects) Collect(effect);
        string? Formula(string? formula) => formula == null ? null : Regex.Replace(formula,
            @"(?<![A-Za-z0-9_.])results\.([A-Za-z_][A-Za-z0-9_]*)\.",
            match => aliases.Contains(match.Groups[1].Value)
                ? $"results.{scope}__{match.Groups[1].Value}." : match.Value, RegexOptions.IgnoreCase);
        EffectDefinition Rename(EffectDefinition effect) => effect with
        {
            OutputId = effect.OutputId == null ? null : $"{scope}__{effect.OutputId}",
            FormulaValue = Formula(effect.FormulaValue), Condition = Formula(effect.Condition),
            Parameters = effect.Parameters.Select(parameter => parameter with
                { FormulaValue = Formula(parameter.FormulaValue) }).ToImmutableArray(),
            PayloadBindings = effect.PayloadBindings.Select(binding => binding with
            {
                CardEffectComponentId = binding.CardEffectComponentId == null ? null : ids[binding.CardEffectComponentId],
                FormulaValue = Formula(binding.FormulaValue)
            }).ToImmutableArray(),
            ChainedEffects = effect.ChainedEffects?.Select(Rename).ToImmutableArray()
        };
        var result = bundle.Components.Select(component => component switch
        {
            CardEffectComponentDefinition effect => effect with { Effect = Rename(effect.Effect) },
            CardTriggerComponentDefinition trigger => trigger with { Effects = trigger.Effects.Select(Rename).ToImmutableArray() },
            CardConditionComponentDefinition condition => condition with { Expression = Formula(condition.Expression)! },
            CardInfluenceComponentDefinition influence => influence with { Formula = Formula(influence.Formula) },
            _ => component
        }).Select(component => component with { ComponentId = ids[component.ComponentId] }).ToImmutableArray();
        return Result<ImmutableArray<CardComponentDefinition>>.Success(result);
    }

    public static Result<CardUpgradeDefinition> Seal(CardUpgradeDefinition definition, ContentRuntime runtime)
    {
        var patches = ImmutableArray.CreateBuilder<CardUpgradePatchDefinition>();
        foreach (var patch in definition.Patches)
        {
            if (patch == null || patch is CardBundleSnapshotPatchDefinition)
                return Result<CardUpgradeDefinition>.Failure("Authored upgrade cannot contain null patches or bundle snapshots");
            if (patch is not CardBundlePatchDefinition reference) { patches.Add(patch); continue; }
            if (!SafeNamespace(reference.Namespace) || !string.IsNullOrEmpty(reference.ComponentId) ||
                !Enum.IsDefined(reference.Operation) || (reference.Operation == CardComponentPatchOperation.Remove
                    ? reference.BundleId != null : string.IsNullOrWhiteSpace(reference.BundleId)))
                return Result<CardUpgradeDefinition>.Failure("Invalid authored bundle operation");
            ImmutableArray<CardComponentDefinition> members = [];
            if (reference.Operation != CardComponentPatchOperation.Remove)
            {
                var bundle = runtime.GetDefinition<CardComponentBundleDefinition>("card-component-bundles", reference.BundleId!);
                if (bundle.IsFailure) return Result<CardUpgradeDefinition>.Failure(bundle.Error);
                if (bundle.Value.BundleId != reference.BundleId)
                    return Result<CardUpgradeDefinition>.Failure("Bundle definition identity mismatch");
                var expanded = Expand(bundle.Value, reference.Namespace);
                if (expanded.IsFailure) return Result<CardUpgradeDefinition>.Failure(expanded.Error);
                members = expanded.Value;
            }
            patches.Add(new CardBundleSnapshotPatchDefinition
            {
                Namespace = reference.Namespace, BundleId = reference.BundleId,
                Operation = reference.Operation, Components = members
            });
        }
        return Result<CardUpgradeDefinition>.Success(definition with { Patches = patches.ToImmutable() });
    }

    public static Result ValidateSlots(IReadOnlyList<CardTransformationSlotDefinition> slots)
    {
        if (slots.Any(slot => slot == null || string.IsNullOrWhiteSpace(slot.SlotId) || slot.Capacity < 1 ||
            slot.Capacity > CardTransformationLedger.MaximumEntries || slot.AllowedCategories.Count == 0 ||
            slot.AllowedCategories.Any(category => !Enum.IsDefined(category)) ||
            slot.AllowedCategories.Distinct().Count() != slot.AllowedCategories.Count) ||
            slots.Select(slot => slot.SlotId).Distinct(StringComparer.Ordinal).Count() != slots.Count)
            return Result.Failure("Transformation slots require unique IDs, bounded positive capacity and valid categories");
        return Result.Success();
    }

    public static Result ValidateOccupancy(CompiledCardDefinition definition, IReadOnlyList<CardUpgradeState> active)
    {
        var valid = ValidateSlots(definition.TransformationSlots);
        if (valid.IsFailure) return valid;
        foreach (var group in active.Where(entry => entry.SlotId != null).GroupBy(entry => entry.SlotId, StringComparer.Ordinal))
        {
            var slot = definition.TransformationSlots.SingleOrDefault(item => item.SlotId == group.Key);
            if (slot == null || group.Count() > slot.Capacity || group.Any(entry => !slot.AllowedCategories.Contains(entry.Category)))
                return Result.Failure($"Unknown, full or incompatible transformation slot: {group.Key}");
        }
        return Result.Success();
    }
}
