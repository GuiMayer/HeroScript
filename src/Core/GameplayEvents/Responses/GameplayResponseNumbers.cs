using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;

namespace Core.GameplayEvents.Responses;

/// <summary>Typed, requested-only projection for the canonical numeric evaluator. Never reflects over JSON or CLR members.</summary>
public static class GameplayResponseNumbers
{
    public static Result<ImmutableDictionary<string, float>> Read(GameplayOccurrence occurrence, GameplayObserverRegistration registration,
        GameplayObserverCollectionContext snapshot, string? ownerActorId, IEnumerable<string> requested)
    {
        if (occurrence?.Payload == null || registration == null || snapshot?.Run == null || requested == null)
            return Result<ImmutableDictionary<string, float>>.Failure("Response numeric projection requires a frozen occurrence and snapshot");
        var inputs = requested.Take(129).ToArray();
        if (inputs.Length > 128 || inputs.Any(name => !EventValue.Valid(name)))
            return Result<ImmutableDictionary<string, float>>.Failure("Response numeric references exceed their budget");
        var names = inputs.Distinct(StringComparer.Ordinal);
        var values = ImmutableDictionary.CreateBuilder<string, float>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            double? number = name switch
            {
                "event.ordinal" => occurrence.Ordinal,
                "event.depth" => occurrence.Depth,
                "scope.run_sequence" => snapshot.Run.Sequence,
                "scope.container_stacks" => registration.Stacks,
                "scope.container_duration" => registration.Duration,
                _ => Payload(occurrence.Payload, name)
            };
            if (number == null)
            {
                var path = name.Split('.');
                var prefix = path.Length > 1 && path[0] == "owner" ? 1
                    : path.Length > 2 && path[0] == "event" && path[1] is "source" or "subject" or "attributed" ? 2 : 0;
                var actorId = prefix == 1 ? ownerActorId : prefix == 2 ? path[1] switch
                { "source" => occurrence.SourceActorId, "subject" => occurrence.SubjectActorId, _ => occurrence.AttributedActorId } : null;
                var actor = actorId == null ? null : snapshot.Combat?.GetActor(actorId);
                var persistent = snapshot.Run.PlayerEntity;
                var components = actor?.Components ?? (snapshot.Combat == null && actorId != null && persistent?.InstanceId == actorId ? persistent!.Components : null);
                if (components != null && path.Length == prefix + 2 && components.GetValueOrDefault(path[prefix]) is StatEntityComponentState stats &&
                    stats.Values.TryGetValue(path[prefix + 1], out var stat)) number = stat;
                else if (components != null && path.Length == prefix + 3 && path[prefix] == "resources")
                {
                    var pool = components.Values.OfType<ResourceEntityComponentState>().SingleOrDefault()?.State.Get(path[prefix + 1]);
                    if (pool != null) number = path[prefix + 2] switch { "current" => pool.Current, "maximum" => pool.Maximum, "minimum" => pool.Minimum, _ => null };
                }
            }
            if (number == null || !double.IsFinite(number.Value) || !float.IsFinite((float)number.Value))
                return Result<ImmutableDictionary<string, float>>.Failure($"Unknown, absent or non-finite response numeric reference: {name}");
            values.Add(name, (float)number.Value);
        }
        return Result<ImmutableDictionary<string, float>>.Success(values.ToImmutable());
    }

    private static double? Payload(GameplayOccurrencePayload payload, string name) => (payload, name) switch
    {
        (ResourceChangePayload value, "event.change.before") => value.Before,
        (ResourceChangePayload value, "event.change.after") => value.After,
        (ResourceChangePayload value, "event.change.operand") => value.Operand,
        (ResourceChangePayload value, "event.change.requested_value") => value.RequestedValue,
        (ResourceChangePayload value, "event.change.requested_delta") => value.RequestedDelta,
        (ResourceChangePayload value, "event.change.applied_delta") => value.AppliedDelta,
        (ResourceChangePayload value, "event.change.limited_delta") => value.LimitedDelta,
        (AttributeChangePayload value, "event.change.before") => value.Before,
        (AttributeChangePayload value, "event.change.after") => value.After,
        (StatusChangePayload value, "event.stacks.before") => value.BeforeStacks,
        (StatusChangePayload value, "event.stacks.after") => value.AfterStacks,
        (StatusChangePayload value, "event.duration.before") => value.BeforeDuration,
        (StatusChangePayload value, "event.duration.after") => value.AfterDuration,
        (ModifierChangePayload value, "event.stacks.before") => value.BeforeStacks,
        (ModifierChangePayload value, "event.stacks.after") => value.AfterStacks,
        (ModifierChangePayload value, "event.duration.before") => value.BeforeDuration,
        (ModifierChangePayload value, "event.duration.after") => value.AfterDuration,
        (InstanceDurationPayload value, "event.duration.before") => value.Before,
        (InstanceDurationPayload value, "event.duration.after") => value.After,
        (ResourceTransportPayload value, "event.transport.before") => value.Before,
        (ResourceTransportPayload value, "event.transport.after") => value.After,
        (ResourceTransportPayload value, "event.transport.requested_current") => value.RequestedCurrent,
        (ResourceRebindPayload value, "event.change.before") => value.Before,
        (ResourceRebindPayload value, "event.change.after") => value.After,
        (ResourceRestrictionPayload value, "event.restriction.before_ceiling") => value.BeforeCeiling,
        (ResourceRestrictionPayload value, "event.restriction.after_ceiling") => value.AfterCeiling,
        (ResourceRestrictionPayload value, "event.restriction.has_ceiling") => value.AfterCeiling.HasValue ? 1 : 0,
        _ => null
    };
}
