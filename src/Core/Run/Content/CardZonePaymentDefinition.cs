using System.Collections.Immutable;
using Core.Common;
using Core.Run.DeckLoadouts;

namespace Core.Run.Content;

/// <summary>Authoring contract; the ordinary cost component integrates it with transactional execution.</summary>
public sealed record CardZonePaymentDefinition
{
    public string PaymentId { get; init; } = string.Empty;
    public string SourceZoneId { get; init; } = string.Empty;
    public string TargetZoneId { get; init; } = string.Empty;
    public int Count { get; init; } = 1;
    public bool AllowResolvingCard { get; init; }
    public ImmutableArray<string> RequiredTags { get; init; } = [];
    public ImmutableArray<string> ExcludedTags { get; init; } = [];
    public Result Validate() => BuildDefinitionRules.Symbol(PaymentId) && BuildDefinitionRules.Symbol(SourceZoneId) &&
        BuildDefinitionRules.Symbol(TargetZoneId) && SourceZoneId != TargetZoneId && Count is >= 1 and <= 64 &&
        BuildDefinitionRules.Symbols(RequiredTags) && BuildDefinitionRules.Symbols(ExcludedTags) &&
        !RequiredTags.Intersect(ExcludedTags, StringComparer.Ordinal).Any()
        ? Result.Success() : Result.Failure("Payment requires identity, distinct zones, bounded positive count and consistent filters");
}

public sealed record CardPaymentSelection(string ComponentId, string PaymentId, ImmutableArray<Guid> CardInstanceIds);
