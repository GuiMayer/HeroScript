using System.Text.Json.Serialization;

namespace Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectExecutionScope { EveryInvocation, OncePerAction, OncePerParentProc }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectChildTiming { AfterParentImpact, BeforeParentImpact }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectRandomScope { Action, ParentProc, Impact }

/// <summary>Stochastic numeric inputs, not a second damage calculator. A critical is an authored use of these inputs.</summary>
public sealed record EffectRandomInputDefinition
{
    public string InputId { get; init; } = string.Empty;
    public string? GroupId { get; init; }
    public float Chance { get; init; } = 1;
    public EffectRandomScope Scope { get; init; }
}

public sealed record EffectRandomInputResult
{
    public string InputId { get; init; } = string.Empty;
    public EffectRandomScope Scope { get; init; }
    public string ScopeId { get; init; } = string.Empty;
    public double? Roll { get; init; }
    public bool Success { get; init; }
}

internal static class EffectInputNamespaces
{
    public static bool IsFactVariable(string token)
    {
        if (token is "continuation.requested_change" or "continuation.applied_change" or "continuation.limited_change") return true;
        if (!token.StartsWith("rolls.", StringComparison.Ordinal)) return false;
        var parts = token.Split('.');
        return parts.Length == 3 && parts[2] == "success" && parts[1].Length is > 0 and <= 64 &&
            parts[1].All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}
