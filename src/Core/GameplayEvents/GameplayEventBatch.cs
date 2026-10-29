using System.Collections.Immutable;
using Core.Common;
using Core.GameplayEvents.Responses;

namespace Core.GameplayEvents;

/// <summary>A successful invocation, captured before its children. Never a second execution queue.</summary>
public sealed record GameplayResponseInvocation
{
    public string InvocationId { get; init; } = string.Empty;
    public string EventId { get; init; } = string.Empty;
    public string RegistrationId { get; init; } = string.Empty;
    public string ResponseId { get; init; } = string.Empty;
    public string OwnerActorId { get; init; } = string.Empty;
    public string SourceActorId { get; init; } = string.Empty;
    public string TargetActorId { get; init; } = string.Empty;
    public GameplayEventOrigin Origin { get; init; } = new();
}

/// <summary>Bounded immutable diagnostics in the authoritative command commit. Reading never dispatches effects.</summary>
public sealed record GameplayEventBatch
{
    public int SchemaVersion { get; init; } = 1;
    public GameplayEventScope Scope { get; init; } = new();
    public ImmutableArray<GameplayOccurrence> Occurrences { get; init; } = [];
    public ImmutableArray<GameplayResponseInvocation> Invocations { get; init; } = [];

    public static GameplayEventBatch? Capture(GameplayEffectObservation? observation) => observation == null ? null : new()
    {
        Scope = observation.Events.Scope,
        Occurrences = observation.Events.Occurrences.ToImmutableArray(),
        Invocations = observation.Invocations
    };

    public Result Validate(Guid branchId, Guid commandId)
    {
        if (SchemaVersion != 1 || Scope == null || Scope.BranchId != branchId.ToString("D") ||
            Scope.CommandId != commandId.ToString("D") || Scope.ActionId != Scope.CommandId ||
            !new[] { Scope.SettingId, Scope.ContentRevision, Scope.RunId }.All(EventValue.Valid) ||
            Occurrences.IsDefault || Occurrences.Length > Core.Effects.EffectExecutionLimits.MaximumSteps ||
            Invocations.IsDefault || Invocations.Length > GameplayResponseQuotas.MaximumInvocations)
            return Result.Failure("Invalid durable gameplay event scope or bounds");
        var prefix = new Dictionary<string, GameplayOccurrence>(StringComparer.Ordinal);
        foreach (var fact in Occurrences)
        {
            if (fact == null || fact.Scope != Scope || fact.Ordinal != prefix.Count || fact.SchemaVersion != 1 ||
                !EventValue.Valid(fact.EventId) || fact.Payload == null || fact.Payload.Validate().IsFailure ||
                HierarchicalName.Parse(fact.Kind).IsFailure || HierarchicalName.Parse(fact.ReasonId).IsFailure ||
                fact.Depth is < 0 or > 32 || prefix.ContainsKey(fact.EventId))
                return Result.Failure("Invalid durable gameplay occurrence");
            if (fact.ParentEventId is { } parent)
            {
                if (!prefix.TryGetValue(parent, out var ancestor) || fact.Depth != ancestor.Depth + 1 || fact.RootEventId != ancestor.RootEventId)
                    return Result.Failure("Durable gameplay occurrence lost its causal prefix");
            }
            else if (fact.Depth != 0 || fact.RootEventId != fact.EventId)
                return Result.Failure("Invalid durable gameplay root");
            if (fact.OriginCause is { } cause && (cause.Validate().IsFailure || cause.SettingId != Scope.SettingId || cause.RunId != Scope.RunId))
                return Result.Failure("Durable gameplay cause belongs to another setting or root run");
            prefix.Add(fact.EventId, fact);
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var invocation in Invocations)
            if (invocation == null || !prefix.ContainsKey(invocation.EventId) ||
                !new[] { invocation.InvocationId, invocation.RegistrationId, invocation.ResponseId, invocation.OwnerActorId,
                    invocation.SourceActorId, invocation.TargetActorId }.All(EventValue.Valid) || !ids.Add(invocation.InvocationId) ||
                invocation.Origin == null || invocation.Origin.ContentRevision != Scope.ContentRevision)
                return Result.Failure("Invalid durable response invocation");
        return Result.Success();
    }
}
