using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.Activation;

/// <summary>Scheduling constraint for any participant; independent from effects or game roles.</summary>
public sealed record ActorActivationCadence(string AnchorActorId, int Every);
public sealed record ActorActivationCadenceProgress
{
    public ImmutableSortedDictionary<string, int> Completed { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableSortedDictionary<string, int> ConsumedAnchor { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
}

public static class ActorActivationCadenceReducer
{
    public static bool HasCadence(CombatState combat) => combat.GetAllActors().Any(actor => actor.ControllerBinding.Cadence != null);

    public static Result Validate(IEnumerable<(string Id, ControllerBinding Binding)> participants)
    {
        var roster = participants.ToArray();
        foreach (var actor in roster)
            if (actor.Binding.Cadence is { } cadence)
            {
                var anchor = roster.FirstOrDefault(other => other.Id == cadence.AnchorActorId);
                if (cadence.Every is < 1 or > 10000 || anchor.Binding == null || anchor.Id == actor.Id || anchor.Binding.Cadence != null)
                    return Result.Failure("Cadence anchor must be a distinct ordinary participant with a positive bounded interval");
            }
        return Result.Success();
    }

    public static bool Eligible(CombatState combat, string actorId)
    {
        var actor = combat.GetActor(actorId);
        if (actor?.IsAlive != true) return false;
        if (actor.ControllerBinding.Cadence is not { } cadence) return true;
        if (combat.GetActor(cadence.AnchorActorId)?.IsAlive != true) return false;
        var progress = combat.ActivationState?.CadenceProgress;
        var count = progress?.Completed.GetValueOrDefault(cadence.AnchorActorId) ?? 0;
        var consumed = progress?.ConsumedAnchor.GetValueOrDefault(actorId) ?? 0;
        return (long)count - consumed >= cadence.Every;
    }

    public static CombatState Complete(CombatState combat, string actorId)
    {
        if (!HasCadence(combat) || combat.ActivationState == null) return combat;
        var progress = combat.ActivationState.CadenceProgress ?? new();
        var completed = progress.Completed.SetItem(actorId, checked(progress.Completed.GetValueOrDefault(actorId) + 1));
        var consumed = progress.ConsumedAnchor;
        if (combat.GetActor(actorId)?.ControllerBinding.Cadence is { } cadence)
            consumed = consumed.SetItem(actorId, completed.GetValueOrDefault(cadence.AnchorActorId));
        return combat with { ActivationState = combat.ActivationState with
            { CadenceProgress = progress with { Completed = completed, ConsumedAnchor = consumed } } };
    }
}
