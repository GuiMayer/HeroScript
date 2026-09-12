using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Resources;
using Core.StatusEffects;
using Core.Calculations;

namespace Core.Effects;

public enum EffectProvenanceKind
{
    Card,
    Status,
    Relic,
    Ability,
    GameMode,
    Encounter,
    Rule
}

/// <summary>
/// Diagnostic origin. The processor records this value but never uses it to
/// choose execution behavior.
/// </summary>
public sealed record EffectProvenance
{
    public EffectProvenanceKind Kind { get; init; }
    public string SourceId { get; init; } = string.Empty;
    public string? ComponentId { get; init; }
}

/// <summary>
/// Fully resolved, source-agnostic effect command. Conditions, chance,
/// targeting and amount calculation have already produced explicit data.
/// </summary>
public sealed record ResolvedEffectCommand
{
    private ImmutableArray<string> _targetEntityIds = [];
    private ImmutableArray<ResolvedCalculationSettlement> _settlements = [];

    public string EffectInstanceId { get; init; } = string.Empty;
    public EffectDefinition Definition { get; init; } = new();
    public string SourceEntityId { get; init; } = string.Empty;
    public IReadOnlyList<string> TargetEntityIds
    {
        get => _targetEntityIds;
        init => _targetEntityIds = value?.ToImmutableArray() ?? [];
    }
    public float ResolvedValue { get; init; }
    public CalculationResult? Calculation { get; init; }
    public IReadOnlyList<ResolvedCalculationSettlement> Settlements
    {
        get => _settlements;
        init => _settlements = value?.ToImmutableArray() ?? [];
    }
    public StatusEffectDefinition? StatusDefinition { get; init; }
    public string? ContentRevision { get; init; }
    public EffectProvenance Provenance { get; init; } = new();
}

public sealed record EffectApplicationRecord
{
    public string EffectInstanceId { get; init; } = string.Empty;
    public EffectType EffectType { get; init; }
    public string TargetEntityId { get; init; } = string.Empty;
    public string? ResourceId { get; init; }
    public ResourceValueField? ResourceField { get; init; }
    public ResourceMutationOperation? ResourceOperation { get; init; }
    public float? PreviousValue { get; init; }
    public float? CurrentValue { get; init; }
    public string? StatusId { get; init; }
    public Guid? StatusInstanceId { get; init; }
    public ImmutableArray<Guid> RemovedStatusInstanceIds { get; init; } = [];
    public ImmutableArray<Guid> CardInstanceIds { get; init; } = [];
    public Guid? ModifierInstanceId { get; init; }
    public string? ModifierId { get; init; }
    public ImmutableArray<Guid> RemovedModifierInstanceIds { get; init; } = [];
    public ImmutableArray<ModifierStackApplicationRecord> ModifierStackChanges { get; init; } = [];
    public string? CalculationId { get; init; }
    public string? CalculationFingerprint { get; init; }
    public string? CalculationInfluenceId { get; init; }
    public EffectProvenance Provenance { get; init; } = new();
}

public sealed record ModifierStackApplicationRecord
{
    public Guid ModifierInstanceId { get; init; }
    public string ModifierId { get; init; } = string.Empty;
    public int PreviousStacks { get; init; }
    public int CurrentStacks { get; init; }
    public bool Removed { get; init; }
}

public sealed record EffectBatchResult
{
    private ImmutableArray<EffectApplicationRecord> _records = [];
    private ImmutableArray<CalculationResult> _calculations = [];

    public CombatState State { get; init; } = null!;
    public Core.Run.RunState? Run { get; init; }
    public ImmutableArray<EffectExecutionStep> Steps { get; init; } = [];
    public IReadOnlyList<EffectApplicationRecord> Records
    {
        get => _records;
        init => _records = value?.ToImmutableArray() ?? [];
    }
    public string Fingerprint { get; init; } = string.Empty;
    public IReadOnlyList<CalculationResult> Calculations
    {
        get => _calculations;
        init => _calculations = value?.ToImmutableArray() ?? [];
    }
}

public interface IImmutableEffectProcessor
{
    Result<EffectBatchResult> Apply(
        CombatState state,
        IReadOnlyList<ResolvedEffectCommand> effects);
}

