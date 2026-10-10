using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.Meta;

/// <summary>Pure reduction over compact authoritative facts. No RNG, clocks, telemetry or runtime profile reads.</summary>
public static class ProfileProgressReducer
{
    public static Result<ProfileProgressCommit?> Plan(ProfileProgressSnapshot basis, ProfileProgressCommit candidate)
    {
        var valid = ProfileProgressPolicyValidator.Validate(candidate.Policy);
        if (valid.IsFailure) return Result<ProfileProgressCommit?>.Failure(valid.Error);
        if (candidate.SchemaVersion != ProfileProgressCommit.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(basis.PlayerId) || string.IsNullOrWhiteSpace(basis.SettingId) ||
            basis.PlayerId != candidate.PlayerId || basis.SettingId != candidate.SettingId ||
            candidate.ProfileSequence != basis.Sequence + 1 || candidate.BasisRevision != basis.Revision ||
            candidate.RunId == Guid.Empty || candidate.CommandId == Guid.Empty || candidate.RunSequence < 1 ||
            string.IsNullOrWhiteSpace(candidate.ContentRevision)) return Result<ProfileProgressCommit?>.Failure("Profile basis/commit identity mismatch");
        if (candidate.Contributions.IsDefault || candidate.Contributions.Any(item => item == null || item.RunId != candidate.RunId || item.Sequence != candidate.RunSequence ||
            item.CommandId != candidate.CommandId || item.ContentRevision != candidate.ContentRevision ||
            item.RootRunId == Guid.Empty || string.IsNullOrWhiteSpace(item.RunDefinitionId) ||
            !Enum.IsDefined(item.Outcome) ||
            item.Kind == UnlockConditionKind.RunCompleted && item.Outcome != Core.Run.RunLifecycleState.Completed ||
            item.Kind == UnlockConditionKind.RunOutcome && item.Outcome == Core.Run.RunLifecycleState.Active ||
            (item.Kind is UnlockConditionKind.NodeCompleted or UnlockConditionKind.EncounterCompleted
                ? string.IsNullOrWhiteSpace(item.NodeId) : item.NodeId != null) ||
            (item.Kind == UnlockConditionKind.EncounterCompleted
                ? item.EncounterOutcome == null || !Enum.IsDefined(item.EncounterOutcome.Value) || item.EncounterOutcome == Core.Combat.Models.CombatStatus.ACTIVE
                : item.EncounterOutcome != null) ||
            !Enum.IsDefined(item.Provenance) || !candidate.Policy.AllowedProvenances.Contains(item.Provenance) ||
            !candidate.Policy.ContributingModeIds.Contains(item.ModeId, StringComparer.Ordinal) ||
            item.Key != CanonicalJson.ComputeHash(new { identity = candidate.Policy.Deduplication == ProfileProgressDeduplication.RootLineage ? item.RootRunId : item.RunId,
                kind = item.Kind, definition = item.RunDefinitionId, node = item.NodeId }) ||
            item.Kind is not (UnlockConditionKind.AttemptStarted or UnlockConditionKind.RunCompleted or UnlockConditionKind.RunOutcome or UnlockConditionKind.NodeCompleted or UnlockConditionKind.EncounterCompleted)))
            return Result<ProfileProgressCommit?>.Failure("Invalid canonical profile contribution");
        var known = basis.Contributions.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var additions = candidate.Contributions.OrderBy(item => item.Key, StringComparer.Ordinal).Where(item => known.Add(item.Key)).ToImmutableArray();
        var all = basis.Contributions.AddRange(additions);
        var unlocked = basis.Grants.Keys.ToHashSet(StringComparer.Ordinal);
        var grants = ImmutableArray.CreateBuilder<UnlockGrantProof>();
        var policyHash = CanonicalJson.ComputeHash(candidate.Policy);
        bool changed;
        do
        {
            changed = false;
            foreach (var unlock in candidate.Policy.Unlocks.OrderBy(item => item.UnlockId, StringComparer.Ordinal))
            {
                if (unlocked.Contains(unlock.UnlockId)) continue;
                var evidence = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
                if (!Satisfied(unlock.Condition, "condition", evidence)) continue;
                unlocked.Add(unlock.UnlockId); changed = true;
                grants.Add(new() { GrantId = CanonicalJson.ComputeHash(new { candidate.PlayerId, candidate.SettingId, unlock.UnlockId }),
                    UnlockId = unlock.UnlockId, PolicyId = candidate.Policy.ProfileProgressPolicyId, PolicyFingerprint = policyHash,
                    ContentRevision = candidate.ContentRevision, RunId = candidate.RunId, Sequence = candidate.RunSequence,
                    CommandId = candidate.CommandId, BasisRevision = basis.Revision, EvidenceCounts = evidence.ToImmutable(),
                    ContributionKeys = additions.Select(item => item.Key).ToImmutableArray(), Targets = unlock.Targets });
            }
        } while (changed);
        if (additions.IsEmpty && grants.Count == 0) return Result<ProfileProgressCommit?>.Success(null);
        var planned = candidate with { Contributions = additions, Grants = grants.ToImmutable(), Revision = string.Empty };
        return Result<ProfileProgressCommit?>.Success(planned with { Revision = CanonicalJson.ComputeHash(planned) });

        bool Satisfied(UnlockConditionDefinition condition, string path, ImmutableSortedDictionary<string, int>.Builder evidence)
        {
            if (condition.Kind is UnlockConditionKind.All or UnlockConditionKind.Any)
            {
                var results = condition.Conditions.Select((child, index) => Satisfied(child, path + "." + index, evidence)).ToArray();
                return condition.Kind == UnlockConditionKind.All ? results.All(item => item) : results.Any(item => item);
            }
            var count = condition.Kind == UnlockConditionKind.UnlockGranted ? (unlocked.Contains(condition.UnlockId!) ? 1 : 0) : all.Count(item =>
                item.Kind == condition.Kind && candidate.Policy.ContributingModeIds.Contains(item.ModeId, StringComparer.Ordinal) &&
                candidate.Policy.AllowedProvenances.Contains(item.Provenance) &&
                (condition.RunDefinitionId == null || condition.RunDefinitionId == item.RunDefinitionId) &&
                (condition.NodeId == null || condition.NodeId == item.NodeId) &&
                (condition.Kind != UnlockConditionKind.RunOutcome || condition.Outcomes.Contains(item.Outcome)) &&
                (condition.Kind != UnlockConditionKind.EncounterCompleted || condition.EncounterOutcomes.Contains(item.EncounterOutcome!.Value)));
            evidence[path] = count; return count >= condition.Count;
        }
    }

