using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Content;
using Core.Run;

namespace Core.Meta;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProfileProgressProvenance { Published, Experimental, Sandbox, DevModder, Branch }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProfileProgressDeduplication { RootLineage, RunInstance }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnlockTargetKind { Card, CardUpgrade, Relic }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnlockConditionKind { Unspecified, All, Any, AttemptStarted, RunCompleted, NodeCompleted, EncounterCompleted, RunOutcome, UnlockGranted }

public sealed record UnlockTargetDefinition
{
    public UnlockTargetKind Kind { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    [JsonIgnore]
    public string ContentKind => Kind switch { UnlockTargetKind.Card => "cards", UnlockTargetKind.CardUpgrade => "card-upgrades", _ => "relics" };
}

public sealed record UnlockConditionDefinition
{
    public UnlockConditionKind Kind { get; init; }
    public int Count { get; init; } = 1;
    public string? RunDefinitionId { get; init; }
    public string? NodeId { get; init; }
    public string? UnlockId { get; init; }
    public ImmutableArray<RunLifecycleState> Outcomes { get; init; } = [];
    public ImmutableArray<Core.Combat.Models.CombatStatus> EncounterOutcomes { get; init; } = [Core.Combat.Models.CombatStatus.VICTORY];
    public ImmutableArray<UnlockConditionDefinition> Conditions { get; init; } = [];
}

public sealed record UnlockDefinition
{
    public string UnlockId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ImmutableArray<UnlockTargetDefinition> Targets { get; init; } = [];
    public UnlockConditionDefinition Condition { get; init; } = new();
}

/// <summary>Option availability, not permanent numerical power. Pinned independently of run-flow progression.</summary>
public sealed record ProfileProgressPolicyDefinition
{
    public string ProfileProgressPolicyId { get; init; } = string.Empty;
    public ImmutableArray<string> ContributingModeIds { get; init; } = [];
    public ImmutableArray<ProfileProgressProvenance> AllowedProvenances { get; init; } = [ProfileProgressProvenance.Published];
    public ProfileProgressDeduplication Deduplication { get; init; }
    public ImmutableArray<UnlockDefinition> Unlocks { get; init; } = [];
}

public static class ProfileProgressPolicyValidator
{
    public static Result Validate(ProfileProgressPolicyDefinition policy, ContentRuntime? runtime = null)
    {
        if (policy == null || string.IsNullOrWhiteSpace(policy.ProfileProgressPolicyId) || !Enum.IsDefined(policy.Deduplication) ||
            policy.ContributingModeIds.IsDefaultOrEmpty || policy.ContributingModeIds.Length > 256 || policy.ContributingModeIds.Any(string.IsNullOrWhiteSpace) ||
            policy.ContributingModeIds.Distinct(StringComparer.Ordinal).Count() != policy.ContributingModeIds.Length ||
            policy.AllowedProvenances.IsDefaultOrEmpty || policy.AllowedProvenances.Any(item => !Enum.IsDefined(item)) ||
            policy.AllowedProvenances.Distinct().Count() != policy.AllowedProvenances.Length ||
            policy.Unlocks.IsDefaultOrEmpty || policy.Unlocks.Length > 256 ||
            policy.Unlocks.Any(item => item == null) || policy.Unlocks.Select(item => item.UnlockId).Distinct(StringComparer.Ordinal).Count() != policy.Unlocks.Length)
            return Result.Failure("Profile progression requires unique unlocks, contributing modes, valid provenances and deduplication");
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var work = 0;
        foreach (var unlock in policy.Unlocks)
        {
            if (string.IsNullOrWhiteSpace(unlock.UnlockId) || unlock.Targets.IsDefaultOrEmpty || unlock.Targets.Length > 256 ||
                unlock.Targets.Any(target => target == null || !Enum.IsDefined(target.Kind) || string.IsNullOrWhiteSpace(target.DefinitionId)) ||
                unlock.Targets.Distinct().Count() != unlock.Targets.Length)
                return Result.Failure("Unlocks require an ID and unique supported option targets");
            var refs = new HashSet<string>(StringComparer.Ordinal);
            var valid = Condition(unlock.Condition, refs, 0);
            if (valid.IsFailure) return valid;
            dependencies[unlock.UnlockId] = refs;
            if (runtime != null)
                foreach (var target in unlock.Targets)
                    if (!runtime.GetDefinitions(target.ContentKind).ContainsKey(target.DefinitionId))
                        return Result.Failure($"Unknown unlock target: {target.ContentKind}/{target.DefinitionId}");
        }
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (visited.Contains(id)) return true;
            if (!dependencies.ContainsKey(id) || !visiting.Add(id)) return false;
            foreach (var dependency in dependencies[id]) if (!Visit(dependency)) return false;
            visiting.Remove(id); visited.Add(id); return true;
        }
        if (dependencies.Keys.Any(id => !Visit(id))) return Result.Failure("Unlock dependencies contain a cycle or unknown ID");
        if (runtime != null && policy.ContributingModeIds.Any(id => !runtime.GetDefinitions("modes").ContainsKey(id)))
            return Result.Failure("Unknown contributing mode in profile progression policy");
        return Result.Success();

