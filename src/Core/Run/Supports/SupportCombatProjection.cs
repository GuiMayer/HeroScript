using Core.Combat.Activation;

namespace Core.Run.Supports;

public sealed record SupportCombatStatus(string SupportId, string RoutineId, string ParticipantId,
    int RemainingOwnerActivations, bool Eligible, bool OwnerAvailable);

/// <summary>Uses the captured encounter build, never the current editable equipment.</summary>
public static class SupportCombatProjection
{
    public static SupportCombatStatus? Current(RunState run)
    {
        var encounter = run.GetActiveEncounter();
        if (encounter?.SupportComposition is not { } build || !encounter.Combat.IsActive) return null;
        var combat = encounter.Combat;
        if (combat.GetActor(build.ParticipantId)?.ControllerBinding.Cadence is not { } cadence) return null;
        var progress = combat.ActivationState?.CadenceProgress;
        var elapsed = (progress?.Completed.GetValueOrDefault(build.ActorId) ?? 0) - (progress?.ConsumedAnchor.GetValueOrDefault(build.ParticipantId) ?? 0);
        return new(build.SupportId, build.RoutineId, build.ParticipantId, System.Math.Max(0, cadence.Every - elapsed),
            ActorActivationCadenceReducer.Eligible(combat, build.ParticipantId), combat.GetActor(build.ActorId)?.IsAlive == true);
    }
}