    public static Result<ProfileProgressSnapshot> Apply(ProfileProgressSnapshot basis, ProfileProgressCommit proof)
    {
        var planned = Plan(basis, proof);
        if (planned.IsFailure || planned.Value == null || CanonicalJson.ComputeHash(planned.Value) != CanonicalJson.ComputeHash(proof))
            return Result<ProfileProgressSnapshot>.Failure(planned.IsFailure ? planned.Error : "Profile proof differs from canonical reduction");
        return Result<ProfileProgressSnapshot>.Success(basis with { Sequence = proof.ProfileSequence, Revision = proof.Revision,
            Contributions = basis.Contributions.AddRange(proof.Contributions), Grants = basis.Grants.SetItems(proof.Grants.Select(grant =>
                new KeyValuePair<string, UnlockGrantProof>(grant.UnlockId, grant))) });
    }

    public static Result<ProfileProgressSnapshot> Rebuild(string player, string setting, IEnumerable<ProfileProgressCommit> commits)
    {
        var snapshot = ProfileProgressSnapshot.Empty(player, setting);
        foreach (var proof in commits.Where(item => item.PlayerId == player && item.SettingId == setting).OrderBy(item => item.ProfileSequence))
        {
            var reduced = Apply(snapshot, proof);
            if (reduced.IsFailure) return reduced;
            snapshot = reduced.Value;
        }
        return Result<ProfileProgressSnapshot>.Success(snapshot);
    }
}