/// <summary>
/// Atomic immutable reducer for resolved combat effects. Resource names and
/// provenance are data; only the explicit effect operation selects behavior.
/// </summary>
public sealed class ImmutableEffectProcessor : IImmutableEffectProcessor
{
    private readonly IResourceMutationReducer _resources;

    public ImmutableEffectProcessor(IResourceMutationReducer? resources = null)
    {
        _resources = resources ?? new ResourceMutationReducer();
    }

    public Result<EffectBatchResult> Apply(
        CombatState state,
        IReadOnlyList<ResolvedEffectCommand> effects)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(effects);
        var current = state;
        var records = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        foreach (var effect in effects)
        {
            var validation = Validate(effect);
            if (validation.IsFailure)
                return Result<EffectBatchResult>.Failure(validation.Error);
            foreach (var settlement in effect.Settlements)
            {
                var appliedSettlement = ApplySettlement(current, effect, settlement);
                if (appliedSettlement.IsFailure)
                    return Result<EffectBatchResult>.Failure(appliedSettlement.Error);
                current = appliedSettlement.Value.State;
                records.Add(appliedSettlement.Value.Record);
            }
            foreach (var targetId in effect.TargetEntityIds)
            {
                var applied = ApplyToTarget(current, effect, targetId);
                if (applied.IsFailure)
                    return Result<EffectBatchResult>.Failure(applied.Error);
                current = applied.Value.State;
                records.Add(applied.Value.Record);
            }
        }

