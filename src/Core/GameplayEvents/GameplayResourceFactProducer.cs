using System.Collections.Immutable;
using Core.Common;
using Core.Effects;
using Core.Resources;

namespace Core.GameplayEvents;

/// <summary>Converts an already applied canonical mutation and its actual input. Never derives a rule from resource names.</summary>
public static class GameplayResourceFactProducer
{
    public const string Kind = "resource.change.resolved";

    public static Result<GameplayOccurrenceDraft> FromMutation(ResolvedResourceMutation mutation, ResourceMutationRecord record,
        GameplayObjectReference owner, GameplayOccurrenceDraft metadata)
    {
        if (mutation == null || record == null || owner == null || !owner.IsValid || metadata == null ||
            mutation.MutationId != record.MutationId || mutation.ResourceId != record.ResourceId ||
            mutation.Field != record.Field || mutation.Operation != record.Operation)
            return Result<GameplayOccurrenceDraft>.Failure("Resource fact does not match its canonical mutation input");
        var payload = new ResourceChangePayload
        {
            Owner = owner, ResourceId = record.ResourceId, Field = record.Field, Operation = record.Operation,
            Operand = mutation.Value, Before = record.PreviousValue, RequestedValue = record.RequestedValue, After = record.CurrentValue
        };
        var valid = payload.Validate();
        return valid.IsFailure ? Result<GameplayOccurrenceDraft>.Failure(valid.Error)
            : Result<GameplayOccurrenceDraft>.Success(metadata with { Kind = Kind, Payload = payload });
    }

    public static Result<GameplayOccurrenceDraft> FromEffect(ResolvedEffectCommand command, EffectApplicationRecord record,
        GameplayObjectReference owner, GameplayOccurrenceDraft metadata)
    {
        if (command == null || record == null || metadata == null || owner == null || owner.Id != record.TargetEntityId ||
            record.EffectInstanceId != command.EffectInstanceId || record.ResourceOutcome == null ||
            record.ResourceId == null || record.ResourceField == null || record.ResourceOperation == null)
            return Result<GameplayOccurrenceDraft>.Failure("Effect has no matching canonical resource application");
        var operand = record.ResourceOperation == ResourceMutationOperation.Set ? command.ResolvedValue : MathF.Abs(command.ResolvedValue);
        if (record.CalculationInfluenceId != null)
        {
            var settlements = command.Settlements.Where(item => item.InfluenceId == record.CalculationInfluenceId &&
                item.EntityId == record.TargetEntityId && item.ResourceId == record.ResourceId && item.Field == record.ResourceField).ToArray();
            if (settlements.Length != 1) return Result<GameplayOccurrenceDraft>.Failure("Settlement input is missing or ambiguous");
            operand = settlements[0].Value;
        }
        else if (!command.TargetEntityIds.Contains(record.TargetEntityId, StringComparer.Ordinal) ||
            !StringComparer.OrdinalIgnoreCase.Equals(command.Definition.TargetResource, record.ResourceId) ||
            command.Definition.ResourceField != record.ResourceField ||
            command.Definition.Type is not (EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE))
            return Result<GameplayOccurrenceDraft>.Failure("Primary resource record does not belong to the resolved effect target");
        var mutationId = record.EffectInstanceId;
        var result = FromMutation(new()
        {
            MutationId = mutationId, ResourceId = record.ResourceId, Field = record.ResourceField.Value,
            Operation = record.ResourceOperation.Value, Value = operand
        }, new(mutationId, record.ResourceId, record.ResourceField.Value, record.ResourceOperation.Value,
            record.ResourceOutcome.PreviousValue, record.ResourceOutcome.CurrentValue, record.ResourceOutcome.RequestedValue), owner,
            metadata with { SourceActorId = command.SourceEntityId.Length == 0 ? null : command.SourceEntityId, SubjectActorId = record.TargetEntityId,
                ImpactId = record.Identity?.ImpactId, ProcId = record.Identity?.ProcId, CalculationId = record.CalculationId });
        return result;
    }

