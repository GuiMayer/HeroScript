using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Effects;
using Core.Resources;

namespace Core.GameplayEvents.Responses;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResponseSnapshot { BeforeOccurrence, AfterOccurrence }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResponseFrequencyScope { Occurrence, Impact, Proc, RootAction, Activation, Round, Combat, Run }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResponsePropagation { RecordOnly, AllowResponses }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResponseAvailabilityKind { ContainerActive, CardZones, SelectedCard, ResolvingCard }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResponseActorBindingKind { ObserverOwner, ObserverSource, Owner, EventSource, EventSubject, EventAttributed, ExplicitActor }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissingResponseBinding { Skip, RejectAction }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceChangeDirection { Any, Increase, Decrease, Unchanged }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResponseTopicSelector
{
    public ImmutableArray<string> Include { get; init; } = ["**"];
    public ImmutableArray<string> Exclude { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResponseActorFilter
{
    public ImmutableArray<string> ActorIds { get; init; } = [];
    public bool? MatchesOwner { get; init; }
    public SideRelationship? RelationshipToOwner { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResponseResourceFilter
{
    public ImmutableArray<string> ResourceIds { get; init; } = [];
    public ImmutableArray<string> CalculationChannels { get; init; } = [];
    public ResourceValueField? Field { get; init; }
    public ResourceMutationOperation? Operation { get; init; }
    public ResourceOccurrenceRole? Role { get; init; }
    public ResourceChangeDirection Direction { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GameplayResponseFilter
{
    public ResponseTopicSelector Kinds { get; init; } = new();
    public ResponseTopicSelector Reasons { get; init; } = new();
    public ResponseActorFilter Source { get; init; } = new();
    public ResponseActorFilter Subject { get; init; } = new();
    public ResponseActorFilter Attributed { get; init; } = new();
    public SideRelationship? SourceSubjectRelationship { get; init; }
    public ResponseResourceFilter? Resource { get; init; }
    public ImmutableArray<string> RequiredTags { get; init; } = [];
    public ImmutableArray<string> ExcludedTags { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResponseActorBinding
{
    public ResponseActorBindingKind Kind { get; init; } = ResponseActorBindingKind.ObserverOwner;
    public string? ActorId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GameplayResponseBindings
{
    public ResponseActorBinding Owner { get; init; } = new();
    public ResponseActorBinding Source { get; init; } = new() { Kind = ResponseActorBindingKind.Owner };
    public ResponseActorBinding Target { get; init; } = new() { Kind = ResponseActorBindingKind.EventSubject };
    public MissingResponseBinding Missing { get; init; } = MissingResponseBinding.Skip;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GameplayResponseAvailability
{
    public ResponseSnapshot Snapshot { get; init; }
    public ResponseAvailabilityKind Kind { get; init; }
    public ImmutableArray<string> ZoneIds { get; init; } = [];
    public bool RequireLivingOwner { get; init; } = true;
    public bool RevalidateOwner { get; init; } = true;
    public bool RequireRegistrationStillActive { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GameplayResponseFrequency
{
    public ResponseFrequencyScope Scope { get; init; }
    public int Maximum { get; init; } = 1;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GameplayResponseOrder
{
    public int Priority { get; init; }
    public bool Descending { get; init; }
}

/// <summary>Authored data only. Providers register it; neither this definition nor its compilation executes effects.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GameplayResponseDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string ResponseId { get; init; } = string.Empty;
    public GameplayResponseFilter Observe { get; init; } = new();
    public GameplayResponseBindings Bindings { get; init; } = new();
    public GameplayResponseAvailability Availability { get; init; } = new();
    public GameplayResponseFrequency Frequency { get; init; } = new();
    public GameplayResponseOrder Order { get; init; } = new();
    public ResponsePropagation Propagation { get; init; } = ResponsePropagation.RecordOnly;
    public string? Condition { get; init; }
    public string? ReasonId { get; init; }
    public bool AllowExtensionTopics { get; init; }
    public ImmutableArray<EffectDefinition> Effects { get; init; } = [];
}
