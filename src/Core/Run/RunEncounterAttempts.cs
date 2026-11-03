using Core.Combat.Models;

namespace Core.Run;

/// <summary>Derived read model; encounter snapshots remain the only attempt ledger.</summary>
public sealed record RunEncounterAttemptView(string NodeId, Guid CombatId, int Number,
    string Outcome, string? OutcomeRuleId, bool AwaitingConfirmation, bool PendingRetry);

public static class RunEncounterAttempts
{
    public static bool IsRetryable(RunState run, CombatStatus outcome) =>
        run.ResolvedMode?.ProgressionPolicy is { EncounterRetry: RunEncounterRetryPolicy.RestartActivity } policy &&
        policy.RetryableEncounterOutcomes.Contains(outcome);

    public static RunEncounterAttemptView? Current(RunState run)
    {
        if (run.ResolvedMode?.ProgressionPolicy?.EncounterRetry != RunEncounterRetryPolicy.RestartActivity ||
            run.CurrentNodeId == null || run.Lifecycle != RunLifecycleState.Active) return null;
        RunEncounterState? latest = null;
        var count = 0;
        foreach (var encounter in run.Encounters)
            if (encounter.NodeId == run.CurrentNodeId) { latest = encounter; count++; }
        if (latest == null || latest.Combat.IsActive || !IsRetryable(run, latest.Combat.Status) ||
            run.Map.ResolvedNodeIds.Contains(run.CurrentNodeId, StringComparer.Ordinal)) return null;
        var confirming = !latest.Resolved && run.ActiveEncounterId == latest.Combat.CombatId;
        var pending = latest.Resolved && run.ActiveEncounterId == null;
        return confirming || pending ? new(run.CurrentNodeId, latest.Combat.CombatId, count,
            latest.Combat.Status.ToString(), latest.Combat.ParticipantVictory?.RuleId, confirming, pending) : null;
    }
}
