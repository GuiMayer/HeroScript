using System.Collections.Immutable;
using Core.Determinism;

namespace Core.Run.Content;

public static class CardPresentationVisibility
{
    public static CardPresentationFacts Clamp(CardPresentationFacts presentation, InspectionDetailLevel detail)
    {
        if (detail == InspectionDetailLevel.Full) return presentation;
        var filtered = presentation with
        {
            Explanations = presentation.Explanations.Select(value => value with {
                ConditionExpression = null,
                Buckets = value.Buckets.Select(bucket => bucket with { Formula = null, Contributions = [] }).ToImmutableArray()
            }).ToImmutableArray(),
            Impacts = presentation.Impacts.Select(value => value with { Parameters = value.Parameters.Select(parameter => parameter with {
                Buckets = parameter.Buckets.Select(bucket => bucket with { Formula = null, Contributions = [] }).ToImmutableArray()
            }).ToImmutableArray() }).ToImmutableArray(), Fingerprint = string.Empty
        };
        return filtered with { Fingerprint = CanonicalJson.ComputeHash(filtered) };
    }
}
