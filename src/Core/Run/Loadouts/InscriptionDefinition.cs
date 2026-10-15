using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.Common;
using Core.Content;
using Core.Effects;

namespace Core.Run.Loadouts;

public sealed record InscriptionAttributeBonusDefinition
{
    public string AttributeId { get; init; } = string.Empty;
    public ContextualInfluenceDefinition Influence { get; init; } = new();
}

/// <summary>Character equipment, never a card component, upgrade or card-owned item.</summary>
public sealed record InscriptionDefinition
{
    public string InscriptionId { get; init; } = string.Empty;
    public float EquipmentCost { get; init; }
    public ImmutableArray<string> Tags { get; init; } = [];
    public ImmutableArray<InscriptionAttributeBonusDefinition> AttributeBonuses { get; init; } = [];
    public ImmutableDictionary<string, JsonElement> Presentation { get; init; } = ImmutableDictionary<string, JsonElement>.Empty;

    public Result Validate(ContentRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(InscriptionId) || !float.IsFinite(EquipmentCost) || EquipmentCost < 0 ||
            Tags.Any(string.IsNullOrWhiteSpace) || Tags.Distinct(StringComparer.Ordinal).Count() != Tags.Length ||
            AttributeBonuses.IsDefaultOrEmpty || AttributeBonuses.Any(bonus => bonus == null || bonus.Influence == null) ||
            AttributeBonuses.Select(bonus => bonus.Influence.InfluenceId).Distinct(StringComparer.Ordinal).Count() != AttributeBonuses.Length)
            return Result.Failure("Inscription requires an identity, finite nonnegative equipment cost, unique tags and attribute bonuses");
        foreach (var bonus in AttributeBonuses)
        {
            var invalid = ContextualInfluencePolicies.Validate(bonus.Influence);
            if (invalid != null) return Result.Failure(invalid);
            var matching = runtime.GetDefinitions("effect-resolution-policies").Keys
                .Select(id => runtime.GetDefinition<EffectResolutionPolicyDefinition>("effect-resolution-policies", id))
                .Where(result => result.IsSuccess).SelectMany(result => result.Value.Attributes)
                .Where(attribute => attribute.AttributeId == bonus.AttributeId)
                .Select(attribute => runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", attribute.PipelineId));
            if (string.IsNullOrWhiteSpace(bonus.AttributeId) || !matching.Any(pipeline => pipeline.IsSuccess &&
                pipeline.Value.Channel == bonus.Influence.Channel && pipeline.Value.Buckets.Any(bucket => bucket.BucketId == bonus.Influence.Bucket)))
                return Result.Failure($"Inscription {InscriptionId} bonus must address an existing character attribute pipeline: {bonus.AttributeId}");
        }
        return Result.Success();
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InscriptionInventoryScope { Actor, Run }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoadoutEquipmentWindow { OutsideCombat, Preparation, Combat }

public sealed record LoadoutSlotDefinition
{
    public string SlotId { get; init; } = string.Empty;
    public ImmutableArray<string> RequiredTags { get; init; } = [];
}

public sealed record LoadoutPolicyDefinition
{
    public string LoadoutPolicyId { get; init; } = string.Empty;
    public InscriptionInventoryScope? InventoryScope { get; init; }
    public float Capacity { get; init; }
    public bool? AllowDuplicateDefinitions { get; init; }
    public int MaximumInventoryEntries { get; init; } = 512;
    public ImmutableArray<LoadoutSlotDefinition> Slots { get; init; } = [];
    public ImmutableArray<string> InscriptionIds { get; init; } = [];
    public ImmutableArray<LoadoutEquipmentWindow> EquipmentWindows { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<string>? StartingInscriptionIds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<InscriptionStationDefinition>? Stations { get; init; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(LoadoutPolicyId) || InventoryScope == null || !Enum.IsDefined(InventoryScope.Value) ||
            AllowDuplicateDefinitions == null || !float.IsFinite(Capacity) || Capacity < 0 || MaximumInventoryEntries is < 1 or > 4096 ||
            InscriptionIds.IsDefaultOrEmpty || InscriptionIds.Any(string.IsNullOrWhiteSpace) || InscriptionIds.Distinct(StringComparer.Ordinal).Count() != InscriptionIds.Length ||
            Slots.IsDefaultOrEmpty || Slots.Length > 64 || Slots.Any(slot => slot == null || string.IsNullOrWhiteSpace(slot.SlotId) ||
                slot.RequiredTags.Any(string.IsNullOrWhiteSpace) || slot.RequiredTags.Distinct(StringComparer.Ordinal).Count() != slot.RequiredTags.Length) ||
            Slots.Select(slot => slot.SlotId).Distinct(StringComparer.Ordinal).Count() != Slots.Length ||
            EquipmentWindows.IsDefaultOrEmpty || EquipmentWindows.Any(window => !Enum.IsDefined(window)) || EquipmentWindows.Distinct().Count() != EquipmentWindows.Length)
            return Result.Failure("Loadout policy requires explicit inventory scope, duplicate policy, valid slots, capacity and equipment windows");
        if (StartingInscriptionIds is { } starting && (starting.IsDefault || starting.Length > MaximumInventoryEntries || starting.Any(id => !InscriptionIds.Contains(id))))
            return Result.Failure("Starting inscriptions must belong to the allowed catalog and inventory bound");
        var stations = InscriptionStationTransitions.Validate(Stations ?? [], InscriptionIds);
        return stations;
    }

    public Result ValidateBindings(ContentRuntime runtime, GameModeDefinition? mode = null)
    {
        var valid = Validate();
        if (valid.IsFailure) return valid;
        foreach (var id in InscriptionIds)
        {
            var item = runtime.GetDefinition<InscriptionDefinition>("inscriptions", id);
            if (item.IsFailure) return Result.Failure(item.Error);
            valid = item.Value.Validate(runtime);
            if (valid.IsFailure) return valid;
            if (mode == null) continue;
            var attributes = runtime.GetDefinition<EffectResolutionPolicyDefinition>("effect-resolution-policies", mode.EffectResolutionPolicyId ?? string.Empty);
            if (attributes.IsFailure) return Result.Failure("Loadout-enabled mode requires its character attribute policy");
            foreach (var bonus in item.Value.AttributeBonuses)
            {
                var attribute = attributes.Value.Attributes.FirstOrDefault(value => value.AttributeId == bonus.AttributeId);
                if (attribute == null || !mode.CalculationPipelineIds.Contains(attribute.PipelineId, StringComparer.Ordinal))
                    return Result.Failure($"Inscription {id} attribute {bonus.AttributeId} is not enabled by mode {mode.ModeId}");
                var pipeline = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", attribute.PipelineId);
                if (pipeline.IsFailure || pipeline.Value.Channel != bonus.Influence.Channel || !pipeline.Value.Buckets.Any(bucket => bucket.BucketId == bonus.Influence.Bucket))
                    return Result.Failure($"Inscription {id} attribute pipeline differs from mode {mode.ModeId}");
            }
        }
        return Result.Success();
    }
}
