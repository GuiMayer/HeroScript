using Core.Combat.Flow;
using Core.Combat.TurnOrder;
using Core.Common;

namespace Core.Run;

/// <summary>
/// Single semantic validator for a composed game-mode policy graph. It is
/// shared by publication and runtime resolution so invalid content cannot be
/// published successfully and then fail only when a run starts.
/// </summary>
public static class GameModePolicyValidator
{
    public static Result Validate(
        CombatRulesDefinition combat,
        ReplayPolicyDefinition replay,
        TimelinePolicyDefinition timeline,
        ContentBindingPolicyDefinition binding,
        CapabilityPolicyDefinition capabilities,
        RunProgressionPolicyDefinition progression)
    {
        if (string.IsNullOrWhiteSpace(combat.DefaultPhaseSequenceId))
            return Result.Failure("Combat rules require a phase sequence id");
        var turnOrder = TurnOrderPolicyValidator.Validate(combat.TurnOrder);
        if (turnOrder.IsFailure)
            return turnOrder;
        var combatFlow = CombatFlowPolicyValidator.Validate(combat.Flow);
        if (combatFlow.IsFailure)
            return combatFlow;
        if (timeline.MaxItemsPerPage is < 1 or > 1000)
            return Result.Failure("Timeline policy maxItemsPerPage must be between 1 and 1000");
        if (capabilities.MaxCards < 0 || capabilities.MaxActors < 1 ||
            capabilities.MaxBranchesPerRoot < 0 || capabilities.MaxSimulationCommands < 0)
            return Result.Failure("Capability policy limits are invalid");
        if (replay.AllowForkFromHistory && !timeline.Enabled)
            return Result.Failure("Replay policy requires a timeline when history forks are enabled");
        if (capabilities.AllowTimelineFork && !replay.AllowForkFromHistory)
            return Result.Failure("Capability policy enables timeline forks but replay policy rejects them");
        if (capabilities.AllowHotReloadActivation &&
            binding.ActiveRuns != ActiveRunContentBinding.Versioned)
        {
            return Result.Failure(
                "Capability policy enables hot reload activation but content binding policy rejects active runs");
        }
        if (!Enum.IsDefined(progression.EncounterVictory) ||
            !Enum.IsDefined(progression.EncounterDefeat) ||
            !Enum.IsDefined(progression.EncounterDraw) ||
            !Enum.IsDefined(progression.EncounterAbandoned) ||
            !Enum.IsDefined(progression.EndOfMap))
            return Result.Failure("Run progression policy contains an invalid transition");
        if (!Enum.IsDefined(progression.EncounterRetry) ||
            progression.RetryableEncounterOutcomes.Any(outcome =>
                !Enum.IsDefined(outcome) || outcome == Core.Combat.Models.CombatStatus.ACTIVE))
            return Result.Failure("Run progression policy contains an invalid retry policy");
        if (progression.EncounterRetry == RunEncounterRetryPolicy.Disabled &&
            progression.RetryableEncounterOutcomes.Count > 0)
            return Result.Failure("Disabled encounter retry cannot declare retryable outcomes");

        return Result.Success();
    }
}
