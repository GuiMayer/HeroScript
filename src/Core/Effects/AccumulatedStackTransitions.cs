using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Run;

namespace Core.Effects;

/// <summary>Opt-in capability. Being a status/modifier or having ticks does not grant consumption.</summary>
public sealed record StackConsumptionPolicy
{
    public ImmutableArray<string> AllowedRecipeIds { get; init; } = [];

    public static bool IsValid(StackConsumptionPolicy? policy) => policy != null &&
        !policy.AllowedRecipeIds.Any(string.IsNullOrWhiteSpace) &&
        policy.AllowedRecipeIds.Distinct(StringComparer.Ordinal).Count() == policy.AllowedRecipeIds.Length;
}

public sealed record AccumulatedStackSelection
{
    public ImmutableArray<EffectStackStore> Stores { get; init; } = [];
    public GameplayOwner? Owner { get; init; }
    public string? SourceEntityId { get; init; }
    public ImmutableArray<string> DefinitionIds { get; init; } = [];
    public ImmutableArray<string> RequiredTags { get; init; } = [];
    public ImmutableArray<string> ExcludedTags { get; init; } = [];
    public int MaximumInstances { get; init; } = 1024;
}

/// <summary>Immutable reference to existing authoritative storage, not a third stack store.</summary>
public sealed record AccumulatedStackReference
{
    public EffectStackStore Store { get; init; }
    public GameplayOwner Owner { get; init; } = new();
    public Guid InstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public int Stacks { get; init; }
    public int Duration { get; init; }
    public string? SourceEntityId { get; init; }
    public ImmutableArray<string> Tags { get; init; } = [];
    public string StateFingerprint { get; init; } = string.Empty;
    public ImmutableArray<StackPayloadLot> PayloadLots { get; init; } = [];
}

public sealed record StackConsumptionPlan
{
    public string RecipeId { get; init; } = string.Empty;
    public ImmutableArray<AccumulatedStackReference> Sources { get; init; } = [];
}

public sealed record StackConsumptionResult(CombatState Combat, RunState? Run,
    ImmutableArray<EffectStackChange> Changes);

/// <summary>Captures and consumes whole instances from status/modifier snapshots without hooks or RNG.</summary>
public static class AccumulatedStackTransitions
{
    public static Result<StackConsumptionPlan> Capture(CombatState combat, RunState? run, string recipeId,
        AccumulatedStackSelection selection)
    {
        if (string.IsNullOrWhiteSpace(recipeId)) return Result<StackConsumptionPlan>.Failure("Consumption requires a recipe ID");
        if (ValidateSelection(selection).IsFailure)
            return Result<StackConsumptionPlan>.Failure("Invalid accumulated stack selection");
        var candidates = ReadEligible(combat, run, recipeId);
        if (candidates.IsFailure) return Result<StackConsumptionPlan>.Failure(candidates.Error);
        var selected = candidates.Value.Where(source =>
                (selection.Stores.IsEmpty || selection.Stores.Contains(source.Store)) &&
                (selection.Owner == null || selection.Owner == source.Owner) &&
                (selection.SourceEntityId == null || selection.SourceEntityId == source.SourceEntityId) &&
                (selection.DefinitionIds.IsEmpty || selection.DefinitionIds.Contains(source.DefinitionId, StringComparer.Ordinal)) &&
                selection.RequiredTags.All(source.Tags.Contains) && !selection.ExcludedTags.Any(source.Tags.Contains))
            .OrderBy(source => source.Store).ThenBy(source => source.Owner.Kind)
            .ThenBy(source => source.Owner.Id, StringComparer.Ordinal)
            .ThenBy(source => source.InstanceId.ToString(), StringComparer.Ordinal).ToImmutableArray();
        if (selected.Length > selection.MaximumInstances)
            return Result<StackConsumptionPlan>.Failure("Stack selection exceeds its limit; selection was not truncated");
        return Result<StackConsumptionPlan>.Success(new() { RecipeId = recipeId, Sources = selected });
    }

    public static Result ValidateSelection(AccumulatedStackSelection selection) =>
        selection.MaximumInstances is < 1 or > EffectExecutionLimits.MaximumSteps ||
            selection.Stores.Any(store => !Enum.IsDefined(store)) ||
            selection.DefinitionIds.Concat(selection.RequiredTags).Concat(selection.ExcludedTags).Any(string.IsNullOrWhiteSpace) ||
            selection.SourceEntityId != null && string.IsNullOrWhiteSpace(selection.SourceEntityId) ||
            selection.Owner is { } owner && (!Enum.IsDefined(owner.Kind) ||
                owner.Kind != GameplayOwnerKind.Global && string.IsNullOrWhiteSpace(owner.Id))
            ? Result.Failure("Invalid accumulated stack selection") : Result.Success();

