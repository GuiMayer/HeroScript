using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Run.Content;

namespace Core.Run.DeckCompartments;

/// <summary>Revisioned, role-free contribution using the ordinary card component schema.</summary>
public sealed record DeckComponentGrant
{
    public string GrantId { get; init; } = string.Empty;
    public string BundleId { get; init; } = string.Empty;
    public int Priority { get; init; }
    public ImmutableSortedDictionary<string, string> EffectBindings { get; init; } =
        ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
}

public sealed record DeckCompartmentDefinition
{
    public string CompartmentId { get; init; } = string.Empty;
    public string NameKey { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public ImmutableArray<DeckComponentGrant> Grants { get; init; } = [];
    public ImmutableArray<string> GrantedTags { get; init; } = [];
    public ImmutableArray<string> RequiredTags { get; init; } = [];
    public ImmutableArray<string> ExcludedTags { get; init; } = [];
}

public sealed record DeckCompartmentProfileDefinition
{
    public string ProfileId { get; init; } = string.Empty;
    public ImmutableArray<DeckCompartmentDefinition> Compartments { get; init; } = [];

    public Result Validate(ContentRuntime runtime)
    {
        if (!Symbol(ProfileId) || Compartments.IsDefaultOrEmpty || Compartments.Length > 32 ||
            Compartments.Any(c => c == null || !Symbol(c.CompartmentId)) ||
            Compartments.Select(c => c.CompartmentId).Distinct(StringComparer.Ordinal).Count() != Compartments.Length)
            return Result.Failure("Compartment profile requires a valid identity and 1..32 unique compartments");
        foreach (var compartment in Compartments)
        {
            if (compartment.Capacity is < 0 or > 256 || compartment.Grants.IsDefaultOrEmpty ||
                compartment.Grants.Length > 32 || compartment.Grants.Any(g => g == null || !Symbol(g.GrantId)) ||
                compartment.Grants.Select(g => g.GrantId).Distinct(StringComparer.Ordinal).Count() != compartment.Grants.Length ||
                !Tags(compartment.GrantedTags) || !Tags(compartment.RequiredTags) || !Tags(compartment.ExcludedTags))
                return Result.Failure($"Invalid compartment capacity, grants or tags: {compartment.CompartmentId}");
            foreach (var grant in compartment.Grants)
            {
                if (!Symbol(grant.BundleId) || grant.EffectBindings == null || grant.EffectBindings.Count > 64)
                    return Result.Failure("Invalid compartment bundle reference or bindings");
                var bundle = runtime.GetDefinition<CardComponentBundleDefinition>("card-component-bundles", grant.BundleId);
                if (bundle.IsFailure) return Result.Failure(bundle.Error);
                var expanded = CardBundleCompiler.Expand(bundle.Value, "grant", grant.EffectBindings);
                if (expanded.IsFailure) return Result.Failure(expanded.Error);
                if (expanded.Value.Length > 64) return Result.Failure("Compartment grant exceeds component budget");
            }
        }
        return Result.Success();
    }

    internal static bool Symbol(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
    private static bool Tags(ImmutableArray<string> values) => !values.IsDefault && values.Length <= 64 &&
        values.All(Symbol) && values.Distinct(StringComparer.Ordinal).Count() == values.Length;
}

public sealed record DeckCompartmentPolicyDefinition
{
    public string PolicyId { get; init; } = string.Empty;
    public bool PreparationOnly { get; init; }
    public Result Validate() => DeckCompartmentProfileDefinition.Symbol(PolicyId)
        ? Result.Success() : Result.Failure("Compartment policy identity is required");
}
