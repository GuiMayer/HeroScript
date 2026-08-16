using System.Collections.Immutable;
using System.Text.Json;

namespace Core.Run;

public sealed record GameModeDefinition
{
    private ImmutableDictionary<string, JsonElement> _rules =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);

    public string ModeId { get; init; } = string.Empty;
    public string? RunDefinitionId { get; init; }
    public bool AllowCustomSeed { get; init; } = true;

    public IReadOnlyDictionary<string, JsonElement> Rules
    {
        get => _rules;
        init => _rules = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public sealed record DailyChallengeDefinition
{
    public string ChallengeId { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
    public string ConfigName { get; init; } = "default";
    public string RunDefinitionId { get; init; } = "default_run";
    public string ModeId { get; init; } = "standard";
    public ulong Seed { get; init; }
}
