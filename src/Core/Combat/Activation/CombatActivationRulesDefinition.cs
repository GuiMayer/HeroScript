using System.Collections.Immutable;
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
    public ActivationIntentRules Intents { get; init; } = new();
    public ActivationEventRules Events { get; init; } = new();
}

public sealed record ActivationStartRules
{
    public int DrawCount { get; init; }
    public ActivationActorScope DrawActorScope { get; init; } = ActivationActorScope.PlayerOnly;
}

public sealed record ActivationEndRules
{
    private ImmutableList<string> _retainTags = ["retain"];

    public ActivationDiscardPolicy DiscardPolicy { get; init; } = ActivationDiscardPolicy.None;
    public ActivationActorScope DiscardActorScope { get; init; } = ActivationActorScope.PlayerOnly;
    public int? HandLimit { get; init; }
    public IReadOnlyList<string> RetainTags
    {
        get => _retainTags;
        init => _retainTags = value?.ToImmutableList() ?? [];
    }
    public UnknownCardPolicy UnknownCardPolicy { get; init; } = UnknownCardPolicy.Fail;
}

public sealed record ActivationAiRules
{
    public bool AutoProcess { get; init; }
    public bool AutoEndAfterAction { get; init; } = true;
}

public sealed record ActivationIntentRules
{
    private ImmutableList<string> _gambitIds = [];

    public bool Enabled { get; init; }
    public bool Authoritative { get; init; }
    public ActivationIntentActorScope ActorScope { get; init; } = ActivationIntentActorScope.EnemiesOnly;
    public IReadOnlyList<string> GambitIds
    {
        get => _gambitIds;
        init => _gambitIds = value?.ToImmutableList() ?? [];
    }
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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivationActorScope
{
    PlayerOnly,
    AllActors
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivationIntentActorScope
{
    ActiveActor,
    EnemiesOnly,
    AllActors
}