    public static Result<StackConsumptionResult> Consume(CombatState combat, RunState? run, StackConsumptionPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.RecipeId) || plan.Sources.Length > EffectExecutionLimits.MaximumSteps ||
            plan.Sources.Select(Key).Distinct().Count() != plan.Sources.Length)
            return Result<StackConsumptionResult>.Failure("Invalid or duplicate stack consumption selection");
        var available = ReadEligible(combat, run, plan.RecipeId);
        if (available.IsFailure) return Result<StackConsumptionResult>.Failure(available.Error);
        var live = available.Value.ToDictionary(Key);
        // Validate every reference before constructing a candidate. Changes in
        // duration, intensity or revision matter even when the count is equal.
        foreach (var source in plan.Sources)
            if (!live.TryGetValue(Key(source), out var current) ||
                CanonicalJson.ComputeHash(source) != CanonicalJson.ComputeHash(current))
                return Result<StackConsumptionResult>.Failure($"Captured stack instance changed or is no longer eligible: {source.InstanceId}");
        var changes = ImmutableArray.CreateBuilder<EffectStackChange>();
        foreach (var source in plan.Sources)
        {
            if (source.Store == EffectStackStore.Status)
            {
                var retained = combat.StatusEffects[source.Owner.Id].Where(status => status.InstanceId != source.InstanceId).ToImmutableArray();
                combat = combat with { StatusEffects = retained.IsEmpty ? combat.StatusEffects.Remove(source.Owner.Id)
                    : combat.StatusEffects.SetItem(source.Owner.Id, retained) };
            }
            else
                run = run! with { Modifiers = run.Modifiers.Where(modifier => modifier.InstanceId != source.InstanceId).ToImmutableArray() };
            changes.Add(new()
            {
                Store = source.Store, InstanceId = source.InstanceId, DefinitionId = source.DefinitionId,
                Owner = source.Owner, PreviousStacks = source.Stacks, CurrentStacks = 0, Reason = EffectStackChangeReason.Consume
            });
        }
        return Result<StackConsumptionResult>.Success(new(combat, run, changes.ToImmutable()));
    }

    private static (EffectStackStore Store, GameplayOwnerKind OwnerKind, string OwnerId, Guid InstanceId) Key(
        AccumulatedStackReference source) => (source.Store, source.Owner.Kind, source.Owner.Id, source.InstanceId);

    private static Result<ImmutableArray<AccumulatedStackReference>> ReadEligible(CombatState combat, RunState? run, string recipeId)
    {
        var references = ImmutableArray.CreateBuilder<AccumulatedStackReference>();
        foreach (var (ownerId, statuses) in combat.StatusEffects.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        foreach (var status in statuses.Where(status => status.IsActive))
        {
            if (!StackConsumptionPolicy.IsValid(status.Definition.Consumption))
                return Result<ImmutableArray<AccumulatedStackReference>>.Failure("Invalid status consumption capability");
            if (!status.Definition.Consumption.AllowedRecipeIds.Contains(recipeId, StringComparer.Ordinal)) continue;
            if (status.InstanceId == Guid.Empty || status.Stacks < 1 || status.TargetId != ownerId ||
                status.StatusId != status.Definition.StatusId || string.IsNullOrWhiteSpace(status.ContentRevision))
                return Result<ImmutableArray<AccumulatedStackReference>>.Failure("Invalid consumable status snapshot");
            references.Add(new()
            {
                Store = EffectStackStore.Status, Owner = new() { Kind = GameplayOwnerKind.Entity, Id = ownerId },
                InstanceId = status.InstanceId, DefinitionId = status.StatusId, ContentRevision = status.ContentRevision,
                Stacks = status.Stacks, Duration = status.Duration, SourceEntityId = status.SourceId,
                Tags = status.Definition.Tags.OrderBy(tag => tag, StringComparer.Ordinal).ToImmutableArray(),
                StateFingerprint = CanonicalJson.ComputeHash(status), PayloadLots = status.PayloadLots
            });
        }
        foreach (var modifier in run?.Modifiers.Where(modifier => modifier.IsActive) ?? [])
        {
            if (!StackConsumptionPolicy.IsValid(modifier.Definition.Consumption))
                return Result<ImmutableArray<AccumulatedStackReference>>.Failure("Invalid modifier consumption capability");
            if (!modifier.Definition.Consumption.AllowedRecipeIds.Contains(recipeId, StringComparer.Ordinal)) continue;
            if (modifier.InstanceId == Guid.Empty || modifier.Stacks < 1 ||
                modifier.ModifierId != modifier.Definition.ModifierId || string.IsNullOrWhiteSpace(modifier.ContentRevision))
                return Result<ImmutableArray<AccumulatedStackReference>>.Failure("Invalid consumable modifier snapshot");
            references.Add(new()
            {
                Store = EffectStackStore.Modifier, Owner = modifier.Owner, InstanceId = modifier.InstanceId,
                DefinitionId = modifier.ModifierId, ContentRevision = modifier.ContentRevision,
                Stacks = modifier.Stacks, Duration = modifier.Duration, SourceEntityId = modifier.SourceId,
                Tags = modifier.Definition.Tags.OrderBy(tag => tag, StringComparer.Ordinal).ToImmutableArray(),
                StateFingerprint = CanonicalJson.ComputeHash(modifier), PayloadLots = modifier.PayloadLots
            });
        }
        var items = references.ToImmutable();
        return items.Select(source => (source.Store, source.InstanceId)).Distinct().Count() != items.Length
            ? Result<ImmutableArray<AccumulatedStackReference>>.Failure("Duplicate authoritative stack instance")
            : Result<ImmutableArray<AccumulatedStackReference>>.Success(items);
    }
}
