using System.Collections.Immutable;
using Core.Combat.Activation;
using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.Flow;

public sealed record CircularSequenceState
{
    public SequenceDirection Direction { get; init; }
    public int PendingAdvance { get; init; } = 1;
    public int CompletedActivations { get; init; }
    public int RoundLength { get; init; }
}

public sealed record ActivationSuccessor(string ActorId, int Index, int Round,
    IReadOnlyList<string> Order, IReadOnlyList<string> Completed, CircularSequenceState? Circular);

/// <summary>Traversal only. Lifecycle, ordering, RNG and persistence remain with their authorities.</summary>
public static class ActivationSequenceReducer
{
    public static bool StartsNewRound(CombatState combat, ActivationSequencePolicy policy)
    {
        var activation = combat.ActivationState!;
        if (policy.Strategy == ActivationSequenceStrategy.Circular)
            return policy.Circular!.RoundPolicy == SequenceRoundStrategy.ActivationCount &&
                activation.Circular!.CompletedActivations + 1 >= activation.Circular.RoundLength;
        return activation.ActivationOrder.Where(id => combat.GetActor(id)?.IsAlive == true)
            .All(id => activation.CompletedActorIds.Contains(id, StringComparer.Ordinal));
    }

    public static Result<ActivationSuccessor> Next(CombatState combat, ActivationSequencePolicy policy)
    {
        var valid = ActivationSequencePolicy.Validate(policy);
        if (valid.IsFailure) return Result<ActivationSuccessor>.Failure(valid.Error);
        var activation = combat.ActivationState;
        if (activation == null) return Result<ActivationSuccessor>.Failure("Activation is not initialized");
        var seats = combat.TurnOrderState.Order;
        var eligible = seats.Where(id => combat.GetActor(id)?.IsAlive == true).ToImmutableArray();
        if (eligible.IsEmpty) return Result<ActivationSuccessor>.Failure("No eligible participants remain");
        if (policy.Strategy == ActivationSequenceStrategy.PerRound)
        {
            var index = Array.FindIndex(eligible.ToArray(), id => !activation.CompletedActorIds.Contains(id, StringComparer.Ordinal));
            var wrapped = index < 0;
            return Result<ActivationSuccessor>.Success(new(eligible[wrapped ? 0 : index], wrapped ? 0 : index,
                wrapped ? checked(activation.Round + 1) : activation.Round, eligible,
                wrapped ? [] : activation.CompletedActorIds, null));
        }
        var circular = activation.Circular;
        if (circular == null || circular.PendingAdvance is < 1 or > 10000 || circular.RoundLength < 1 ||
            circular.Direction is not (SequenceDirection.Forward or SequenceDirection.Reverse))
            return Result<ActivationSuccessor>.Failure("Invalid circular sequence state");
        if (eligible.Length == 1 && !policy.Circular!.AllowSelfSuccessor)
            return Result<ActivationSuccessor>.Failure("Circular policy does not allow a sole participant to act again");
        var cursor = seats.ToList().IndexOf(activation.ActiveActorId!);
        if (cursor < 0) return Result<ActivationSuccessor>.Failure("Circular anchor is not in the seat order");
        var remaining = circular.PendingAdvance;
        var direction = circular.Direction == SequenceDirection.Forward ? 1 : -1;
        while (remaining > 0)
        {
            cursor = (cursor + direction + seats.Count) % seats.Count;
            if (combat.GetActor(seats[cursor])?.IsAlive == true) remaining--;
        }
        var round = StartsNewRound(combat, policy);
        return Result<ActivationSuccessor>.Success(new(seats[cursor], cursor,
            round ? checked(activation.Round + 1) : activation.Round, seats, [],
            circular with { PendingAdvance = 1, CompletedActivations = round ? 0 : checked(circular.CompletedActivations + 1) }));
    }
}
