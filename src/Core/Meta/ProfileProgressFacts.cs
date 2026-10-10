using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.Combat.Models;
using Core.Determinism;
using Core.Run;

namespace Core.Meta;

public sealed record ProfileProgressContribution
{
    public string Key { get; init; } = string.Empty;
    public Guid RunId { get; init; }
    public Guid RootRunId { get; init; }
    public int Sequence { get; init; }
    public Guid CommandId { get; init; }
    public string ModeId { get; init; } = string.Empty;
    public string RunDefinitionId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public ProfileProgressProvenance Provenance { get; init; }
    public UnlockConditionKind Kind { get; init; }
    public string? NodeId { get; init; }
    public RunLifecycleState Outcome { get; init; }
    public CombatStatus? EncounterOutcome { get; init; }
}

public sealed record UnlockGrantProof
{
    public string GrantId { get; init; } = string.Empty;
    public string UnlockId { get; init; } = string.Empty;
    public string PolicyId { get; init; } = string.Empty;
    public string PolicyFingerprint { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public Guid RunId { get; init; }
    public int Sequence { get; init; }
    public Guid CommandId { get; init; }
    public string BasisRevision { get; init; } = string.Empty;
    public ImmutableSortedDictionary<string, int> EvidenceCounts { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableArray<string> ContributionKeys { get; init; } = [];
    public ImmutableArray<UnlockTargetDefinition> Targets { get; init; } = [];
}

/// <summary>Immutable delta inside the SAME atomic run commit. No independent authoritative profile save.</summary>
public sealed record ProfileProgressCommit
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string PlayerId { get; init; } = string.Empty;
    public string SettingId { get; init; } = string.Empty;
    public long ProfileSequence { get; init; }
    public string BasisRevision { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public Guid RunId { get; init; }
    public int RunSequence { get; init; }
    public Guid CommandId { get; init; }
    public string ContentRevision { get; init; } = string.Empty;
    public ProfileProgressPolicyDefinition Policy { get; init; } = new();
    public ImmutableArray<ProfileProgressContribution> Contributions { get; init; } = [];
    public ImmutableArray<UnlockGrantProof> Grants { get; init; } = [];

    public void ValidateEnvelope(Guid runId, int sequence, Guid commandId)
    {
        if (SchemaVersion != CurrentSchemaVersion || RunId != runId || RunSequence != sequence || CommandId != commandId ||
            ProfileSequence < 1 || string.IsNullOrWhiteSpace(PlayerId) || string.IsNullOrWhiteSpace(SettingId) ||
            string.IsNullOrWhiteSpace(BasisRevision) || string.IsNullOrWhiteSpace(ContentRevision) ||
            Contributions.IsDefault || Grants.IsDefault || ProfileProgressPolicyValidator.Validate(Policy).IsFailure ||
            Revision != CanonicalJson.ComputeHash(this with { Revision = string.Empty }))
            throw new InvalidOperationException("Invalid profile progress commit proof");
    }
}

public sealed record ProfileProgressSnapshot
{
    public string PlayerId { get; init; } = string.Empty;
    public string SettingId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public string Revision { get; init; } = string.Empty;
    public ImmutableArray<ProfileProgressContribution> Contributions { get; init; } = [];
    public ImmutableSortedDictionary<string, UnlockGrantProof> Grants { get; init; } = ImmutableSortedDictionary<string, UnlockGrantProof>.Empty.WithComparers(StringComparer.Ordinal);
    public static ProfileProgressSnapshot Empty(string player, string setting) => new()
    { PlayerId = player, SettingId = setting, Revision = CanonicalJson.ComputeHash(new { player, setting, sequence = 0L }) };
}

public interface IProfileProgressSnapshotReader
{
    Task<ProfileProgressSnapshot> ReadAsync(string playerId, string settingId, CancellationToken ct = default);
}

/// <summary>Reads stable meta proofs without requiring the historical gameplay execution version.</summary>
public interface IProfileProgressCommitReader
{
    Task<IReadOnlyList<ProfileProgressCommit>> LoadProfileProgressCommitsAsync(string playerId, string settingId, CancellationToken ct = default);
}

public static class ProfileProgressFacts
{
    public static ImmutableArray<ProfileProgressContribution> Extract(RunState? before, RunState after, RunCommandIdentity command)
    {
        var mode = after.ResolvedMode;
        var policy = mode?.ProfileProgressPolicy;
        if (mode == null || policy == null || string.IsNullOrWhiteSpace(after.ModeId) || after.Lineage?.InternalSimulation == true) return [];
        // Fork creation copies a historical state; it is not new gameplay completion.
        if (before == null && after.Lineage?.ParentRunId != null) return [];
        var provenance = after.Lineage?.ParentRunId != null ? ProfileProgressProvenance.Branch :
            mode.Definition.ProfileProgressProvenance is ProfileProgressProvenance.DevModder or ProfileProgressProvenance.Sandbox
                ? mode.Definition.ProfileProgressProvenance.Value :
            after.Scenario != null ? ProfileProgressProvenance.Sandbox :
            (mode.CapabilityPolicy.AllowRunResourceCheats || mode.CapabilityPolicy.AllowCardZoneCheats
                ? mode.Definition.ProfileProgressProvenance is ProfileProgressProvenance.DevModder
                    ? ProfileProgressProvenance.DevModder : ProfileProgressProvenance.Experimental
                : mode.Definition.ProfileProgressProvenance ?? ProfileProgressProvenance.Published);
        if (!policy.ContributingModeIds.Contains(after.ModeId, StringComparer.Ordinal) || !policy.AllowedProvenances.Contains(provenance)) return [];
        var result = ImmutableArray.CreateBuilder<ProfileProgressContribution>();
        if (before == null && after.Lineage?.ParentRunId == null) Add(UnlockConditionKind.AttemptStarted);
        if (before?.Lifecycle == RunLifecycleState.Active && after.Lifecycle != RunLifecycleState.Active)
        {
            Add(UnlockConditionKind.RunOutcome);
            if (after.Lifecycle == RunLifecycleState.Completed) Add(UnlockConditionKind.RunCompleted);
        }
        foreach (var node in after.Map.ResolvedNodeIds.Except(before?.Map.ResolvedNodeIds ?? [], StringComparer.Ordinal).Order(StringComparer.Ordinal))
            Add(UnlockConditionKind.NodeCompleted, node);
        foreach (var encounter in after.Encounters.Where(item => item.Resolved &&
                     !(before?.Encounters.Any(previous => previous.Combat.CombatId == item.Combat.CombatId && previous.Resolved) ?? false))
                     .OrderBy(item => item.NodeId, StringComparer.Ordinal))
            Add(UnlockConditionKind.EncounterCompleted, encounter.NodeId, encounter.Combat.Status);
        return result.ToImmutable();

        void Add(UnlockConditionKind kind, string? node = null, CombatStatus? encounter = null)
        {
            var root = after.Lineage?.RootRunId ?? after.RunId;
            var identity = policy.Deduplication == ProfileProgressDeduplication.RootLineage ? root : after.RunId;
            var definition = after.ResolvedMode!.Definition.RunDefinitionId ?? string.Empty;
            result.Add(new() { Key = CanonicalJson.ComputeHash(new { identity, kind, definition, node }),
                RunId = after.RunId, RootRunId = root, Sequence = after.Sequence, CommandId = command.CommandId,
                ModeId = after.ModeId!, RunDefinitionId = definition, ContentRevision = after.Determinism.ContentRevision,
                Provenance = provenance, Kind = kind, NodeId = node, Outcome = after.Lifecycle, EncounterOutcome = encounter });
        }
    }
}
