using System.Collections.Immutable;

namespace Core.Resources;

/// <summary>
/// Immutable inputs available to regeneration formulas. Callers may add any
/// numeric game variables without giving the resource subsystem knowledge of
/// turns, actors or a particular game mode.
/// </summary>
public sealed record ResourceRegenerationContext
{
    private ImmutableDictionary<string, float> _variables =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public string? ContentRevision { get; init; }
    public IReadOnlyDictionary<string, float> Variables
    {
        get => _variables;
        init => _variables = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record ResourceRegenerationResult(
    ResourceSet State,
    IReadOnlyList<ResourceMutationRecord> Records,
    RegenerationTiming Timing);
