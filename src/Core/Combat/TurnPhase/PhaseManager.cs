using Core.Combat.Models;
using Core.Common;
using Core.Events;
using Core.Logging;

namespace Core.Combat.TurnPhase;

public sealed class PhaseManager : IPhaseManager
{
    private readonly IPrioritySystem _prioritySystem;
    private readonly ILogger _logger;

    public PhaseManager(IPrioritySystem prioritySystem, ILogger logger, IEventBus? eventBus = null)
    {
        _prioritySystem = prioritySystem ?? throw new ArgumentNullException(nameof(prioritySystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Result<PhaseState> StartPhase(
        string phaseId,
        CombatState state,
        PhaseSequenceDefinition sequence)
    {
        if (string.IsNullOrWhiteSpace(phaseId) || sequence.Find(phaseId) == null)
            return Result<PhaseState>.Failure($"Unknown phase: {phaseId}");

        var playerOrder = _prioritySystem.GetPlayerOrder(state);
        if (playerOrder.Count == 0)
            return Result<PhaseState>.Failure("No actors found in combat state");

        var phaseState = new PhaseState
        {
            CurrentPhaseId = phaseId,
            PhaseIndex = IndexOf(sequence, phaseId),
            PhaseSequence = sequence,
            PriorityOrder = playerOrder,
            ActivePlayerId = playerOrder[0],
            CanTransition = false,
            PlayerPassedPriority = playerOrder.ToDictionary(id => id, _ => false),
            PhaseStartTime = state.Determinism.LogicalTimestamp.UtcDateTime
        };
        _logger.LogDebug($"Started phase {phaseId} with active actor {playerOrder[0]}");
        return Result<PhaseState>.Success(phaseState);
    }

    public Result<PhaseState> TransitionToNextPhase(
        PhaseState currentPhase,
        PhaseSequenceDefinition sequence)
    {
        var nextIndex = currentPhase.PhaseIndex + 1;
        if (nextIndex >= sequence.Phases.Count)
            return Result<PhaseState>.Failure("Reached end of phase sequence");
        return TransitionToPhase(sequence.Phases[nextIndex].PhaseId, currentPhase, sequence);
    }

    public Result<PhaseState> TransitionToPhase(
        string targetPhaseId,
        PhaseState currentPhase,
        PhaseSequenceDefinition sequence)
    {
        var targetIndex = IndexOf(sequence, targetPhaseId);
        if (targetIndex < 0)
            return Result<PhaseState>.Failure($"Phase {targetPhaseId} not found in sequence");
        var validation = ValidatePhaseTransition(currentPhase.CurrentPhaseId, targetPhaseId, sequence);
        if (validation.IsFailure)
            return Result<PhaseState>.Failure(validation.Error);

        var changed = currentPhase with
        {
            CurrentPhaseId = targetPhaseId,
            PhaseIndex = targetIndex,
            CanTransition = false,
            PhaseStartTime = currentPhase.PhaseStartTime.AddTicks(1)
        };
        var reset = _prioritySystem.ResetPriority(changed, currentPhase.ActivePlayerId);
        if (reset.IsFailure)
            return Result<PhaseState>.Failure($"Failed to reset priority: {reset.Error}");

        _logger.LogDebug($"Transitioned from {currentPhase.CurrentPhaseId} to {targetPhaseId}");
        return reset;
    }

    public bool CanExecuteAction(
        ActionType action,
        PhaseState phaseState,
        PhaseSequenceDefinition sequence) =>
        sequence.Find(phaseState.CurrentPhaseId)?.AllowedActions.Contains(action) == true;

    public List<ActionType> GetAllowedActions(
        PhaseState phaseState,
        PhaseSequenceDefinition sequence) =>
        sequence.Find(phaseState.CurrentPhaseId)?.AllowedActions.ToList() ?? [];

    public Result ValidatePhaseTransition(
        string fromPhaseId,
        string toPhaseId,
        PhaseSequenceDefinition sequence)
    {
        var from = sequence.Find(fromPhaseId);
        var toIndex = IndexOf(sequence, toPhaseId);
        if (from == null || toIndex < 0)
            return Result.Failure("Phase transition references an unknown phase");
        if (from.ValidNextPhaseIds.Count > 0)
            return from.ValidNextPhaseIds.Contains(toPhaseId, StringComparer.Ordinal)
                ? Result.Success()
                : Result.Failure($"Cannot transition from {fromPhaseId} to {toPhaseId}");

        var fromIndex = IndexOf(sequence, fromPhaseId);
        if (toIndex == fromIndex + 1)
            return Result.Success();
        if (sequence.AllowPhaseSkipping && toIndex > fromIndex)
            return Result.Success();
        return Result.Failure($"Cannot transition from {fromPhaseId} to {toPhaseId}");
    }

    private static int IndexOf(PhaseSequenceDefinition sequence, string phaseId)
    {
        for (var index = 0; index < sequence.Phases.Count; index++)
        {
            if (string.Equals(sequence.Phases[index].PhaseId, phaseId, StringComparison.Ordinal))
                return index;
        }
        return -1;
    }
}
