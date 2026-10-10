using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Determinism;
using Core.Meta;

namespace Core.Run;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunEligibilitySource { Profile, Fixed, Unrestricted }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmptyEligiblePoolPolicy { Reject, AllowEmpty }

/// <summary>Published mode configuration, never a list supplied by a gameplay client.</summary>
public sealed record RunEligibilityRules
{
    public RunEligibilitySource Source { get; init; }
    public ImmutableArray<string> FixedUnlockIds { get; init; } = [];
    public EmptyEligiblePoolPolicy EmptyPools { get; init; }
}

public sealed record RunEligibilitySnapshot
{
    public string PlayerId { get; init; } = string.Empty;
    public string SettingId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string PolicyId { get; init; } = string.Empty;
    public string PolicyFingerprint { get; init; } = string.Empty;
    public RunEligibilitySource Source { get; init; }
    public EmptyEligiblePoolPolicy EmptyPools { get; init; }
    public long ProfileSequence { get; init; }
    public string ProfileRevision { get; init; } = string.Empty;
    public ImmutableArray<string> UnlockIds { get; init; } = [];
    public ImmutableArray<UnlockTargetDefinition> GatedTargets { get; init; } = [];
    public ImmutableArray<UnlockTargetDefinition> AvailableTargets { get; init; } = [];
    public string EffectiveFingerprint { get; init; } = string.Empty;

    // History revision is provenance, not an effective gameplay input.
    public string ComputeEffectiveFingerprint() => CanonicalJson.ComputeHash(new
    { PlayerId, SettingId, ContentRevision, PolicyId, PolicyFingerprint, Source, EmptyPools, GatedTargets, AvailableTargets });
    public bool Allows(UnlockTargetKind kind, string id) =>
        !GatedTargets.Any(target => target.Kind == kind && target.DefinitionId == id) ||
        AvailableTargets.Any(target => target.Kind == kind && target.DefinitionId == id);
}

public static class RunEligibility
{
    public static Result ValidateRules(ResolvedGameMode mode)
    {
        var rules = mode.Definition.EligibilityRules;
        if (rules == null) return Result.Success();
        if (mode.ProfileProgressPolicy == null || !Enum.IsDefined(rules.Source) || !Enum.IsDefined(rules.EmptyPools) ||
            rules.FixedUnlockIds.IsDefault || rules.FixedUnlockIds.Length > 256 ||
            rules.FixedUnlockIds.Any(string.IsNullOrWhiteSpace) ||
            rules.FixedUnlockIds.Distinct(StringComparer.Ordinal).Count() != rules.FixedUnlockIds.Length ||
            rules.Source != RunEligibilitySource.Fixed && !rules.FixedUnlockIds.IsEmpty ||
            rules.FixedUnlockIds.Any(id => !mode.ProfileProgressPolicy.Unlocks.Any(item => item.UnlockId == id)))
            return Result.Failure("Invalid mode eligibility rules or unknown fixed unlock");
        if (rules.Source == RunEligibilitySource.Unrestricted &&
            !mode.CapabilityPolicy.AllowCardZoneCheats && !mode.CapabilityPolicy.AllowRunResourceCheats)
            return Result.Failure("Unrestricted eligibility requires an explicitly cheat-enabled mode");
        return Result.Success();
    }

    public static Result<RunEligibilitySnapshot?> Capture(ResolvedGameMode mode, string player, string setting,
        string revision, ProfileProgressSnapshot? progress)
    {
        var valid = ValidateRules(mode);
        if (valid.IsFailure) return Result<RunEligibilitySnapshot?>.Failure(valid.Error);
        var policy = mode.ProfileProgressPolicy;
        if (policy == null) return Result<RunEligibilitySnapshot?>.Success(null);
        var rules = mode.Definition.EligibilityRules ?? new();
        if (rules.Source == RunEligibilitySource.Profile &&
            (progress == null || progress.PlayerId != player || progress.SettingId != setting))
            return Result<RunEligibilitySnapshot?>.Failure("Authoritative setting profile is required for eligibility capture");
        var unlocked = rules.Source switch
        {
            RunEligibilitySource.Fixed => rules.FixedUnlockIds,
            RunEligibilitySource.Unrestricted => policy.Unlocks.Select(item => item.UnlockId).ToImmutableArray(),
            _ => progress!.Grants.Keys.ToImmutableArray()
        };
        var gated = Canonical(policy.Unlocks.SelectMany(item => item.Targets));
        var available = rules.Source == RunEligibilitySource.Profile
            ? Canonical(progress!.Grants.Values.SelectMany(item => item.Targets).Where(gated.Contains))
            : Canonical(policy.Unlocks.Where(item => unlocked.Contains(item.UnlockId, StringComparer.Ordinal)).SelectMany(item => item.Targets));
        var snapshot = new RunEligibilitySnapshot
        {
            PlayerId = player, SettingId = setting, ContentRevision = revision,
            PolicyId = policy.ProfileProgressPolicyId, PolicyFingerprint = CanonicalJson.ComputeHash(policy),
            Source = rules.Source, EmptyPools = rules.EmptyPools,
            ProfileSequence = rules.Source == RunEligibilitySource.Profile ? progress!.Sequence : 0,
            ProfileRevision = rules.Source == RunEligibilitySource.Profile ? progress!.Revision : string.Empty,
            UnlockIds = unlocked.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            GatedTargets = gated, AvailableTargets = available
        };
        return Result<RunEligibilitySnapshot?>.Success(snapshot with { EffectiveFingerprint = snapshot.ComputeEffectiveFingerprint() });
    }

