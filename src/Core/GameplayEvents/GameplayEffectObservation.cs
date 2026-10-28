using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;

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
    public string? ParentEventId { get; init; }
    public string? LastApplicationEventId { get; init; }
    public ImmutableArray<UnobservedEffectApplication> Unobserved { get; init; } = [];

    internal Result<GameplayEffectObservation> Record(ResolvedEffectCommand command, CombatState before, CombatState after,
        IReadOnlyList<EffectApplicationRecord> records)
    {
        if (Events == null || Unobserved.IsDefault || !EventValue.Valid(PublisherId) || !EventValue.Valid(ReasonId) ||
            records.Count > EffectExecutionLimits.MaximumSteps)
            return Result<GameplayEffectObservation>.Failure("Invalid effect observation context");
        var current = this with { LastApplicationEventId = null };
        var metadata = new GameplayOccurrenceDraft
        {
            ReasonId = ReasonId, Attribution = GameplayAttribution.Source,
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
            var targetMetadata = metadata with { SubjectActorId = string.IsNullOrEmpty(record.TargetEntityId) ? null : record.TargetEntityId };
            if (record.ResourceOutcome != null)
            {
                var fact = GameplayResourceFactProducer.FromEffect(command, record,
                    new() { Kind = "entity", Id = record.TargetEntityId, ScopeId = Events.Scope.CombatId ?? Events.Scope.RunId }, targetMetadata);
                if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
                drafts.Add(fact.Value);
            }
            if (record.AttributeOutcome != null)
            {
                var fact = GameplayEntityFactProducer.Attribute(record.AttributeOutcome, targetMetadata);
                if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
                drafts.Add(fact.Value);
            }
            foreach (var change in record.StackChanges.Where(change => change.Store == EffectStackStore.Status))
            {
                var oldStatus = before.StatusEffects.GetValueOrDefault(record.TargetEntityId, [])
                    .SingleOrDefault(status => status.InstanceId == change.InstanceId);
                var newStatus = after.StatusEffects.GetValueOrDefault(record.TargetEntityId, [])
                    .SingleOrDefault(status => status.InstanceId == change.InstanceId);
                var fact = GameplayEntityFactProducer.Status(change, oldStatus, newStatus, targetMetadata);
                if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
                drafts.Add(fact.Value);
            }
            // Partial families are also reported: a stack fact alone does not describe a condensation/zone/modifier action.
            if (drafts.Count == 0 || record.Condensation != null || !record.CardZoneSteps.IsEmpty ||
                record.StackChanges.Any(change => change.Store != EffectStackStore.Status))
            {
                if (current.Unobserved.Length >= EffectExecutionLimits.MaximumSteps)
                    return Result<GameplayEffectObservation>.Failure("Unobserved effect budget exceeded");
                current = current with { Unobserved = current.Unobserved.Add(new(command.EffectInstanceId, record.EffectType, record.TargetEntityId)) };
            }
            for (var factIndex = 0; factIndex < drafts.Count; factIndex++)
            {
                var appended = current.Events.Append(PublisherId, $"{command.EffectInstanceId}/{index}/{factIndex}", drafts[factIndex], ParentEventId);
                if (appended.IsFailure) return Result<GameplayEffectObservation>.Failure(appended.Error);
                current = current with { Events = appended.Value.Transaction, LastApplicationEventId = appended.Value.Occurrence.EventId };
            }
        }
        return Result<GameplayEffectObservation>.Success(current);
    }
}
