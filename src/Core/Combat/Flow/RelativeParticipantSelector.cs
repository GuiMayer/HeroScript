using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.Flow;

public static class RelativeParticipantSelector
{
    public static Result<string> Resolve(CombatState combat, string anchor, int offset = 1, bool scheduled = false)
    {
        var circular = combat.ActivationState?.Circular;
        var seats = combat.TurnOrderState.Order;
        if (circular == null || offset is < -10000 or > 10000 || offset == 0 || seats.Count == 0)
            return Result<string>.Failure("Relative participant selection requires an initialized circular sequence and a nonzero bounded offset");
        var cursor = seats.ToList().IndexOf(anchor);
        if (cursor < 0 || !seats.Any(id => combat.GetActor(id)?.IsAlive == true))
            return Result<string>.Failure("Relative participant selection has no eligible anchor/participants");
        var direction = (circular.Direction == SequenceDirection.Forward ? 1 : -1) * System.Math.Sign(offset);
        var remaining = scheduled ? circular.PendingAdvance : System.Math.Abs(offset);
        if (remaining is < 1 or > 10000) return Result<string>.Failure("Invalid scheduled advance");
        while (remaining > 0) {
            cursor = (cursor + direction + seats.Count) % seats.Count;
            if (combat.GetActor(seats[cursor])?.IsAlive == true) remaining--;
        }
        return Result<string>.Success(seats[cursor]);
    }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum SequenceOperation { Unspecified, ReverseDirection, SetDirection, AddAdvance }
public sealed record SequenceMutation
{
    public SequenceOperation Operation { get; init; }
    public SequenceDirection Direction { get; init; }
    public int Amount { get; init; }
}
