using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Resources;

namespace Core.GameplayEvents;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameplayPayloadKind { ResourceChange, AttributeChange, StatusChange, CardMovement, Action, Boundary, RuleResult, ZoneOrder, ModifierChange, ResourceThreshold, ActorDefeatChange, SequenceChange, InstanceDuration, ResourceTransport, ContentRevision, ResourceRebind, ResourceRestriction }

public sealed record GameplayEventScope
{
    public string SettingId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string BranchId { get; init; } = string.Empty;
    public string ActionId { get; init; } = string.Empty;
    public string CommandId { get; init; } = string.Empty;
    public string? CombatId { get; init; }
}

public sealed record GameplayEventOrigin
{
    public string ContainerKind { get; init; } = string.Empty;
    public string ContainerId { get; init; } = string.Empty;
    public string ComponentId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
}

/// <summary>Frozen gameplay fact. Creating/reading it neither applies an effect nor publishes telemetry.</summary>
public sealed record GameplayOccurrence
{
    public int SchemaVersion { get; init; } = 1;
    public string EventId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string ReasonId { get; init; } = string.Empty;
    public GameplayEventScope Scope { get; init; } = new();
    public string RootEventId { get; init; } = string.Empty;
    public string? ParentEventId { get; init; }
    public long Ordinal { get; init; }
    public int Depth { get; init; }
    public string? SourceActorId { get; init; }
    public string? AttributedActorId { get; init; }
    public string? SubjectActorId { get; init; }
    public GameplayEventOrigin? Origin { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GameplayCausalAnchor? OriginCause { get; init; }
    public string? ImpactId { get; init; }
    public string? ProcId { get; init; }
    public string? CalculationId { get; init; }
    public ImmutableArray<string> Tags { get; init; } = [];
    public GameplayOccurrencePayload Payload { get; init; } = null!;

    public Result Validate(GameplayEventCatalog catalog)
    {
        if (catalog == null || SchemaVersion != 1 || Scope == null || Payload == null ||
            HierarchicalName.Parse(Kind).IsFailure || HierarchicalName.Parse(ReasonId).IsFailure ||
            !EventValue.Valid(EventId) || !EventValue.Valid(RootEventId) ||
            Ordinal < 0 || Depth is < 0 or > 32 ||
            !new[] { Scope.SettingId, Scope.ContentRevision, Scope.RunId, Scope.BranchId, Scope.ActionId, Scope.CommandId }.All(EventValue.Valid) ||
            !new[] { ParentEventId, Scope.CombatId, SourceActorId, AttributedActorId, SubjectActorId, ImpactId, ProcId, CalculationId }.All(EventValue.Optional) ||
            Tags.IsDefault || Tags.Length > 64 || Tags.Any(tag => !EventValue.Valid(tag)) ||
            Tags.Distinct(StringComparer.Ordinal).Count() != Tags.Length)
            return Result.Failure("Occurrence identity, scope, tags or causal bounds are invalid");
        if (Depth == 0 && (ParentEventId != null || RootEventId != EventId) ||
            Depth > 0 && (ParentEventId == null || ParentEventId == EventId || RootEventId == EventId))
            return Result.Failure("Occurrence causal references do not match its depth");
        if (Origin != null && (HierarchicalName.Parse(Origin.ContainerKind).IsFailure ||
            !new[] { Origin.ContainerId, Origin.ComponentId, Origin.ContentRevision }.All(EventValue.Valid)))
            return Result.Failure("Occurrence origin is invalid");
        if (OriginCause != null && (OriginCause.Validate().IsFailure || OriginCause.SettingId != Scope.SettingId || OriginCause.RunId != Scope.RunId))
            return Result.Failure("Historical causal anchor belongs to another run or setting");
        var definition = catalog.FindKind(Kind);
        if (definition.IsFailure || definition.Value.Kind != Kind) return Result.Failure("Occurrence kind must be a canonical catalog identifier");
        var reason = catalog.FindReason(ReasonId);
        if (reason.IsFailure || reason.Value.ReasonId != ReasonId) return Result.Failure("Occurrence reason must be a canonical catalog identifier");
        if (catalog.SettingId != Scope.SettingId || catalog.ContentRevision != Scope.ContentRevision ||
            definition.Value.SchemaVersion != SchemaVersion || definition.Value.Payload != Payload.PayloadKind)
            return Result.Failure("Occurrence does not match its catalog scope or payload schema");
        return Payload.Validate();
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ResourceChangePayload), "resource")]
[JsonDerivedType(typeof(AttributeChangePayload), "attribute")]
[JsonDerivedType(typeof(StatusChangePayload), "status")]
[JsonDerivedType(typeof(CardMovementPayload), "card_movement")]
[JsonDerivedType(typeof(ActionOccurrencePayload), "action")]
[JsonDerivedType(typeof(BoundaryOccurrencePayload), "boundary")]
[JsonDerivedType(typeof(RuleResultPayload), "rule_result")]
[JsonDerivedType(typeof(ZoneOrderPayload), "zone_order")]
[JsonDerivedType(typeof(ModifierChangePayload), "modifier")]
[JsonDerivedType(typeof(ResourceThresholdPayload), "resource_threshold")]
[JsonDerivedType(typeof(ActorDefeatChangePayload), "actor_defeat")]
[JsonDerivedType(typeof(SequenceChangePayload), "sequence")]
[JsonDerivedType(typeof(InstanceDurationPayload), "instance_duration")]
[JsonDerivedType(typeof(ResourceTransportPayload), "resource_transport")]
[JsonDerivedType(typeof(ContentRevisionPayload), "content_revision")]
[JsonDerivedType(typeof(ResourceRebindPayload), "resource_rebind")]
[JsonDerivedType(typeof(ResourceRestrictionPayload), "resource_restriction")]
public abstract record GameplayOccurrencePayload
{
    [JsonIgnore] public abstract GameplayPayloadKind PayloadKind { get; }
    public abstract Result Validate();
}

