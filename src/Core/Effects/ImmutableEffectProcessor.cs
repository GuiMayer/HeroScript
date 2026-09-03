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

    public string EffectInstanceId { get; init; } = string.Empty;
    public EffectDefinition Definition { get; init; } = new();
    public string SourceEntityId { get; init; } = string.Empty;
    public IReadOnlyList<string> TargetEntityIds
    {
        get => _targetEntityIds;
        init => _targetEntityIds = value?.ToImmutableArray() ?? [];
    }
    public float ResolvedValue { get; init; }
    public StatusEffectDefinition? StatusDefinition { get; init; }
    public EffectProvenance Provenance { get; init; } = new();
}

public sealed record EffectApplicationRecord
{
    public string EffectInstanceId { get; init; } = string.Empty;
    public EffectType EffectType { get; init; }
    public string TargetEntityId { get; init; } = string.Empty;
    public string? ResourceId { get; init; }
    public float? PreviousValue { get; init; }
    public float? CurrentValue { get; init; }
    public string? StatusId { get; init; }
    public Guid? StatusInstanceId { get; init; }
    public EffectProvenance Provenance { get; init; } = new();
}

public sealed record EffectBatchResult
{
    private ImmutableArray<EffectApplicationRecord> _records = [];
    private ImmutableArray<CalculationResult> _calculations = [];

    public CombatState State { get; init; } = null!;
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
        if (effect.Definition.Type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE &&
            string.IsNullOrWhiteSpace(effect.Definition.TargetResource))
            return Result.Failure($"Effect {effect.EffectInstanceId} requires targetResource");
        if (effect.Definition.Type == EffectType.APPLY_STATUS && effect.StatusDefinition == null)
            return Result.Failure($"Effect {effect.EffectInstanceId} requires a pinned status definition");
        return Result.Success();
    }

    private Result<EffectTargetApplication> ApplyToTarget(
        CombatState state,
        ResolvedEffectCommand effect,
        string targetId)
    {
        var target = state.GetEntity(targetId);
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
        CombatEntity target,
        ResolvedEffectCommand effect,
        ResourceEffectOperation operation)
    {
        var resourceId = effect.Definition.TargetResource!;
        var resource = target.GetResource(resourceId);
        if (resource == null)
            return Result<EffectTargetApplication>.Failure(
                $"Resource {resourceId} not found on effect target {target.EntityId}");
        var mutationOperation = operation switch
        {
            ResourceEffectOperation.ADD => ResourceMutationOperation.Add,
            ResourceEffectOperation.SUBTRACT => ResourceMutationOperation.Subtract,
            ResourceEffectOperation.SET => ResourceMutationOperation.Set,
            _ => throw new InvalidOperationException($"Unsupported resource operation: {operation}")
        };
        var reduced = _resources.Apply(
            target.ResourceState.Resources,
            [new ResolvedResourceMutation
            {
                MutationId = effect.EffectInstanceId,
                ResourceId = resourceId,
                Field = effect.Definition.ResourceField,
                Operation = mutationOperation,
                Value = effect.ResolvedValue
            }]);
        if (reduced.IsFailure)
            return Result<EffectTargetApplication>.Failure(reduced.Error);
        var updatedPool = reduced.Value.Resources[resourceId];
        var updated = state.ReplaceEntity(target.UpdateResource(resourceId, updatedPool));
        return Result<EffectTargetApplication>.Success(new EffectTargetApplication(
            updated,
            new EffectApplicationRecord
            {
                EffectInstanceId = effect.EffectInstanceId,
                EffectType = effect.Definition.Type,
                TargetEntityId = target.EntityId,
                ResourceId = resourceId,
                PreviousValue = reduced.Value.Records[0].PreviousValue,
                CurrentValue = reduced.Value.Records[0].CurrentValue,
                Provenance = effect.Provenance
            }));
    }

    private static Result<EffectTargetApplication> ApplyStatus(
        CombatState state,
        CombatEntity target,
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
        var statuses = state.StatusEffects.GetValueOrDefault(target.EntityId, []);
        var index = FindStatus(statuses, statusId);
        StatusEffectInstance applied;
        var context = state.Determinism;
        if (index >= 0)
        {
            var existing = statuses[index];
            applied = existing with
            {
                Stacks = System.Math.Min(
                    definition.MaxStacks,
                    existing.Stacks + (effect.Definition.StatusStacks ?? definition.DefaultStacks))
            };
            statuses = statuses.SetItem(index, applied);
        }
        else
        {
            var allocated = context.AllocateId($"status:{target.EntityId}:{statusId}");
            context = allocated.Context;
            applied = new StatusEffectInstance
            {
                InstanceId = allocated.Value,
                StatusId = statusId,
                Definition = definition,
                TargetId = target.EntityId,
                SourceId = effect.SourceEntityId,
                ContentRevision = state.Determinism.ContentRevision,
                Stacks = System.Math.Min(
                    definition.MaxStacks,
                    effect.Definition.StatusStacks ?? definition.DefaultStacks),
                Duration = effect.Definition.StatusDuration ?? definition.DefaultDuration,
                AppliedAt = context.LogicalTimestamp.UtcDateTime,
                TurnApplied = state.CurrentTurn,
                IsActive = true
            };
            statuses = statuses.Add(applied);
        }
        var updated = state with
        {
            StatusEffects = state.StatusEffects.SetItem(target.EntityId, statuses),
            Determinism = context
        };
        return Result<EffectTargetApplication>.Success(new EffectTargetApplication(
            updated,
            new EffectApplicationRecord
            {
                EffectInstanceId = effect.EffectInstanceId,
                EffectType = effect.Definition.Type,
                TargetEntityId = target.EntityId,
                StatusId = statusId,
                StatusInstanceId = applied.InstanceId,
                Provenance = effect.Provenance
            }));
    }

    private static Result<EffectTargetApplication> RemoveStatus(
        CombatState state,
        CombatEntity target,
        ResolvedEffectCommand effect,
        bool removeAll)
    {
        var statuses = state.StatusEffects.GetValueOrDefault(target.EntityId, []);
        string? statusId = null;
        if (removeAll)
        {
            statuses = [];
        }
        else
        {
            statusId = effect.Definition.StatusId;
            if (string.IsNullOrWhiteSpace(statusId))
                return Result<EffectTargetApplication>.Failure(
                    $"Effect {effect.EffectInstanceId} requires statusId");
            statuses = statuses
                .Where(status => !string.Equals(status.StatusId, statusId, StringComparison.Ordinal))
                .ToImmutableArray();
        }
        var updatedStatuses = statuses.IsEmpty
            ? state.StatusEffects.Remove(target.EntityId)
            : state.StatusEffects.SetItem(target.EntityId, statuses);
        return Result<EffectTargetApplication>.Success(new EffectTargetApplication(
            state with { StatusEffects = updatedStatuses },
            new EffectApplicationRecord
            {
                EffectInstanceId = effect.EffectInstanceId,
                EffectType = effect.Definition.Type,
                TargetEntityId = target.EntityId,
                StatusId = removeAll ? "*" : statusId,
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
