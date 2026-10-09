using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Common;

namespace Core.Run;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActorResourceLifecycleAction { Unspecified, PreserveCurrent, ResetToMaximum, ResetToConfiguredValue, EncounterOnly }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissingActorResourceBehavior { Error, Ignore }

/// <summary>Transfers current values only. Persistent bounds never inherit encounter buffs.</summary>
public sealed record ActorResourceLifecycleRule
{
    public string ResourceId { get; init; } = string.Empty;
    public ActorResourceLifecycleAction Entry { get; init; }
    public ActorResourceLifecycleAction Victory { get; init; }
    public ActorResourceLifecycleAction Defeat { get; init; }
    public ActorResourceLifecycleAction Draw { get; init; }
    public ActorResourceLifecycleAction Abandoned { get; init; }
    public ActorResourceLifecycleAction Retry { get; init; }
    public float? ConfiguredValue { get; init; }
    public MissingActorResourceBehavior MissingResource { get; init; }

    public ActorResourceLifecycleAction Exit(CombatStatus outcome, bool retry) => retry ? Retry : outcome switch
    {
        CombatStatus.VICTORY => Victory,
        CombatStatus.DEFEAT => Defeat,
        CombatStatus.DRAW => Draw,
        CombatStatus.ABANDONED => Abandoned,
        _ => ActorResourceLifecycleAction.Unspecified
    };
}

public sealed record ActorResourceLifecyclePolicyDefinition
{
    public string ActorResourceLifecyclePolicyId { get; init; } = string.Empty;
    public ImmutableArray<ActorResourceLifecycleRule> Rules { get; init; } = [];
}

public static class ActorResourceLifecyclePolicyValidator
{
    public static Result ValidatePlayer(ActorResourceLifecyclePolicyDefinition policy, EntityState? player)
    {
        var valid = Validate(policy);
        if (valid.IsFailure) return valid;
        if (player == null) return Result.Failure("Actor resource lifecycle requires a persistent player definition");
        var resources = player.Component<ResourceEntityComponentState>()?.State;
        foreach (var rule in policy.Rules)
        {
            var actions = new[] { rule.Entry, rule.Victory, rule.Defeat, rule.Draw, rule.Abandoned, rule.Retry };
            if (rule.MissingResource == MissingActorResourceBehavior.Error &&
                actions.Any(action => action != ActorResourceLifecycleAction.EncounterOnly) &&
                resources?.Contains(rule.ResourceId) != true)
                return Result.Failure($"Persistent player is missing policy resource '{rule.ResourceId}'");
        }
        return Result.Success();
    }

    public static Result Validate(ActorResourceLifecyclePolicyDefinition policy)
    {
        if (string.IsNullOrWhiteSpace(policy.ActorResourceLifecyclePolicyId) || policy.Rules.IsDefaultOrEmpty)
            return Result.Failure("Actor resource lifecycle policy requires an id and rules");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in policy.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.ResourceId) || !ids.Add(rule.ResourceId))
                return Result.Failure("Actor resource lifecycle resource ids must be nonempty and unique");
            var actions = new[] { rule.Entry, rule.Victory, rule.Defeat, rule.Draw, rule.Abandoned, rule.Retry };
            if (actions.Any(action => !Enum.IsDefined(action) || action == ActorResourceLifecycleAction.Unspecified) ||
                !Enum.IsDefined(rule.MissingResource))
                return Result.Failure($"Resource '{rule.ResourceId}' requires explicit entry, outcome and retry actions");
            if (rule.ConfiguredValue.HasValue && !float.IsFinite(rule.ConfiguredValue.Value) ||
                actions.Contains(ActorResourceLifecycleAction.ResetToConfiguredValue) && rule.ConfiguredValue == null)
                return Result.Failure($"Resource '{rule.ResourceId}' requires a finite configured reset value");
        }
        return Result.Success();
    }
}