    private static ImmutableArray<UnlockTargetDefinition> Canonical(IEnumerable<UnlockTargetDefinition> targets) =>
        targets.Distinct().OrderBy(item => item.Kind).ThenBy(item => item.DefinitionId, StringComparer.Ordinal).ToImmutableArray();

    public static Result ValidateSnapshot(RunState run)
    {
        var provenance = RunProgressProvenance.Validate(null, run);
        if (provenance.IsFailure) return provenance;
        var snapshot = run.Eligibility;
        if (snapshot == null) return run.ResolvedMode?.ProfileProgressPolicy == null ? Result.Success()
            : Result.Failure("Run is missing its frozen eligibility snapshot");
        if (snapshot.PlayerId != run.PlayerEntityId || snapshot.SettingId != run.SettingId ||
            string.IsNullOrWhiteSpace(snapshot.ContentRevision) || string.IsNullOrWhiteSpace(snapshot.PolicyId) ||
            string.IsNullOrWhiteSpace(snapshot.PolicyFingerprint) || !Enum.IsDefined(snapshot.Source) || !Enum.IsDefined(snapshot.EmptyPools) ||
            snapshot.ProfileSequence < 0 || snapshot.GatedTargets.IsDefault || snapshot.AvailableTargets.IsDefault || snapshot.UnlockIds.IsDefault ||
            snapshot.GatedTargets.Any(item => item == null || !Enum.IsDefined(item.Kind) || string.IsNullOrWhiteSpace(item.DefinitionId)) ||
            snapshot.AvailableTargets.Any(item => item == null || !Enum.IsDefined(item.Kind) || string.IsNullOrWhiteSpace(item.DefinitionId)) ||
            snapshot.UnlockIds.Any(string.IsNullOrWhiteSpace) ||
            !snapshot.GatedTargets.SequenceEqual(Canonical(snapshot.GatedTargets)) ||
            !snapshot.AvailableTargets.SequenceEqual(Canonical(snapshot.AvailableTargets)) ||
            snapshot.AvailableTargets.Any(item => !snapshot.GatedTargets.Contains(item)) ||
            !snapshot.UnlockIds.SequenceEqual(snapshot.UnlockIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) ||
            snapshot.Source == RunEligibilitySource.Profile && string.IsNullOrWhiteSpace(snapshot.ProfileRevision) ||
            snapshot.EffectiveFingerprint != snapshot.ComputeEffectiveFingerprint())
            return Result.Failure("Invalid frozen run eligibility snapshot");
        if (run.ResolvedMode?.ProfileProgressPolicy is not { } policy || policy.ProfileProgressPolicyId != snapshot.PolicyId ||
            !Canonical(policy.Unlocks.SelectMany(unlock => unlock.Targets)).SequenceEqual(snapshot.GatedTargets))
            return Result.Failure("Mode catalog changes the frozen eligibility gates; start a new run");
        return Result.Success();
    }

    public static bool Allows(RunState run, UnlockTargetKind kind, string id) => run.Eligibility?.Allows(kind, id) ?? true;
    public static Result Require(RunState run, UnlockTargetKind kind, string id) => Allows(run, kind, id)
        ? Result.Success() : Result.Failure($"Content is not eligible for this run: {kind}/{id}");

    /// <summary>Final acquisition fence shared by every command, including effect/zone-driven grants.</summary>
    public static Result ValidateCandidate(RunState? before, RunState after)
    {
        var provenance = RunProgressProvenance.Validate(before, after);
        if (provenance.IsFailure) return provenance;
        var valid = ValidateSnapshot(after);
        if (valid.IsFailure) return valid;
        if (before != null && CanonicalJson.ComputeHash(before.Eligibility) != CanonicalJson.ComputeHash(after.Eligibility))
            return Result.Failure("A run cannot recapture or replace its eligibility");
        if (after.Eligibility == null) return Result.Success();
        foreach (var card in after.Deck.Topology.Instances.Values)
        {
            var previous = before?.Deck.Topology.Instances.GetValueOrDefault(card.CardInstanceId);
            if (previous?.DefinitionId != card.DefinitionId)
            {
                valid = Require(after, UnlockTargetKind.Card, card.DefinitionId);
                if (valid.IsFailure) return valid;
            }
            foreach (var entry in card.Upgrades.Where(item => item.Operation != CardTransformationOperation.Remove &&
                         !(previous?.Upgrades.Contains(item) ?? false)))
            {
                valid = Require(after, UnlockTargetKind.CardUpgrade, entry.UpgradeId);
                if (valid.IsFailure) return valid;
            }
        }
        foreach (var relic in after.Relics.Where(item => !(before?.Relics.Any(old => old.RelicInstanceId == item.RelicInstanceId &&
                     old.DefinitionId == item.DefinitionId && old.Stacks >= item.Stacks) ?? false)))
        {
            valid = Require(after, UnlockTargetKind.Relic, relic.DefinitionId);
            if (valid.IsFailure) return valid;
        }
        return Result.Success();
    }
}