        var recordArray = records.ToImmutable();
        return Result<EffectBatchResult>.Success(new EffectBatchResult
        {
            State = current,
            Records = recordArray,
            Fingerprint = CanonicalJson.ComputeHash(new
            {
                stateHash = CanonicalJson.ComputeHash(current),
                records = recordArray
            })
        });
    }

    private static Result Validate(ResolvedEffectCommand effect)
    {
        if (string.IsNullOrWhiteSpace(effect.EffectInstanceId))
            return Result.Failure("EffectInstanceId is required");
        if (effect.TargetEntityIds.Count == 0)
            return Result.Failure($"Effect {effect.EffectInstanceId} requires at least one target");
        if (effect.TargetEntityIds.Distinct(StringComparer.Ordinal).Count() != effect.TargetEntityIds.Count)
            return Result.Failure($"Effect {effect.EffectInstanceId} contains duplicate targets");
        if (float.IsNaN(effect.ResolvedValue) || float.IsInfinity(effect.ResolvedValue))
            return Result.Failure($"Effect {effect.EffectInstanceId} value must be finite");
        if (effect.Definition.Type is EffectType.DAMAGE or EffectType.HEAL && effect.ResolvedValue < 0)
            return Result.Failure("DAMAGE and HEAL require non-negative amounts");
        if (effect.Definition.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE &&
            string.IsNullOrWhiteSpace(effect.Definition.TargetResource))
            return Result.Failure($"Effect {effect.EffectInstanceId} requires targetResource");
        if (effect.Definition.Type == EffectType.APPLY_STATUS && effect.StatusDefinition == null)
            return Result.Failure($"Effect {effect.EffectInstanceId} requires a pinned status definition");
        if (effect.Settlements.Count > 0 && effect.TargetEntityIds.Count != 1)
            return Result.Failure($"Effect {effect.EffectInstanceId} settlements require exactly one resolved target");
        if (effect.Settlements.Any(item => string.IsNullOrWhiteSpace(item.SettlementId) ||
                string.IsNullOrWhiteSpace(item.InfluenceId) || string.IsNullOrWhiteSpace(item.EntityId) ||
                string.IsNullOrWhiteSpace(item.ResourceId) || !float.IsFinite(item.Value) || item.Value < 0 ||
                !Enum.IsDefined(item.Field) || !Enum.IsDefined(item.Operation)))
            return Result.Failure($"Effect {effect.EffectInstanceId} contains an invalid calculation settlement");
        return Result.Success();
    }

    private Result<EffectTargetApplication> ApplySettlement(
        CombatState state,
        ResolvedEffectCommand effect,
        ResolvedCalculationSettlement settlement)
    {
        var target = state.GetActor(settlement.EntityId);
        if (target == null)
            return Result<EffectTargetApplication>.Failure(
                $"Settlement entity not found: {settlement.EntityId}");
        var mutationOperation = settlement.Operation switch
        {
            ResourceEffectOperation.ADD => ResourceMutationOperation.Add,
            ResourceEffectOperation.SUBTRACT => ResourceMutationOperation.Subtract,
            ResourceEffectOperation.SET => ResourceMutationOperation.Set,
            _ => throw new InvalidOperationException($"Unsupported settlement operation: {settlement.Operation}")
        };
        var reduced = target.ResourceState.Apply([new ResolvedResourceMutation
        {
            MutationId = settlement.SettlementId,
            ResourceId = settlement.ResourceId,
            Field = settlement.Field,
            Operation = mutationOperation,
            Value = settlement.Value
        }], _resources);
        if (reduced.IsFailure)
            return Result<EffectTargetApplication>.Failure(reduced.Error);
        var record = reduced.Value.Records[0];
        return Result<EffectTargetApplication>.Success(new(
            state.ReplaceActor(target.WithResourceState(reduced.Value.State)),
            new EffectApplicationRecord
            {
                EffectInstanceId = effect.EffectInstanceId,
                EffectType = effect.Definition.Type,
                TargetEntityId = target.InstanceId,
                ResourceId = settlement.ResourceId,
                ResourceField = record.Field,
                ResourceOperation = record.Operation,
                PreviousValue = record.PreviousValue,
                CurrentValue = record.CurrentValue,
                CalculationId = effect.Calculation?.CalculationId,
                CalculationFingerprint = effect.Calculation?.Fingerprint,
                CalculationInfluenceId = settlement.InfluenceId,
                Provenance = effect.Provenance
            }));
    }

    private Result<EffectTargetApplication> ApplyToTarget(
        CombatState state,
        ResolvedEffectCommand effect,
        string targetId)
    {
        var target = state.GetActor(targetId);
        if (target == null)
            return Result<EffectTargetApplication>.Failure($"Effect target not found: {targetId}");
        return effect.Definition.Type switch
        {
            EffectType.DAMAGE => ApplyResource(
                state,
                target,
                effect,
                ResourceEffectOperation.SUBTRACT),
            EffectType.HEAL => ApplyResource(
                state,
                target,
                effect,
                ResourceEffectOperation.ADD),
            EffectType.MODIFY_RESOURCE => ApplyResource(
                state,
                target,
                effect,
                effect.Definition.Operation),
            EffectType.APPLY_STATUS => ApplyStatus(state, target, effect),
            EffectType.REMOVE_STATUS => RemoveStatus(state, target, effect, removeAll: false),
            EffectType.DISPEL_STATUS => RemoveStatus(state, target, effect, removeAll: true),
            _ => Result<EffectTargetApplication>.Failure(
                $"Effect type is not supported by immutable processor: {effect.Definition.Type}")
        };
    }

    private Result<EffectTargetApplication> ApplyResource(
        CombatState state,
        CombatActorState target,
        ResolvedEffectCommand effect,
        ResourceEffectOperation operation)
    {
        var resourceId = effect.Definition.TargetResource!;
        var resource = target.GetResource(resourceId);
        if (resource == null)
            return Result<EffectTargetApplication>.Failure(
                $"Resource {resourceId} not found on effect target {target.InstanceId}");
        var mutationOperation = operation switch
        {
            ResourceEffectOperation.ADD => ResourceMutationOperation.Add,
            ResourceEffectOperation.SUBTRACT => ResourceMutationOperation.Subtract,
            ResourceEffectOperation.SET => ResourceMutationOperation.Set,
            _ => throw new InvalidOperationException($"Unsupported resource operation: {operation}")
        };
        // Resolve signed authoring into an explicit direction and magnitude.
        // The resource reducer never guesses direction from a value's sign.
        var magnitude = effect.ResolvedValue;
        if (magnitude < 0 && mutationOperation != ResourceMutationOperation.Set)
        {
            magnitude = -magnitude;
            mutationOperation = mutationOperation == ResourceMutationOperation.Add
                ? ResourceMutationOperation.Subtract : ResourceMutationOperation.Add;
        }
        var reduced = target.ResourceState.Apply(
            [new ResolvedResourceMutation
            {
                MutationId = effect.EffectInstanceId,
                ResourceId = resourceId,
                Field = effect.Definition.ResourceField,
                Operation = mutationOperation,
                Value = magnitude
            }],
            _resources);
        if (reduced.IsFailure)
            return Result<EffectTargetApplication>.Failure(reduced.Error);
        var updated = state.ReplaceActor(target.WithResourceState(reduced.Value.State));
        return Result<EffectTargetApplication>.Success(new EffectTargetApplication(
            updated,
            new EffectApplicationRecord
            {
                EffectInstanceId = effect.EffectInstanceId,
                EffectType = effect.Definition.Type,
                TargetEntityId = target.InstanceId,
                ResourceId = resourceId,
                ResourceField = reduced.Value.Records[0].Field,
                ResourceOperation = reduced.Value.Records[0].Operation,
                PreviousValue = reduced.Value.Records[0].PreviousValue,
                CurrentValue = reduced.Value.Records[0].CurrentValue,
                CalculationId = effect.Calculation?.CalculationId,
                CalculationFingerprint = effect.Calculation?.Fingerprint,
                Provenance = effect.Provenance
            }));
    }

    private static Result<EffectTargetApplication> ApplyStatus(
        CombatState state,
        CombatActorState target,
        ResolvedEffectCommand effect)
    {
        var definition = effect.StatusDefinition!;
        var statusId = string.IsNullOrWhiteSpace(effect.Definition.StatusId)
            ? definition.StatusId
            : effect.Definition.StatusId;
        if (string.IsNullOrWhiteSpace(statusId) ||
            !string.Equals(statusId, definition.StatusId, StringComparison.Ordinal))
            return Result<EffectTargetApplication>.Failure(
                $"Pinned status definition does not match effect statusId: {statusId}");
        if (definition.MaxStacks < 1)
            return Result<EffectTargetApplication>.Failure($"Status {statusId} maxStacks must be positive");
        var incomingStacks = effect.Definition.StatusStacks ?? definition.DefaultStacks;
        var incomingDuration = effect.Definition.StatusDuration ?? definition.DefaultDuration;
        var validatedStacks = InstancePolicies.Stacks(0, incomingStacks, definition.MaxStacks, definition.Stacking);
        if (validatedStacks.IsFailure || !InstancePolicies.ValidDuration(incomingDuration) ||
            !InstancePolicies.ValidDuration(definition.DefaultDuration) || !Enum.IsDefined(definition.DurationReapply))
            return Result<EffectTargetApplication>.Failure($"Status {statusId} has invalid stacks or duration policy");
        var statuses = state.StatusEffects.GetValueOrDefault(target.InstanceId, []);
        var index = definition.Stacking == StackReapplyPolicy.Independent ? -1 : FindStatus(statuses, statusId);
        StatusEffectInstance applied;
        var context = state.Determinism;
        if (index >= 0)
        {
            var existing = statuses[index];
            if (existing.ContentRevision != null && existing.ContentRevision != (effect.ContentRevision ?? context.ContentRevision))
                return Result<EffectTargetApplication>.Failure($"Cannot merge different revisions of status {statusId}; remove or create an independent instance");
            var mergedStacks = InstancePolicies.Stacks(existing.Stacks, incomingStacks, definition.MaxStacks, definition.Stacking);
            var mergedDuration = InstancePolicies.Duration(existing.Duration, incomingDuration,
                definition.DefaultDuration, definition.DurationReapply);
            if (mergedStacks.IsFailure || mergedDuration.IsFailure)
                return Result<EffectTargetApplication>.Failure($"Status {statusId} reapplication failed");
            applied = existing with
            {
                Stacks = mergedStacks.Value,
                Duration = mergedDuration.Value
            };
            statuses = statuses.SetItem(index, applied);
        }
        else
        {
            var allocated = context.AllocateId($"status:{target.InstanceId}:{statusId}");
            context = allocated.Context;
            applied = new StatusEffectInstance
            {
                InstanceId = allocated.Value,
                StatusId = statusId,
                Definition = definition,
                TargetId = target.InstanceId,
                SourceId = effect.SourceEntityId,
                ContentRevision = effect.ContentRevision ?? state.Determinism.ContentRevision,
                Stacks = validatedStacks.Value,
                Duration = incomingDuration,
                AppliedAt = context.LogicalTimestamp.UtcDateTime,
                TurnApplied = state.CurrentTurn,
                IsActive = true
            };
            statuses = statuses.Add(applied);
        }
        var updated = state with
        {
            StatusEffects = state.StatusEffects.SetItem(target.InstanceId, statuses),
            Determinism = context
        };
        return Result<EffectTargetApplication>.Success(new EffectTargetApplication(
            updated,
            new EffectApplicationRecord
            {
                EffectInstanceId = effect.EffectInstanceId,
                EffectType = effect.Definition.Type,
                TargetEntityId = target.InstanceId,
                StatusId = statusId,
                StatusInstanceId = applied.InstanceId,
                Provenance = effect.Provenance
            }));
    }

    private static Result<EffectTargetApplication> RemoveStatus(
        CombatState state,
        CombatActorState target,
        ResolvedEffectCommand effect,
        bool removeAll)
    {
        var statuses = state.StatusEffects.GetValueOrDefault(target.InstanceId, []);
        var statusId = effect.Definition.StatusId;
        var filter = effect.Definition.Dispel;
        if (!removeAll && string.IsNullOrWhiteSpace(statusId))
            return Result<EffectTargetApplication>.Failure($"Effect {effect.EffectInstanceId} requires statusId");
        if (filter.MaximumInstances < 1 || !Enum.IsDefined(filter.Order))
            return Result<EffectTargetApplication>.Failure("Invalid dispel policy");
        var candidates = statuses.Where(status => removeAll
            ? status.Definition.Dispellable &&
                (filter.StatusIds.IsEmpty || filter.StatusIds.Contains(status.StatusId, StringComparer.Ordinal)) &&
                filter.RequiredTags.All(tag => status.Definition.Tags.Contains(tag, StringComparer.Ordinal)) &&
                (filter.SourceEntityId == null || status.SourceId == filter.SourceEntityId)
            : status.StatusId == statusId);
        var ordered = filter.Order switch
        {
            DispelOrder.OldestFirst => candidates.OrderBy(status => status.AppliedAt).ThenBy(status => status.InstanceId),
            DispelOrder.NewestFirst => candidates.OrderByDescending(status => status.AppliedAt).ThenBy(status => status.InstanceId),
            DispelOrder.HighestPriorityFirst => candidates.OrderByDescending(status => status.Definition.Priority).ThenBy(status => status.InstanceId),
            _ => candidates.OrderBy(status => status.InstanceId)
        };
        var removed = ordered.Take(filter.MaximumInstances).Select(status => status.InstanceId).ToImmutableArray();
        statuses = statuses.Where(status => !removed.Contains(status.InstanceId)).ToImmutableArray();
        var updatedStatuses = statuses.IsEmpty
            ? state.StatusEffects.Remove(target.InstanceId)
            : state.StatusEffects.SetItem(target.InstanceId, statuses);
        return Result<EffectTargetApplication>.Success(new EffectTargetApplication(
            state with { StatusEffects = updatedStatuses },
            new EffectApplicationRecord
            {
                EffectInstanceId = effect.EffectInstanceId,
                EffectType = effect.Definition.Type,
                TargetEntityId = target.InstanceId,
                StatusId = removeAll ? "*" : statusId,
                RemovedStatusInstanceIds = removed,
                Provenance = effect.Provenance
            }));
    }

    private static int FindStatus(
        ImmutableArray<StatusEffectInstance> statuses,
        string statusId)
    {
        for (var index = 0; index < statuses.Length; index++)
        {
            if (statuses[index].IsActive &&
                string.Equals(statuses[index].StatusId, statusId, StringComparison.Ordinal))
                return index;
        }
        return -1;
    }

    private sealed record EffectTargetApplication(
        CombatState State,
        EffectApplicationRecord Record);
}
