using System.Text.Json.Serialization;

namespace Core.Combat.Activation;

public sealed record CombatActivationRulesDefinition
{
    public string RulesId { get; init; } = "default_activation";
    public string TurnOrderSource { get; init; } = "existing_turn_order";
    public bool SkipDeadActors { get; init; } = true;
    public ActivationStartRules StartActivation { get; init; } = new();
    public ActivationEndRules EndActivation { get; init; } = new();
    public ActivationAiRules Ai { get; init; } = new();
    public ActivationEventRules Events { get; init; } = new();
}

public sealed record ActivationStartRules
{
    public int DrawCount { get; init; }
}

public sealed record ActivationEndRules
{
    public ActivationDiscardPolicy DiscardPolicy { get; init; } = ActivationDiscardPolicy.None;
    public int? HandLimit { get; init; }
    public List<string> RetainTags { get; init; } = new() { "retain" };
    public UnknownCardPolicy UnknownCardPolicy { get; init; } = UnknownCardPolicy.Fail;
}

public sealed record ActivationAiRules
{
    public bool AutoProcess { get; init; }
    public bool AutoEndAfterAction { get; init; } = true;
}

public sealed record ActivationEventRules
{
    public bool EmitActivationEvents { get; init; } = true;
    public bool EmitDeckEvents { get; init; } = true;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivationDiscardPolicy
{
    None,
    DiscardAll,
    DiscardNonRetain,
    DiscardDownToHandLimit
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnknownCardPolicy
{
    Fail,
    Discard
}