    public static Result<GameplayEventTransaction> Append(GameplayEventTransaction transaction,
        IReadOnlyList<ResolvedResourceMutation> mutations, IReadOnlyList<ResourceMutationRecord> records,
        GameplayObjectReference owner, GameplayOccurrenceDraft metadata, string publisherId, string producerPath,
        string? parentEventId = null)
    {
        if (transaction == null || mutations == null || records == null || mutations.Count != records.Count ||
            mutations.Count > EffectExecutionLimits.MaximumSteps)
            return Result<GameplayEventTransaction>.Failure("Observed resource mutation batch is invalid or exceeds budget");
        var inputs = mutations.Take(EffectExecutionLimits.MaximumSteps + 1).ToImmutableArray();
        var outcomes = records.Take(EffectExecutionLimits.MaximumSteps + 1).ToImmutableArray();
        if (inputs.Length != outcomes.Length || inputs.Length > EffectExecutionLimits.MaximumSteps)
            return Result<GameplayEventTransaction>.Failure("Observed resource batch changed shape or exceeded budget");
        var current = transaction;
        for (var index = 0; index < outcomes.Length; index++)
        {
            var draft = FromMutation(inputs[index], outcomes[index], owner, metadata);
            if (draft.IsFailure) return Result<GameplayEventTransaction>.Failure(draft.Error);
            var emitted = current.Append(publisherId, $"{producerPath}/{index}", draft.Value, parentEventId);
            if (emitted.IsFailure) return Result<GameplayEventTransaction>.Failure(emitted.Error);
            current = emitted.Value.Transaction;
        }
        return Result<GameplayEventTransaction>.Success(current);
    }
}

public sealed record ObservedResourceTransition(ResourceSetMutationResult Mutation, GameplayEventTransaction Events);

/// <summary>Explicit shadow observation. Rules remain ResourceSet/ResourceCostTransitions; no event is externally published here.</summary>
public sealed class ObservedResourceTransitions
{
    private readonly IResourceMutationReducer _resources;
    public ObservedResourceTransitions(IResourceMutationReducer? resources = null) => _resources = resources ?? new ResourceMutationReducer();

    public Result<ObservedResourceTransition> Apply(ResourceSet resources, IReadOnlyList<ResolvedResourceMutation> mutations,
        GameplayEventTransaction events, GameplayObjectReference owner, GameplayOccurrenceDraft metadata,
        string publisherId, string producerPath)
    {
        if (resources == null || owner == null || resources.OwnerId != owner.Id || mutations == null)
            return Result<ObservedResourceTransition>.Failure("Observed resource owner does not match its resource set");
        var inputs = mutations.Take(EffectExecutionLimits.MaximumSteps + 1).ToImmutableArray();
        if (inputs.Length > EffectExecutionLimits.MaximumSteps || inputs.Any(item => item == null))
            return Result<ObservedResourceTransition>.Failure("Invalid observed resource input batch");
        var applied = resources.Apply(inputs, _resources);
        return Complete(applied, events, inputs, owner, metadata, publisherId, producerPath);
    }

    public Result<ObservedResourceTransition> Spend(ResourceSet resources, IReadOnlyList<ResolvedResourceCost> costs,
        string transactionId, GameplayEventTransaction events, GameplayObjectReference owner,
        GameplayOccurrenceDraft metadata, string publisherId, string producerPath)
    {
        if (resources == null || owner == null || resources.OwnerId != owner.Id || costs == null)
            return Result<ObservedResourceTransition>.Failure("Observed cost owner does not match its resource set");
        var inputs = costs.Take(EffectExecutionLimits.MaximumSteps + 1).ToImmutableArray();
        if (inputs.Length > EffectExecutionLimits.MaximumSteps || inputs.Any(item => item == null))
            return Result<ObservedResourceTransition>.Failure("Invalid observed cost input batch");
        var applied = ResourceCostTransitions.Spend(resources, inputs, transactionId);
        if (applied.IsFailure) return Result<ObservedResourceTransition>.Failure(applied.Error);
        // Use the actual returned record identity; only the explicitly requested operand comes from the cost.
        var mutations = inputs.Select((cost, index) => new ResolvedResourceMutation
        {
            MutationId = applied.Value.Records[index].MutationId, ResourceId = cost.ResourceId,
            Operation = ResourceMutationOperation.Subtract, Value = cost.Amount
        }).ToImmutableArray();
        return Complete(applied, events, mutations, owner, metadata, publisherId, producerPath);
    }

    private static Result<ObservedResourceTransition> Complete(Result<ResourceSetMutationResult> applied,
        GameplayEventTransaction events, IReadOnlyList<ResolvedResourceMutation> mutations, GameplayObjectReference owner,
        GameplayOccurrenceDraft metadata, string publisherId, string producerPath)
    {
        if (applied.IsFailure) return Result<ObservedResourceTransition>.Failure(applied.Error);
        var emitted = GameplayResourceFactProducer.Append(events, mutations, applied.Value.Records, owner, metadata, publisherId, producerPath);
        return emitted.IsFailure ? Result<ObservedResourceTransition>.Failure(emitted.Error)
            : Result<ObservedResourceTransition>.Success(new(applied.Value, emitted.Value));
    }
}