        Result Condition(UnlockConditionDefinition condition, HashSet<string> refs, int depth)
        {
            if (++work > 2048 || depth > 8 || condition == null || !Enum.IsDefined(condition.Kind) || condition.Kind == UnlockConditionKind.Unspecified ||
                condition.Conditions.IsDefault || condition.Outcomes.IsDefault || condition.EncounterOutcomes.IsDefaultOrEmpty ||
                condition.EncounterOutcomes.Distinct().Count() != condition.EncounterOutcomes.Length ||
                condition.EncounterOutcomes.Any(outcome => !Enum.IsDefined(outcome) || outcome == Core.Combat.Models.CombatStatus.ACTIVE) ||
                condition.Kind != UnlockConditionKind.EncounterCompleted &&
                !condition.EncounterOutcomes.SequenceEqual([Core.Combat.Models.CombatStatus.VICTORY]))
                return Result.Failure("Unsupported or excessive unlock condition");
            if (condition.Kind is UnlockConditionKind.All or UnlockConditionKind.Any)
            {
                if (condition.Conditions.IsDefaultOrEmpty || condition.Count != 1 || condition.NodeId != null ||
                    condition.UnlockId != null || condition.RunDefinitionId != null || !condition.Outcomes.IsEmpty)
                    return Result.Failure("Composed unlock conditions only accept their typed children");
                foreach (var child in condition.Conditions) { var valid = Condition(child, refs, depth + 1); if (valid.IsFailure) return valid; }
                return Result.Success();
            }
            if (condition.Count is < 1 or > 1_000_000 || !condition.Conditions.IsEmpty || condition.Outcomes.Any(outcome =>
                !Enum.IsDefined(outcome) || outcome == RunLifecycleState.Active) ||
                condition.Outcomes.Distinct().Count() != condition.Outcomes.Length ||
                condition.RunDefinitionId != null && string.IsNullOrWhiteSpace(condition.RunDefinitionId)) return Result.Failure("Invalid unlock condition threshold/outcome");
            if (condition.Kind == UnlockConditionKind.UnlockGranted)
            {
                if (string.IsNullOrWhiteSpace(condition.UnlockId) || condition.Count != 1 || condition.NodeId != null ||
                    condition.RunDefinitionId != null || !condition.Outcomes.IsEmpty) return Result.Failure("Invalid unlock dependency condition");
                refs.Add(condition.UnlockId); return Result.Success();
            }
            if (condition.UnlockId != null || condition.Kind != UnlockConditionKind.RunOutcome && !condition.Outcomes.IsEmpty ||
                condition.Kind == UnlockConditionKind.RunOutcome && condition.Outcomes.IsDefaultOrEmpty)
                return Result.Failure("Outcomes must be explicitly selected by a RunOutcome condition");
            var nodeCondition = condition.Kind is UnlockConditionKind.NodeCompleted or UnlockConditionKind.EncounterCompleted;
            if (nodeCondition && (string.IsNullOrWhiteSpace(condition.RunDefinitionId) || string.IsNullOrWhiteSpace(condition.NodeId)) ||
                !nodeCondition && condition.NodeId != null || condition.EncounterOutcomes.Any(outcome =>
                    !Enum.IsDefined(outcome) || outcome == Core.Combat.Models.CombatStatus.ACTIVE))
                return Result.Failure("Node/encounter conditions require an explicit run definition and node");
            if (runtime != null && condition.RunDefinitionId != null)
            {
                var run = runtime.GetDefinition<RunDefinition>("runs", condition.RunDefinitionId);
                if (run.IsFailure || nodeCondition && !run.Value.MapNodes.Any(node => node.NodeId == condition.NodeId &&
                    (condition.Kind != UnlockConditionKind.EncounterCompleted || node.Activity.Type == RunActivityType.Encounter)))
                    return Result.Failure("Unknown run/node in unlock condition");
            }
            return Result.Success();
        }
    }
}
