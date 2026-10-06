using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;

namespace Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectTargetLossPolicy { Fail, Skip, StopRepeat, Retarget }

public sealed record EffectTargetLossDefinition
{
    public EffectTargetLossPolicy Policy { get; init; } = EffectTargetLossPolicy.Fail;
    public EffectTarget? Retarget { get; init; }
}

public sealed record EffectTargetResolution
{
    public ImmutableArray<string> TargetIds { get; init; } = [];
    public ImmutableArray<string> LostTargetIds { get; init; } = [];
    public bool StopRepeat { get; init; }
    public bool Retargeted { get; init; }
    public DeterministicContext Context { get; init; } = null!;
}

/// <summary>Pure targeting; only targets valid at action entry can use loss policies.</summary>
public static class EffectTargetResolver
{
    public static Result<EffectTargetResolution> Resolve(
        CombatState initial, CombatState current, string ownerId,
        IReadOnlyList<string> selection, EffectDefinition effect)
    {
        if (initial.GetActor(ownerId) == null || current.GetActor(ownerId) == null)
            return Result<EffectTargetResolution>.Failure("Trigger owner not found");
        var original = Candidates(initial, ownerId, selection, effect.Target);
        if (original.Length == 0)
            return Result<EffectTargetResolution>.Failure($"Trigger target {effect.Target} resolved no entities");
        if (effect.Target != EffectTarget.SELF && original.Any(id => initial.GetActor(id)?.IsAlive != true))
            return Result<EffectTargetResolution>.Failure("Selection contains an invalid or defeated target");

        var candidates = Candidates(current, ownerId, selection, effect.Target);
        var lost = original.Where(id => current.GetActor(id)?.IsAlive != true).ToImmutableArray();
        if (effect.Target == EffectTarget.SELF) lost = [];
        // Automatic selectors already omit defeated actors. They only need a loss policy
        // when the entire set disappears; explicit selections must account for every id.
        var unavailable = effect.Target == EffectTarget.TARGET ? lost.Length > 0 : candidates.Length == 0;
        if (!unavailable)
            return Select(current, candidates, effect.Target, effect.SelectionResourceId);
        if (effect.TargetLoss.Policy == EffectTargetLossPolicy.Fail)
            return Result<EffectTargetResolution>.Failure("Selection contains an invalid or defeated target");
        if (effect.TargetLoss.Policy == EffectTargetLossPolicy.StopRepeat)
            return Result<EffectTargetResolution>.Success(new()
            { LostTargetIds = lost, StopRepeat = true, Context = current.Determinism });
        if (effect.TargetLoss.Policy == EffectTargetLossPolicy.Skip)
            return Result<EffectTargetResolution>.Success(new()
            {
                TargetIds = candidates.Where(id => current.GetActor(id)?.IsAlive == true).ToImmutableArray(),
                LostTargetIds = lost, Context = current.Determinism
            });

        var selector = effect.TargetLoss.Retarget;
        if (selector == null || selector is EffectTarget.TARGET or EffectTarget.SELF)
            return Result<EffectTargetResolution>.Failure("Retarget requires an automatic target selector");
        var remaining = Candidates(current, ownerId, [], selector.Value);
        var retained = candidates.Where(id => current.GetActor(id)?.IsAlive == true).ToImmutableArray();
        remaining = remaining.Where(id => !retained.Contains(id, StringComparer.Ordinal)).ToImmutableArray();
        if (remaining.Length == 0)
            return Result<EffectTargetResolution>.Success(new()
            { TargetIds = retained, LostTargetIds = lost, StopRepeat = retained.IsEmpty, Context = current.Determinism });
        var redirected = Select(current, remaining, selector.Value, effect.SelectionResourceId);
        return redirected.IsFailure ? redirected : Result<EffectTargetResolution>.Success(redirected.Value with
        { TargetIds = retained.AddRange(redirected.Value.TargetIds), LostTargetIds = lost, Retargeted = true });
    }

    private static ImmutableArray<string> Candidates(CombatState state, string ownerId,
        IReadOnlyList<string> selection, EffectTarget selector)
    {
        var owner = state.GetActor(ownerId)!;
        return selector switch
        {
            EffectTarget.SELF => [ownerId],
            EffectTarget.TARGET => selection.Distinct(StringComparer.Ordinal).ToImmutableArray(),
            EffectTarget.ALL_ENEMIES or EffectTarget.RANDOM_ENEMY or
                EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY => state.GetAllActors()
                    .Where(entity => entity.IsAlive && state.Relationship(owner, entity) == SideRelationship.Enemy)
                    .OrderBy(entity => entity.InstanceId, StringComparer.Ordinal)
                    .Select(entity => entity.InstanceId).ToImmutableArray(),
            EffectTarget.ALL_ALLIES => state.GetAllActors()
                .Where(entity => entity.IsAlive && state.Relationship(owner, entity) == SideRelationship.Ally)
                .OrderBy(entity => entity.InstanceId, StringComparer.Ordinal)
                .Select(entity => entity.InstanceId).ToImmutableArray(),
            _ => []
        };
    }

    private static Result<EffectTargetResolution> Select(CombatState state,
        ImmutableArray<string> candidates, EffectTarget selector, string? resourceId)
    {
        if (selector is EffectTarget.LOWEST_RESOURCE_ENEMY or EffectTarget.HIGHEST_RESOURCE_ENEMY)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
                return Result<EffectTargetResolution>.Failure($"Trigger target {selector} requires selectionResourceId");
            var ranked = candidates.Select(state.GetActor).Where(actor => actor?.GetResource(resourceId) != null)
                .Cast<CombatActorState>();
            var target = selector == EffectTarget.LOWEST_RESOURCE_ENEMY
                ? ranked.OrderBy(actor => actor.GetResource(resourceId)!.Current)
                    .ThenBy(actor => actor.InstanceId, StringComparer.Ordinal).FirstOrDefault()
                : ranked.OrderByDescending(actor => actor.GetResource(resourceId)!.Current)
                    .ThenBy(actor => actor.InstanceId, StringComparer.Ordinal).FirstOrDefault();
            return target == null ? Result<EffectTargetResolution>.Failure($"No candidate exposes selection resource {resourceId}")
                : Result<EffectTargetResolution>.Success(new() { TargetIds = [target.InstanceId], Context = state.Determinism });
        }
        if (selector != EffectTarget.RANDOM_ENEMY)
            return Result<EffectTargetResolution>.Success(new() { TargetIds = candidates, Context = state.Determinism });
        var draw = state.Determinism.DrawInt32(candidates.Length);
        return Result<EffectTargetResolution>.Success(new() { TargetIds = [candidates[draw.Value]], Context = draw.Context });
    }
}
