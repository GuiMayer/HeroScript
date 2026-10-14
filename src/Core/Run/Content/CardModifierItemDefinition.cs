using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Content;

namespace Core.Run.Content;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardModifierItemUse { Consume, Bind }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardModifierItemState { Available, Consumed, Bound }

/// <summary>A separately owned item referencing the existing card composition grammar.</summary>
public sealed record CardModifierItemDefinition
{
    public string ItemDefinitionId { get; init; } = string.Empty;
    public string UpgradeId { get; init; } = string.Empty;
    public CardModifierItemUse? Use { get; init; }
    public ImmutableDictionary<string, string> Presentation { get; init; } = ImmutableDictionary<string, string>.Empty;

    public Result Validate(ContentRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(ItemDefinitionId) || string.IsNullOrWhiteSpace(UpgradeId) || Use == null || !Enum.IsDefined(Use.Value))
            return Result.Failure("Modifier item requires an identity, transformation and explicit consumption/binding policy");
        var upgrade = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", UpgradeId);
        if (upgrade.IsFailure) return Result.Failure(upgrade.Error);
        var sealedUpgrade = CardBundleCompiler.Seal(upgrade.Value, runtime);
        return sealedUpgrade.IsFailure ? Result.Failure(sealedUpgrade.Error) : Result.Success();
    }
}

public sealed record CardModifierItemInstance
{
    public Guid ItemInstanceId { get; init; }
    public string ItemDefinitionId { get; init; } = string.Empty;
    public string AcquisitionRevision { get; init; } = string.Empty;
    public string AcquisitionSource { get; init; } = string.Empty;
    public int AcquisitionSequence { get; init; }
    public CardModifierItemState State { get; init; } = CardModifierItemState.Available;
    public Guid? CardInstanceId { get; init; }
    public ulong? TransformationId { get; init; }
}

/// <summary>Immutable ownership. Retains consumed units for provenance instead of deleting their identity.</summary>
public sealed record CardModifierItemInventory
{
    public ImmutableArray<CardModifierItemInstance> Items { get; init; } = [];
    public const int MaximumEntries = 512;

    public Result Validate()
    {
        if (Items.Length > MaximumEntries || Items.Select(item => item.ItemInstanceId).Distinct().Count() != Items.Length ||
            Items.Any(item => item.ItemInstanceId == Guid.Empty || string.IsNullOrWhiteSpace(item.ItemDefinitionId) ||
                string.IsNullOrWhiteSpace(item.AcquisitionRevision) || string.IsNullOrWhiteSpace(item.AcquisitionSource) ||
                item.AcquisitionSequence < 0 || !Enum.IsDefined(item.State) ||
                item.State == CardModifierItemState.Available && (item.CardInstanceId != null || item.TransformationId != null) ||
                item.State != CardModifierItemState.Available && (item.CardInstanceId == null || item.CardInstanceId == Guid.Empty || item.TransformationId is null or 0)))
            return Result.Failure("Invalid modifier item inventory, ownership or provenance");
        return Result.Success();
    }

    public Result<CardModifierItemInventory> Add(CardModifierItemInstance item)
    {
        var next = this with { Items = Items.Add(item) };
        var valid = next.Validate();
        return valid.IsFailure ? Result<CardModifierItemInventory>.Failure(valid.Error) : Result<CardModifierItemInventory>.Success(next);
    }
}
