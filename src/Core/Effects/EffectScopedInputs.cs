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