public sealed record GameplayObjectReference
{
    public string Kind { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string? ScopeId { get; init; }
    [JsonIgnore] public bool IsValid => HierarchicalName.Parse(Kind).IsSuccess && EventValue.Valid(Id) && EventValue.Optional(ScopeId);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceOccurrenceRole { DirectMutation, PrimaryEffect, Settlement }

public sealed record ResourceChangePayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.ResourceChange;
    public string ResourceId { get; init; } = string.Empty;
    public ResourceOccurrenceRole Role { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CalculationChannel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GameplayObjectReference? Owner { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public ResourceValueField Field { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public ResourceMutationOperation Operation { get; init; }
    public float Operand { get; init; }
    public float Before { get; init; }
    public float RequestedValue { get; init; }
    public float After { get; init; }
    public double RequestedDelta => (double)RequestedValue - Before;
    public double AppliedDelta => (double)After - Before;
    public double LimitedDelta => (double)RequestedValue - After;
    public string? TransferGroupId { get; init; }
    public ImmutableArray<string> RelatedFactIds { get; init; } = [];
    public override Result Validate()
    {
        if (!EventValue.Valid(ResourceId) || Owner is { IsValid: false } || !Enum.IsDefined(Field) || !Enum.IsDefined(Operation) ||
            !Enum.IsDefined(Role) || !EventValue.Optional(CalculationChannel) ||
            !new[] { Operand, Before, RequestedValue, After }.All(float.IsFinite) || !EventValue.Optional(TransferGroupId) ||
            RelatedFactIds.IsDefault || RelatedFactIds.Length > 64 || RelatedFactIds.Any(id => !EventValue.Valid(id)))
            return Result.Failure("Invalid resource change payload");
        var expected = Operation switch
        {
            ResourceMutationOperation.Add => (float)((double)Before + Operand),
            ResourceMutationOperation.Subtract => (float)((double)Before - Operand),
            _ => Operand
        };
        return float.IsFinite(expected) && expected == RequestedValue
            ? Result.Success() : Result.Failure("Requested resource value does not match the explicit operation");
    }
}

public sealed record AttributeChangePayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.AttributeChange;
    public string ComponentId { get; init; } = string.Empty;
    public string ValueId { get; init; } = string.Empty;
    public Core.Entity.AttributeOperation Operation { get; init; }
    public Core.Entity.AttributeLifetime Lifetime { get; init; }
    public float Before { get; init; }
    public float After { get; init; }
    public override Result Validate() => EventValue.Valid(ComponentId) && EventValue.Valid(ValueId) && Enum.IsDefined(Operation) && Enum.IsDefined(Lifetime) &&
        float.IsFinite(Before) && float.IsFinite(After) ? Result.Success() : Result.Failure("Invalid attribute change payload");
}

public sealed record StatusChangePayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.StatusChange;
    public string DefinitionId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public int BeforeStacks { get; init; }
    public int AfterStacks { get; init; }
    public int? BeforeDuration { get; init; }
    public int? AfterDuration { get; init; }
    public override Result Validate() => EventValue.Valid(DefinitionId) && EventValue.Valid(InstanceId) &&
        BeforeStacks >= 0 && AfterStacks >= 0 && BeforeDuration is not < -1 && AfterDuration is not < -1
        ? Result.Success() : Result.Failure("Invalid status change payload");
}

public sealed record GameplayZoneReference
{
    public string ZoneId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public string? ScopeId { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public Core.CardZones.CardZoneOwnerScope OwnerScope { get; init; }
    [JsonIgnore] public bool IsValid => EventValue.Valid(ZoneId) && EventValue.Valid(OwnerId) && EventValue.Optional(ScopeId) &&
        Enum.IsDefined(OwnerScope) && OwnerScope != Core.CardZones.CardZoneOwnerScope.Unspecified;
}

public sealed record CardMovementPayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.CardMovement;
    public string CardInstanceId { get; init; } = string.Empty;
    public GameplayZoneReference? From { get; init; }
    public GameplayZoneReference? To { get; init; }
    public string? MovementGroupId { get; init; }
    public override Result Validate() => EventValue.Valid(CardInstanceId) && (From != null || To != null) &&
        (From?.IsValid ?? true) && (To?.IsValid ?? true) && EventValue.Optional(MovementGroupId)
        ? Result.Success() : Result.Failure("Invalid card movement payload");
}

public sealed record ZoneOrderPayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.ZoneOrder;
    public GameplayZoneReference Zone { get; init; } = null!;
    public ImmutableArray<string> Before { get; init; } = [];
    public ImmutableArray<string> After { get; init; } = [];
    public string MovementGroupId { get; init; } = string.Empty;
    public override Result Validate() => Zone is { IsValid: true } && EventValue.Valid(MovementGroupId) &&
        !Before.IsDefault && !After.IsDefault && Before.Length <= 65536 && Before.Length == After.Length &&
        Before.All(EventValue.Valid) && After.All(EventValue.Valid) &&
        Before.Distinct(StringComparer.Ordinal).Count() == Before.Length &&
        Before.ToHashSet(StringComparer.Ordinal).SetEquals(After)
        ? Result.Success() : Result.Failure("Zone order payload must contain the same unique instances");
}

public sealed record ActionOccurrencePayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.Action;
    public string ActionDefinitionId { get; init; } = string.Empty;
    public ImmutableArray<string> ChildEventIds { get; init; } = [];
    public override Result Validate() => EventValue.Valid(ActionDefinitionId) && !ChildEventIds.IsDefault &&
        ChildEventIds.Length <= 4096 && ChildEventIds.All(EventValue.Valid)
        ? Result.Success() : Result.Failure("Invalid action payload");
}

public sealed record BoundaryOccurrencePayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.Boundary;
    public string BoundaryId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public override Result Validate() => EventValue.Valid(BoundaryId) && Sequence >= 0
        ? Result.Success() : Result.Failure("Invalid boundary payload");
}

public sealed record RuleResultPayload : GameplayOccurrencePayload
{
    public override GameplayPayloadKind PayloadKind => GameplayPayloadKind.RuleResult;
    public string RuleId { get; init; } = string.Empty;
    public string ResultId { get; init; } = string.Empty;
    public override Result Validate() => EventValue.Valid(RuleId) && EventValue.Valid(ResultId)
        ? Result.Success() : Result.Failure("Invalid rule result payload");
}

internal static class EventValue
{
    internal static bool Valid(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512;
    internal static bool Optional(string? value) => value == null || Valid(value);
}
