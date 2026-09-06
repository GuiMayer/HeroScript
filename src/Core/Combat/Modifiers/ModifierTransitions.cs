using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Run;

namespace Core.Combat.Modifiers;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModifierDurationBoundary { Command, Activation, Round, Combat, Node, Run }

public sealed record ModifierTransition(RunState Run, ScriptModifierInstance Instance);

public static class ModifierTransitions
{
    public static Result<ModifierTransition> Apply(RunState run, ScriptModifierDefinition definition,
        GameplayOwner owner, string? sourceId, int? stacks = null, int? duration = null, Guid? allocatedId = null,
        string? contentRevision = null)
    {
        var revision = contentRevision ?? run.Determinism.ContentRevision;
        if (string.IsNullOrWhiteSpace(definition.ModifierId) || !Enum.IsDefined(owner.Kind) ||
            owner.Kind != GameplayOwnerKind.Global && string.IsNullOrWhiteSpace(owner.Id))
            return Result<ModifierTransition>.Failure("Modifier definition and owner are required");
        var count = stacks ?? definition.DefaultStacks;
        var remaining = duration ?? definition.DefaultDuration;
        var validated = InstancePolicies.Stacks(0, count, definition.MaxStacks, definition.Stacking);
        if (validated.IsFailure || !InstancePolicies.ValidDuration(remaining) ||
            !Enum.IsDefined(definition.DurationBoundary) || !Enum.IsDefined(definition.DurationReapply))
            return Result<ModifierTransition>.Failure("Invalid modifier stacks or duration policy");
        var existing = definition.Stacking == StackReapplyPolicy.Independent ? null : run.Modifiers.FirstOrDefault(item =>
            item.IsActive && item.ModifierId == definition.ModifierId && item.Owner == owner);
        if (existing != null)
        {
            if (existing.ContentRevision != revision)
                return Result<ModifierTransition>.Failure("Cannot merge modifier instances from different revisions");
            var merged = InstancePolicies.Stacks(existing.Stacks, count, definition.MaxStacks, definition.Stacking);
            var timed = InstancePolicies.Duration(existing.Duration, remaining, definition.DefaultDuration, definition.DurationReapply);
            if (merged.IsFailure || timed.IsFailure) return Result<ModifierTransition>.Failure("Modifier reapplication failed");
            var updated = existing with { Stacks = merged.Value, Duration = timed.Value };
            return Result<ModifierTransition>.Success(new(run with { Modifiers = run.Modifiers.Replace(existing, updated) }, updated));
        }
        var allocation = run.Determinism.AllocateId($"modifier:{owner.Kind}:{owner.Id}:{definition.ModifierId}");
        var instance = new ScriptModifierInstance
        {
            InstanceId = allocatedId ?? allocation.Value, ModifierId = definition.ModifierId, Definition = definition,
            Owner = owner, OwnerId = owner.Kind == GameplayOwnerKind.Run ? $"run:{owner.Id}" : owner.Id,
            ContentRevision = revision, SourceId = sourceId,
            Stacks = validated.Value, Duration = remaining
        };
        return Result<ModifierTransition>.Success(new(run with
        {
            Modifiers = run.Modifiers.Add(instance), Determinism = allocatedId.HasValue ? run.Determinism : allocation.Context
        }, instance));
    }

    public static RunState Tick(RunState run, ModifierDurationBoundary boundary, CombatState? combat = null,
        string? actorId = null, IReadOnlySet<Guid>? eligibleIds = null) => run with
    {
        Modifiers = run.Modifiers.Select(item =>
        {
            if (!item.IsActive || item.Duration < 0 || item.Definition.DurationBoundary != boundary ||
                eligibleIds != null && !eligibleIds.Contains(item.InstanceId)) return item;
            if (boundary == ModifierDurationBoundary.Activation &&
                (combat?.GetEntity(actorId ?? "") is not { } actor || !item.Owner.Includes(actor, combat, run))) return item;
            return item with { Duration = item.Duration - 1, IsActive = item.Duration > 1 };
        }).Where(item => item.IsActive).ToImmutableArray()
    };
}
