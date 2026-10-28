using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Run;
using Core.CardZones;

namespace Core.GameplayEvents;

public sealed record UnobservedEffectApplication(string EffectInstanceId, EffectType EffectType, string TargetEntityId);
public sealed record ObservedEffectExecution(EffectBatchResult Execution, GameplayEffectObservation Observation);

/// <summary>
/// Immutable, action-local shadow context. Never executes responses or publishes events.
/// Unsupported application families remain explicit coverage gaps, not invented facts.
/// </summary>
public sealed record GameplayEffectObservation
{
    public GameplayEventTransaction Events { get; init; } = null!;
    public string PublisherId { get; init; } = string.Empty;
    public string ReasonId { get; init; } = string.Empty;
    public string? CostReasonId { get; init; }
    public string? RefundReasonId { get; init; }
    public ImmutableDictionary<string, GameplayObjectReference> ResourceOwnerBindings { get; init; } =
        ImmutableDictionary<string, GameplayObjectReference>.Empty.WithComparers(StringComparer.Ordinal);
    public string? ParentEventId { get; init; }
    public string? LastApplicationEventId { get; init; }
    public ImmutableArray<UnobservedEffectApplication> Unobserved { get; init; } = [];
    public ImmutableDictionary<string, string> ImpactAnchors { get; init; } =
        ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);

    internal Result<GameplayEffectObservation> Record(ResolvedEffectCommand command, CombatState before, CombatState after,
        IReadOnlyList<EffectApplicationRecord> records, RunState? runBefore = null, RunState? runAfter = null)
    {
        if (Events == null || ImpactAnchors == null || ResourceOwnerBindings == null || Unobserved.IsDefault || !EventValue.Valid(PublisherId) || !EventValue.Valid(ReasonId) ||
            records.Count > EffectExecutionLimits.MaximumSteps)
            return Result<GameplayEffectObservation>.Failure("Invalid effect observation context");
        var current = this with { LastApplicationEventId = null };
        var parentEventId = command.Identity?.ParentImpactId is { } parentImpact && ImpactAnchors.TryGetValue(parentImpact, out var anchor)
            ? anchor : ParentEventId;
        var settlements = ImmutableArray.CreateBuilder<string>();
        var metadata = new GameplayOccurrenceDraft
        {
            ReasonId = command.SemanticReasonId ?? ReasonId, Attribution = GameplayAttribution.Source,
            SourceActorId = string.IsNullOrEmpty(command.SourceEntityId) ? null : command.SourceEntityId,
            Origin = new() { ContainerKind = command.Provenance.Kind.ToString().ToLowerInvariant(),
                ContainerId = string.IsNullOrEmpty(command.Provenance.SourceId) ? command.EffectInstanceId : command.Provenance.SourceId,
                ComponentId = command.Provenance.ComponentId ?? command.EffectInstanceId,
                ContentRevision = command.ContentRevision ?? Events.Scope.ContentRevision },
            ImpactId = command.Identity?.ImpactId, ProcId = command.Identity?.ProcId
        };
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.EffectInstanceId != command.EffectInstanceId)
                return Result<GameplayEffectObservation>.Failure("Observed application does not belong to the resolved command");
            var drafts = ImmutableArray.CreateBuilder<GameplayOccurrenceDraft>();
            var targetMetadata = metadata with { SubjectActorId = string.IsNullOrEmpty(record.TargetEntityId) ? null : record.TargetEntityId,
                CalculationId = record.CalculationId, ImpactId = record.Identity?.ImpactId, ProcId = record.Identity?.ProcId };
            if (record.ResourceOutcome != null)
            {
                ResourceOwnerBindings.TryGetValue(record.TargetEntityId, out var boundOwner);
                if (boundOwner != null && (before.GetActor(record.TargetEntityId)?.ResourceState.OwnerId != boundOwner.Id || !boundOwner.IsValid))
                    return Result<GameplayEffectObservation>.Failure("Resource owner binding does not match the captured actor adapter");
                var fact = GameplayResourceFactProducer.FromEffect(command, record,
                    boundOwner ?? new() { Kind = "entity", Id = record.TargetEntityId, ScopeId = Events.Scope.CombatId ?? Events.Scope.RunId },
                    targetMetadata, boundOwner == null ? null : record.TargetEntityId);
                if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
                var resourceFact = fact.Value;
                if (record.CalculationInfluenceId == null && settlements.Count > 0)
                    resourceFact = resourceFact with { Payload = ((ResourceChangePayload)resourceFact.Payload) with
                        { RelatedFactIds = settlements.ToImmutable() } };
                drafts.Add(resourceFact);
                var rules = GameplayRuleFactProducer.ResourceRules(record.ResourceOutcome, targetMetadata);
                if (rules.IsFailure) return Result<GameplayEffectObservation>.Failure(rules.Error);
                drafts.AddRange(rules.Value);
            }
            if (record.AttributeOutcome != null)
            {
                var fact = GameplayEntityFactProducer.Attribute(record.AttributeOutcome, targetMetadata);
                if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
                drafts.Add(fact.Value);
            }
            if (record.Condensation != null)
            {
                var payload = new RuleResultPayload { RuleId = record.Condensation.Selection.RecipeId, ResultId = "stacks.consumed" };
                var valid = payload.Validate();
                if (valid.IsFailure) return Result<GameplayEffectObservation>.Failure(valid.Error);
                drafts.Add(targetMetadata with { Kind = "effect.condensation.resolved", Payload = payload });
            }
            foreach (var change in record.StackChanges.Where(change => change.Store == EffectStackStore.Status))
            {
                var statusOwnerId = change.Owner?.Kind == GameplayOwnerKind.Entity ? change.Owner.Id : record.TargetEntityId;
                var oldStatus = before.StatusEffects.GetValueOrDefault(statusOwnerId, [])
                    .SingleOrDefault(status => status.InstanceId == change.InstanceId);
                var newStatus = after.StatusEffects.GetValueOrDefault(statusOwnerId, [])
                    .SingleOrDefault(status => status.InstanceId == change.InstanceId);
                var fact = GameplayEntityFactProducer.Status(change, oldStatus, newStatus, targetMetadata);
                if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
                drafts.Add(fact.Value);
            }
            foreach (var change in record.StackChanges.Where(change => change.Store == EffectStackStore.Modifier))
            {
                var fact = GameplayRuleFactProducer.Modifier(change,
                    runBefore?.Modifiers.SingleOrDefault(item => item.InstanceId == change.InstanceId),
                    runAfter?.Modifiers.SingleOrDefault(item => item.InstanceId == change.InstanceId), targetMetadata);
                if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
                drafts.Add(fact.Value);
            }
            foreach (var step in record.CardZoneSteps)
            {
                var source = ResolveZone(step.SourceAddress);
                var target = ResolveZone(step.TargetAddress);
                if (source.IsFailure || target.IsFailure)
                    return Result<GameplayEffectObservation>.Failure(source.IsFailure ? source.Error : target.Error);
                var facts = GameplayCardFactProducer.FromPrimitiveStep(step, source.Value, target.Value, targetMetadata);
                if (facts.IsFailure) return Result<GameplayEffectObservation>.Failure(facts.Error);
                drafts.AddRange(facts.Value);
            }
            if (record.EffectType == EffectType.TURN_SEQUENCE)
            {
                var payload = new SequenceChangePayload { Mutation = command.Definition.SequenceMutation!,
                    Before = before.ActivationState?.Circular!, After = after.ActivationState?.Circular! };
                var valid = payload.Validate();
                if (valid.IsFailure) return Result<GameplayEffectObservation>.Failure(valid.Error);
                drafts.Add(targetMetadata with { Kind = "activation.sequence.changed", Payload = payload });
            }
            // A canonical removal with no matching instance is a resolved attempt, not a missing producer.
            if (drafts.Count == 0 && record.StackChanges.IsEmpty && record.EffectType is
                EffectType.REMOVE_STATUS or EffectType.DISPEL_STATUS or EffectType.REMOVE_MODIFIER)
                drafts.Add(targetMetadata with { Kind = "effect.application.resolved", Payload = new RuleResultPayload
                    { RuleId = command.EffectInstanceId, ResultId = "no-matching-instance" } });
            // Unsupported application families must remain visible instead of inventing ordinary effect facts.
            if (drafts.Count == 0 && record.EffectType != EffectType.CARD_ZONE_FLOW)
            {
                if (current.Unobserved.Length >= EffectExecutionLimits.MaximumSteps)
                    return Result<GameplayEffectObservation>.Failure("Unobserved effect budget exceeded");
                current = current with { Unobserved = current.Unobserved.Add(new(command.EffectInstanceId, record.EffectType, record.TargetEntityId)) };
            }
            string? resourceEventId = null;
            string? condensationEventId = null;
            for (var factIndex = 0; factIndex < drafts.Count; factIndex++)
            {
                var ruleParent = drafts[factIndex].Payload is ResourceThresholdPayload or ActorDefeatChangePayload ? resourceEventId
                    : drafts[factIndex].Payload is StatusChangePayload or ModifierChangePayload ? condensationEventId : null;
                var appended = current.Events.Append(PublisherId, $"{command.EffectInstanceId}/{index}/{factIndex}", drafts[factIndex], ruleParent ?? parentEventId);
                if (appended.IsFailure) return Result<GameplayEffectObservation>.Failure(appended.Error);
                current = current with { Events = appended.Value.Transaction, LastApplicationEventId = appended.Value.Occurrence.EventId };
                if (drafts[factIndex].Payload is ResourceChangePayload) resourceEventId = appended.Value.Occurrence.EventId;
                if (drafts[factIndex].Kind == "effect.condensation.resolved") condensationEventId = appended.Value.Occurrence.EventId;
            }
            if (resourceEventId != null) current = current with { LastApplicationEventId = resourceEventId };
            if (condensationEventId != null) current = current with { LastApplicationEventId = condensationEventId };
            if (record.CalculationInfluenceId != null && resourceEventId != null) settlements.Add(resourceEventId);
        }
        if (command.Identity?.ImpactId is { } impactId && current.LastApplicationEventId is { } eventId)
        {
            if (current.ImpactAnchors.Count >= EffectExecutionLimits.MaximumSteps && !current.ImpactAnchors.ContainsKey(impactId))
                return Result<GameplayEffectObservation>.Failure("Observed impact anchor budget exceeded");
            current = current with { ImpactAnchors = current.ImpactAnchors.SetItem(impactId, eventId) };
        }
        return Result<GameplayEffectObservation>.Success(current);

        Result<GameplayZoneReference?> ResolveZone(string? address)
        {
            if (address == null) return Result<GameplayZoneReference?>.Success(null);
            var zone = runAfter?.Deck.Topology.Zones.GetValueOrDefault(address)
                ?? runBefore?.Deck.Topology.Zones.GetValueOrDefault(address);
            var definition = runAfter?.ResolvedMode?.CardZoneSystem?.Zones.SingleOrDefault(item => item.ZoneId == zone?.Address.ZoneId)
                ?? runBefore?.ResolvedMode?.CardZoneSystem?.Zones.SingleOrDefault(item => item.ZoneId == zone?.Address.ZoneId);
            return zone == null || definition == null ? Result<GameplayZoneReference?>.Failure("Observed zone requires its captured topology and definition")
                : Result<GameplayZoneReference?>.Success(new() { ZoneId = zone.Address.ZoneId, OwnerId = zone.Address.OwnerId,
                    ScopeId = zone.Address.ScopeId, OwnerScope = definition.OwnerScope });
        }

    }
}
