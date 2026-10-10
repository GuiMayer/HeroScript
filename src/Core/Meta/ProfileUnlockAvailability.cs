using System.Collections.Immutable;
using Core.Content;

namespace Core.Meta;

public sealed record ProfileUnlockOption(string PolicyId, string UnlockId, string DisplayName,
    UnlockConditionDefinition Condition, bool ConditionSatisfied, ImmutableSortedDictionary<string, int> EvidenceCounts,
    ImmutableArray<UnlockTargetDefinition> Targets, ImmutableArray<UnlockTargetDefinition> AvailableTargets, UnlockGrantProof? Proof);
public sealed record ProfileUnlockAvailability(string ContentRevision, string ProgressRevision,
    ImmutableArray<ProfileUnlockOption> Options);

/// <summary>Explains a published catalog; it does not grant options or recapture run eligibility.</summary>
public static class ProfileUnlockAvailabilityProjection
{
    public static ProfileUnlockAvailability Project(ContentRuntime runtime, ProfileProgressSnapshot progress)
    {
        var options = ImmutableArray.CreateBuilder<ProfileUnlockOption>();
        foreach (var id in runtime.GetDefinitions("profile-progress-policies").Keys.Order(StringComparer.Ordinal))
        {
            var policy = runtime.GetDefinition<ProfileProgressPolicyDefinition>("profile-progress-policies", id).Value;
            foreach (var unlock in policy.Unlocks.OrderBy(item => item.UnlockId, StringComparer.Ordinal))
            {
                var evidence = ProfileProgressConditions.Evaluate(policy, unlock.Condition, progress.Contributions, progress.Grants.Keys);
                progress.Grants.TryGetValue(unlock.UnlockId, out var proof);
                var available = unlock.Targets.Where(target => proof?.Targets.Contains(target) == true &&
                    runtime.GetDefinitions(target.ContentKind).ContainsKey(target.DefinitionId)).ToImmutableArray();
                options.Add(new(id, unlock.UnlockId, unlock.DisplayName, unlock.Condition, evidence.Satisfied,
                    evidence.Counts, unlock.Targets, available, proof));
            }
        }
        return new(runtime.Manifest.Revision, progress.Revision, options.ToImmutable());
    }
}

public sealed record UnlockConditionEvidence(bool Satisfied, ImmutableSortedDictionary<string, int> Counts);
public static class ProfileProgressConditions
{
    public static UnlockConditionEvidence Evaluate(ProfileProgressPolicyDefinition policy, UnlockConditionDefinition root,
        IEnumerable<ProfileProgressContribution> contributions, IEnumerable<string> unlockIds)
    {
        var all = contributions.ToArray();
        var unlocked = unlockIds.ToHashSet(StringComparer.Ordinal);
        var evidence = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var satisfied = Visit(root, "condition");
        return new(satisfied, evidence.ToImmutable());

        bool Visit(UnlockConditionDefinition condition, string path)
        {
            if (condition.Kind is UnlockConditionKind.All or UnlockConditionKind.Any)
            {
                var results = condition.Conditions.Select((child, index) => Visit(child, path + "." + index)).ToArray();
                return condition.Kind == UnlockConditionKind.All ? results.All(item => item) : results.Any(item => item);
            }
            var count = condition.Kind == UnlockConditionKind.UnlockGranted ? (unlocked.Contains(condition.UnlockId!) ? 1 : 0) : all.Count(item =>
                item.Kind == condition.Kind && policy.ContributingModeIds.Contains(item.ModeId, StringComparer.Ordinal) &&
                policy.AllowedProvenances.Contains(item.Provenance) &&
                (condition.RunDefinitionId == null || condition.RunDefinitionId == item.RunDefinitionId) &&
                (condition.NodeId == null || condition.NodeId == item.NodeId) &&
                (condition.Kind != UnlockConditionKind.RunOutcome || condition.Outcomes.Contains(item.Outcome)) &&
                (condition.Kind != UnlockConditionKind.EncounterCompleted || condition.EncounterOutcomes.Contains(item.EncounterOutcome!.Value)));
            evidence[path] = count;
            return count >= condition.Count;
        }
    }
}
