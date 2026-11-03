using System.Collections.Immutable;
using Core.Common;
using Core.Run.DeckLoadouts;

namespace Core.Run.Content;

/// <summary>Authored card payment. The cost component compiles it into the ordinary transactional zone effect.</summary>
public sealed record CardZonePaymentDefinition
{
    public string PaymentId { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? FlowId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CostOptionId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CardPaymentOrder? Order { get; init; }
    public string SourceZoneId { get; init; } = string.Empty;
    public string TargetZoneId { get; init; } = string.Empty;
    public int Count { get; init; } = 1;
    public bool AllowResolvingCard { get; init; }
    public ImmutableArray<string> RequiredTags { get; init; } = [];
    public ImmutableArray<string> ExcludedTags { get; init; } = [];
    public Result Validate() => (FlowId == null || BuildDefinitionRules.Symbol(FlowId)) &&
        (CostOptionId == null || BuildDefinitionRules.Symbol(CostOptionId)) && (!Order.HasValue || Enum.IsDefined(Order.Value)) &&
        BuildDefinitionRules.Symbol(PaymentId) && BuildDefinitionRules.Symbol(SourceZoneId) &&
        BuildDefinitionRules.Symbol(TargetZoneId) && SourceZoneId != TargetZoneId && Count is >= 1 and <= 64 &&
        BuildDefinitionRules.Symbols(RequiredTags) && BuildDefinitionRules.Symbols(ExcludedTags) &&
        !RequiredTags.Intersect(ExcludedTags, StringComparer.Ordinal).Any()
        ? Result.Success() : Result.Failure("Payment requires identity, distinct zones, bounded positive count and consistent filters");
}

public sealed record CardPaymentSelection(string ComponentId, string PaymentId, ImmutableArray<Guid> CardInstanceIds);

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<CardPaymentOrder>))]
public enum CardPaymentOrder { SourceOrder, ChosenOrder }
public sealed record ResolvedCardZonePayment(string ComponentId, string PaymentId, string FlowId,
    ImmutableArray<Guid> CardInstanceIds, bool IncludesResolvingCard);
public sealed record CardPaymentRequirement(string ComponentId, string PaymentId, string? CostOptionId, string SourceZoneId,
    string TargetZoneId, int Count, bool AllowResolvingCard, CardPaymentOrder Order, ImmutableArray<Guid> EligibleCardIds, string? UnavailableReason);
