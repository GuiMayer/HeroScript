using System.Collections.Immutable;
using Core.Common;
using Core.Effects;

namespace Core.GameplayEvents.Responses;

/// <summary>Host capabilities, never author-controlled grants. Longer-lived frequencies require their persisted counter implementation.</summary>
public sealed record GameplayResponseCapabilities
{
    public ImmutableHashSet<ResponseFrequencyScope> Frequencies { get; init; } =
        [ResponseFrequencyScope.Occurrence, ResponseFrequencyScope.Impact, ResponseFrequencyScope.Proc, ResponseFrequencyScope.RootAction];
    public bool ExtensionTopics { get; init; }
}

public sealed record CompiledGameplayResponse(GameplayResponseDefinition Definition, HierarchySelector Kinds, HierarchySelector Reasons,
    string SettingId, string ContentRevision, bool HasKnownTopics)
{
    public long NormalizedPriority => Definition.Order.Descending ? -(long)Definition.Order.Priority : Definition.Order.Priority;
}

public static class GameplayResponseCompiler
{
    public static Result<CompiledGameplayResponse> Compile(GameplayResponseDefinition definition, GameplayEventCatalog catalog,
        GameplayResponseCapabilities? capabilities = null)
    {
        capabilities ??= new();
        if (definition == null || catalog == null || definition.SchemaVersion != 1 || !EventValue.Valid(definition.ResponseId) ||
            definition.Observe == null || definition.Bindings == null || definition.Availability == null || definition.Frequency == null || definition.Order == null ||
            !Enum.IsDefined(definition.Propagation) || definition.Condition is { Length: > 2048 } || definition.Condition != null && string.IsNullOrWhiteSpace(definition.Condition) ||
            definition.Effects.IsDefaultOrEmpty || definition.Effects.Length > 16 || definition.Effects.Any(effect => effect == null) ||
            !Enum.IsDefined(definition.Frequency.Scope) || capabilities.Frequencies == null || !capabilities.Frequencies.Contains(definition.Frequency.Scope) ||
            definition.Frequency.Maximum is < 1 or > EffectExecutionLimits.MaximumSteps ||
            definition.ReasonId != null && catalog.FindReason(definition.ReasonId).IsFailure ||
            definition.AllowExtensionTopics && !capabilities.ExtensionTopics)
            return Result<CompiledGameplayResponse>.Failure("Response definition is invalid, exceeds its budget or requires unavailable capabilities");
        var observe = definition.Observe;
        if (observe.Kinds == null || observe.Reasons == null || !ValidActorFilter(observe.Source) || !ValidActorFilter(observe.Subject) ||
            !ValidActorFilter(observe.Attributed) || observe.SourceSubjectRelationship is { } relationship && !Enum.IsDefined(relationship) ||
            !ValidIds(observe.RequiredTags) || !ValidIds(observe.ExcludedTags) ||
            observe.RequiredTags.Intersect(observe.ExcludedTags, StringComparer.Ordinal).Any())
            return Result<CompiledGameplayResponse>.Failure("Response has invalid or contradictory typed filters");
        var kinds = HierarchySelector.Compile(observe.Kinds.Include, observe.Kinds.Exclude);
        var reasons = HierarchySelector.Compile(observe.Reasons.Include, observe.Reasons.Exclude);
        if (kinds.IsFailure || reasons.IsFailure)
            return Result<CompiledGameplayResponse>.Failure(kinds.IsFailure ? kinds.Error : reasons.Error);
        var kindPatterns = ValidatePatterns(kinds.Value, catalog.Kinds.Keys, definition.AllowExtensionTopics);
        var reasonPatterns = ValidatePatterns(reasons.Value, catalog.Reasons.Keys, definition.AllowExtensionTopics);
        if (kindPatterns.IsFailure || reasonPatterns.IsFailure)
            return Result<CompiledGameplayResponse>.Failure(kindPatterns.IsFailure ? kindPatterns.Error : reasonPatterns.Error);
        var matchingKinds = catalog.Kinds.Values.Where(kind => kinds.Value.Matches(HierarchicalName.Parse(kind.Kind).Value)).ToArray();
        var known = matchingKinds.Length > 0 && catalog.Reasons.Keys.Any(reason => reasons.Value.Matches(HierarchicalName.Parse(reason).Value));
        if (!known && !definition.AllowExtensionTopics)
            return Result<CompiledGameplayResponse>.Failure("Response selectors exclude every topic/reason in the pinned catalog");
        if (observe.Resource is { } resource && (!ValidIds(resource.ResourceIds) || !ValidIds(resource.CalculationChannels) ||
            resource.Field is { } field && !Enum.IsDefined(field) || resource.Operation is { } operation && !Enum.IsDefined(operation) ||
            resource.Role is { } role && !Enum.IsDefined(role) || !Enum.IsDefined(resource.Direction) ||
            matchingKinds.Length > 0 && !matchingKinds.Any(kind => kind.Payload == GameplayPayloadKind.ResourceChange)))
            return Result<CompiledGameplayResponse>.Failure("Resource response filter cannot match the declared typed topics");
        if (observe.Boundary is { } boundary && (!ValidIds(boundary.BoundaryIds) || boundary.BoundaryIds.IsEmpty || observe.Resource != null ||
            matchingKinds.Length > 0 && !matchingKinds.Any(kind => kind.Payload == GameplayPayloadKind.Boundary)))
            return Result<CompiledGameplayResponse>.Failure("Boundary response filter cannot match the declared typed topics");
        var availability = definition.Availability;
        if (!Enum.IsDefined(availability.Snapshot) || !Enum.IsDefined(availability.Kind) || !ValidIds(availability.ZoneIds) ||
            (availability.Kind == ResponseAvailabilityKind.CardZones) != !availability.ZoneIds.IsEmpty ||
            !ValidBinding(definition.Bindings.Owner) || definition.Bindings.Owner.Kind == ResponseActorBindingKind.Owner ||
            !ValidBinding(definition.Bindings.Source) || !ValidBinding(definition.Bindings.Target) || !Enum.IsDefined(definition.Bindings.Missing))
            return Result<CompiledGameplayResponse>.Failure("Response bindings or availability are incomplete or contradictory");
        var effects = EffectDefinitionValidator.Validate(definition.Effects);
        return effects.IsEmpty ? Result<CompiledGameplayResponse>.Success(new(definition, kinds.Value, reasons.Value,
            catalog.SettingId, catalog.ContentRevision, known)) : Result<CompiledGameplayResponse>.Failure(string.Join("; ", effects));
    }

    private static Result ValidatePatterns(HierarchySelector selector, IEnumerable<string> names, bool extensions)
    {
        var known = names.Select(name => HierarchicalName.Parse(name).Value).ToArray();
        foreach (var pattern in selector.Include.Concat(selector.Exclude))
            if (!known.Any(pattern.Matches) && !(extensions && pattern.Segments.Any(segment => segment is "*" or "**")))
                return Result.Failure($"Response selector has no match in its pinned catalog: {pattern.Value}");
        return Result.Success();
    }

    private static bool ValidActorFilter(ResponseActorFilter? filter) => filter != null && ValidIds(filter.ActorIds) &&
        (filter.RelationshipToOwner == null || Enum.IsDefined(filter.RelationshipToOwner.Value));

    private static bool ValidBinding(ResponseActorBinding? binding) => binding != null && Enum.IsDefined(binding.Kind) &&
        (binding.Kind == ResponseActorBindingKind.ExplicitActor ? EventValue.Valid(binding.ActorId) : binding.ActorId == null);

    internal static bool ValidIds(ImmutableArray<string> values) => !values.IsDefault && values.Length <= 64 &&
        values.All(EventValue.Valid) && values.Distinct(StringComparer.Ordinal).Count() == values.Length;
}
