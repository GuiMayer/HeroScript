using Core.Abstractions.Persistence;
using Core.Caching;
using Core.Common;
using Core.Determinism;
using Core.Meta;

namespace Core.Run;

public sealed record RunCreationReceiptIdentity(string Key, string RequestFingerprint);

/// <summary>Storage exposes a lease and a read projection, not an independent creation journal.</summary>
public interface IRunCreationStorage
{
    Task<IDisposable> AcquireCreationLeaseAsync(string key, CancellationToken ct = default);
    Task<RunState?> FindCreationStateAsync(RunCreationReceiptIdentity identity, CancellationToken ct = default);
}

/// <summary>Owns external profile inputs and creation retries. Never depends on IRunManager.</summary>
internal sealed class RunCreationService(IRunCommitStore? store)
{
    private readonly LruCache<string, RunState> _receipts = new(256);

    public static RunCreationReceiptIdentity Identify(RunStartOptions options)
    {
        var fingerprint = CanonicalJson.ComputeHash(options with { CapturedEligibility = null, CapturedCreationIdentity = null });
        var attempt = options.CreationCommandId?.ToString("D") ?? (options.Seed.HasValue ? fingerprint : Guid.NewGuid().ToString("N")); // nondeterministic-boundary: unseeded creation nonce captured in the first commit.
        var key = CanonicalJson.ComputeHash(new { player = options.PlayerEntityId, setting = options.SettingId ?? options.ConfigName,
            attempt });
        return new(key, fingerprint);
    }

    public IDisposable? Acquire(RunCreationReceiptIdentity identity) => store is IRunCreationStorage storage
        ? storage.AcquireCreationLeaseAsync(identity.Key).GetAwaiter().GetResult() : null;

    public Result<RunState?> Find(RunCreationReceiptIdentity identity)
    {
        if (_receipts.TryGetValue(identity.Key, out var cached) && cached != null) return Compare(cached, identity);
        RunState? found = null;
        if (store is IRunCreationStorage storage) found = storage.FindCreationStateAsync(identity).GetAwaiter().GetResult();
        else if (store != null)
        {
            foreach (var runId in store.ListRunIdsAsync().GetAwaiter().GetResult() ?? [])
            {
                var initial = store.LoadStateAsync(runId, 1).GetAwaiter().GetResult();
                if (initial?.CreationReceipt?.Key == identity.Key) { found = initial; break; }
            }
        }
        if (found == null) return Result<RunState?>.Success(null);
        var result = Compare(found, identity);
        if (result.IsSuccess) _receipts.Set(identity.Key, found);
        return result;
    }

    private static Result<RunState?> Compare(RunState initial, RunCreationReceiptIdentity requested) =>
        initial.CreationReceipt == requested ? Result<RunState?>.Success(initial)
            : Result<RunState?>.Failure("Creation command id was reused with different inputs");

    public void Remember(RunState state)
    {
        if (state.CreationReceipt is { } receipt) _receipts.Set(receipt.Key, state);
    }

    public Result<RunEligibilitySnapshot?> Capture(RunStartOptions options, ResolvedGameMode mode, string revision)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("eligibility_capture");
        if (options.CapturedEligibility is { } captured)
        {
            if (store != null) return Result<RunEligibilitySnapshot?>.Failure("Live creation cannot accept a supplied eligibility snapshot");
            var run = new RunState { PlayerEntityId = options.PlayerEntityId, SettingId = options.SettingId ?? options.ConfigName,
                ResolvedMode = mode, Eligibility = captured };
            var valid = RunEligibility.ValidateSnapshot(run);
            if (valid.IsFailure || captured.ContentRevision != revision || mode.ProfileProgressPolicy == null ||
                captured.PolicyFingerprint != CanonicalJson.ComputeHash(mode.ProfileProgressPolicy))
                return Result<RunEligibilitySnapshot?>.Failure(valid.IsFailure ? valid.Error : "Replay eligibility differs from its pinned policy/revision");
            return Result<RunEligibilitySnapshot?>.Success(captured);
        }
        var rules = mode.Definition.EligibilityRules;
        ProfileProgressSnapshot? profile = null;
        if (mode.ProfileProgressPolicy != null && (rules?.Source ?? RunEligibilitySource.Profile) == RunEligibilitySource.Profile)
        {
            if (store is not IProfileProgressSnapshotReader reader)
                return Result<RunEligibilitySnapshot?>.Failure("Authoritative profile reader is required for live run creation");
            profile = reader.ReadAsync(options.PlayerEntityId, options.SettingId ?? options.ConfigName).GetAwaiter().GetResult();
        }
        return RunEligibility.Capture(mode, options.PlayerEntityId, options.SettingId ?? options.ConfigName, revision, profile);
    }
}
